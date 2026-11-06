using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Stl;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Services;

public enum UploadStatus
{
    Added,
    Duplicate,
    Skipped,
    Rejected,
    Failed
}

public sealed record DuplicateInfo(Guid Id, string Name);

/// <summary>Result of staging + validating one uploaded file.</summary>
public sealed class UploadResult
{
    public required UploadStatus Status { get; init; }
    public required string FileName { get; init; }
    public required string Message { get; init; }
    public long Bytes { get; init; }
    public StlMesh? Mesh { get; init; }
    public StlModel? Model { get; init; }
    public string? DuplicateToken { get; init; }
    public IReadOnlyList<DuplicateInfo> Duplicates { get; init; } = [];
}

public sealed record PendingUpload(
    string TempPath,
    string Sha256,
    string FileName,
    long Bytes,
    StlMesh Mesh,
    DateTimeOffset ExpiresAt,
    string? Category = null);

/// <summary>
/// Holds staged duplicate files between "already in library" detection and the
/// user's keep-both/skip choice (FR-03). Tokens are one-shot and expire.
/// </summary>
public sealed class PendingUploadStore
{
    private readonly ConcurrentDictionary<string, PendingUpload> _items = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public string Add(PendingUpload upload)
    {
        Sweep();
        string token = Guid.NewGuid().ToString("N");
        _items[token] = upload with { ExpiresAt = DateTimeOffset.UtcNow + Lifetime };
        return token;
    }

    public PendingUpload? Take(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        if (_items.TryRemove(token, out var upload) && upload.ExpiresAt > DateTimeOffset.UtcNow)
            return upload;
        if (upload is not null)
            TryDelete(upload.TempPath);
        return null;
    }

    private void Sweep()
    {
        foreach (var (key, value) in _items)
        {
            if (value.ExpiresAt <= DateTimeOffset.UtcNow && _items.TryRemove(key, out _))
                TryDelete(value.TempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}

/// <summary>
/// Upload pipeline (docs/02 §4–5): temp -> parse/validate -> SHA-256 -> dedupe ->
/// content-addressed move -> DB row. File first, row second: a crash leaves a
/// harmless orphan, never a dangling row.
/// </summary>
public sealed class IngestService(
    IDbContextFactory<AppDbContext> dbFactory,
    StorageService storage,
    PendingUploadStore pending,
    UploadOptions uploadOptions,
    ILogger<IngestService> logger)
{
    public long MaxBytes => uploadOptions.MaxBytes;

    /// <summary>Stage, hash, parse and either commit or return a duplicate decision.</summary>
    public async Task<UploadResult> IngestAsync(
        Stream source,
        string originalFileName,
        long declaredBytes,
        IProgress<long>? progress = null,
        CancellationToken ct = default,
        string? category = null)
    {
        string safeName = Path.GetFileName(originalFileName ?? string.Empty);
        string displayName = DisplayNameFrom(safeName);

        if (!string.Equals(Path.GetExtension(safeName), ".stl", StringComparison.OrdinalIgnoreCase))
            return Reject(safeName, "Only .stl files are accepted (binary or ASCII STL).", 0);

        if (declaredBytes > MaxBytes)
            return Reject(safeName, $"File is larger than the {uploadOptions.MaxBytesDisplay} upload limit.", declaredBytes);

        if (declaredBytes == 0)
            return Reject(safeName, "The file is empty.", 0);

        storage.EnsureDirectories();
        string tempPath = storage.NewTempFile();
        long total = 0;
        string sha;

        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 1 << 17, useAsync: true))
            {
                byte[] buffer = new byte[1 << 17]; // 128 KB
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        await temp.DisposeAsync();
                        TryDelete(tempPath);
                        return Reject(safeName,
                            $"File is larger than the {uploadOptions.MaxBytesDisplay} upload limit.", total);
                    }

                    hasher.AppendData(buffer, 0, read);
                    await temp.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(total);
                }
            }

            sha = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();

            StlMesh mesh;
            try
            {
                mesh = StlParser.ParseFile(tempPath);
            }
            catch (StlParseException ex)
            {
                TryDelete(tempPath);
                logger.LogInformation("Rejected STL {File}: {Code} {Message}", safeName, ex.Code, ex.Message);
                return Reject(safeName, Describe(ex), total);
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                logger.LogWarning(ex, "Failed to parse {File}", safeName);
                return Reject(safeName, "The file could not be read as an STL.", total);
            }

            List<DuplicateInfo> duplicates;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                duplicates = await db.Models.AsNoTracking()
                    .Where(m => m.FileSha256 == sha)
                    .OrderBy(m => m.Name)
                    .Select(m => new DuplicateInfo(m.Id, m.Name))
                    .ToListAsync(ct);
            }

            if (duplicates.Count > 0)
            {
                string token = pending.Add(new PendingUpload(tempPath, sha, displayName, total, mesh,
                    DateTimeOffset.UtcNow.AddMinutes(30), string.IsNullOrWhiteSpace(category) ? null : category.Trim()));
                return new UploadResult
                {
                    Status = UploadStatus.Duplicate,
                    FileName = safeName,
                    Message = $"Already in the library as “{duplicates[0].Name}”.",
                    Bytes = total,
                    Mesh = mesh,
                    DuplicateToken = token,
                    Duplicates = duplicates
                };
            }

            var model = await CommitAsync(tempPath, sha, displayName, total, mesh, ct, category);
            return new UploadResult
            {
                Status = UploadStatus.Added,
                FileName = safeName,
                Message = $"Added “{model.Name}”.",
                Bytes = total,
                Mesh = mesh,
                Model = model
            };
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            logger.LogError(ex, "Upload failed for {File}", safeName);
            return Reject(safeName, "Upload failed: " + ex.Message, total);
        }
    }

    /// <summary>Resolve a pending duplicate: keep both (second row, shared file) or skip.</summary>
    public async Task<UploadResult> ResolveDuplicateAsync(string? token, bool keepBoth, CancellationToken ct = default)
    {
        var upload = pending.Take(token);
        if (upload is null)
            return new UploadResult
            {
                Status = UploadStatus.Failed,
                FileName = string.Empty,
                Message = "That upload expired. Please upload the file again."
            };

        if (!keepBoth)
        {
            TryDelete(upload.TempPath);
            return new UploadResult
            {
                Status = UploadStatus.Skipped, // resolved: nothing added
                FileName = upload.FileName,
                Message = $"Skipped “{upload.FileName}” (already in library).",
                Bytes = upload.Bytes
            };
        }

        // The fingerprint file may already exist; keep-both shares it (dedupe by content).
        string target = storage.LibraryFileFor(upload.Sha256);
        if (!File.Exists(target) && File.Exists(upload.TempPath))
            File.Move(upload.TempPath, target);
        else
            TryDelete(upload.TempPath);

        var model = await AddRowAsync(upload.Sha256, upload.FileName, upload.Bytes, upload.Mesh, ct, upload.Category);
        return new UploadResult
        {
            Status = UploadStatus.Added,
            FileName = upload.FileName,
            Message = $"Kept both — added “{model.Name}”.",
            Bytes = upload.Bytes,
            Model = model,
            Mesh = upload.Mesh
        };
    }

    /// <summary>Copy a server-side sample into the library (used by the seeder).</summary>
    public async Task<StlModel> ImportFileAsync(string sourcePath, string displayName, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(sourcePath);
        var result = await IngestAsync(stream, displayName + ".stl", new FileInfo(sourcePath).Length, null, ct);
        if (result.Status == UploadStatus.Added && result.Model is not null)
            return result.Model;
        if (result.Status == UploadStatus.Duplicate)
        {
            var resolved = await ResolveDuplicateAsync(result.DuplicateToken, keepBoth: true, ct);
            if (resolved.Model is not null)
                return resolved.Model;
        }
        throw new InvalidOperationException($"Could not import {sourcePath}: {result.Message}");
    }

    private async Task<StlModel> CommitAsync(
        string tempPath, string sha, string displayName, long bytes, StlMesh mesh, CancellationToken ct,
        string? category = null)
    {
        string target = storage.LibraryFileFor(sha);
        if (File.Exists(target))
            TryDelete(tempPath); // orphan/content already there
        else
            File.Move(tempPath, target);

        return await AddRowAsync(sha, displayName, bytes, mesh, ct, category);
    }

    private async Task<StlModel> AddRowAsync(
        string sha, string displayName, long bytes, StlMesh mesh, CancellationToken ct,
        string? category = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        string name = await UniqueNameAsync(db, displayName, ct);
        var model = new StlModel
        {
            Name = name,
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            FileSha256 = sha,
            FileBytes = bytes,
            TriCount = mesh.TriangleCount,
            BboxX = Round3(mesh.SizeX),
            BboxY = Round3(mesh.SizeY),
            BboxZ = Round3(mesh.SizeZ),
            VolumeMm3 = Round3(mesh.VolumeMm3),
            Format = "stl",
            PrintUnit = "mm",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Models.Add(model);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Added model {Name} ({Sha}) {Triangles} triangles", name, sha, mesh.TriangleCount);
        return model;
    }

    private static async Task<string> UniqueNameAsync(AppDbContext db, string baseName, CancellationToken ct)
    {
        string candidate = baseName;
        for (int suffix = 2; suffix < 500; suffix++)
        {
            bool exists = await db.Models.AsNoTracking().AnyAsync(m => m.Name == candidate, ct);
            if (!exists)
                return candidate;
            candidate = $"{baseName} ({suffix})";
        }
        return $"{baseName} ({Guid.NewGuid():N}"[..60];
    }

    private static decimal Round3(double value) =>
        Math.Round((decimal)value, 3, MidpointRounding.AwayFromZero);

    private static string DisplayNameFrom(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (name.Length == 0)
            return "Untitled";
        return name.Length > 180 ? name[..180] : name;
    }

    private static string Describe(StlParseException ex) => ex.Code switch
    {
        StlErrorCode.EmptyFile => "The file is empty.",
        StlErrorCode.NotAnStl => "This is not an STL file.",
        StlErrorCode.Truncated => "The STL file is truncated (incomplete). " + ex.Message,
        StlErrorCode.BadTriangleCount => "The STL header declares an impossible triangle count.",
        StlErrorCode.TrailingData => "The STL has unexpected trailing bytes.",
        StlErrorCode.NonFiniteVertex => "The STL contains NaN/Infinity coordinates.",
        StlErrorCode.MalformedAscii => "The ASCII STL is malformed. " + ex.Message,
        _ => ex.Message
    };

    private static UploadResult Reject(string fileName, string message, long bytes) => new()
    {
        Status = UploadStatus.Rejected,
        FileName = fileName,
        Message = message,
        Bytes = bytes
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
