using PrintShelf.Stl;

namespace PrintShelf.Web.Services;

/// <summary>On-disk layout (docs/02-architecture.md §4). Relative paths resolve against the content root.</summary>
public sealed class StorageOptions
{
    public string LibraryPath { get; set; } = "data/library";
    public string ThumbPath { get; set; } = "data/thumbs";
    public string TempPath { get; set; } = "data/tmp";
}

/// <summary>Upload cap [A-02][U-04]. 200 MB default.</summary>
public sealed class UploadOptions
{
    public long MaxBytes { get; set; } = 200L * 1024 * 1024;

    public string MaxBytesDisplay => $"{MaxBytes / (1024d * 1024d):0.#} MB";
}

/// <summary>
/// Content-addressed storage. Filenames are always the server-computed SHA-256,
/// never a user-supplied name (path-traversal safety, docs/02 §4).
/// </summary>
public sealed class StorageService
{
    private readonly string _root;

    public StorageOptions Options { get; }

    public StorageService(StorageOptions options, string contentRoot)
    {
        Options = options;
        _root = contentRoot;
    }

    public string LibraryDir => Resolve(Options.LibraryPath);
    public string ThumbDir => Resolve(Options.ThumbPath);
    public string TempDir => Resolve(Options.TempPath);

    private string Resolve(string path) =>
        Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(_root, path));

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(LibraryDir);
        Directory.CreateDirectory(ThumbDir);
        Directory.CreateDirectory(TempDir);
    }

    public static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    /// <summary>data/library/{sha256}.stl — server-computed hex only.</summary>
    public string LibraryFileFor(string sha256Hex)
    {
        if (!IsSha256Hex(sha256Hex))
            throw new ArgumentException("Not a sha256 hex digest.", nameof(sha256Hex));
        return Path.Combine(LibraryDir, sha256Hex.ToLowerInvariant() + ".stl");
    }

    /// <summary>data/thumbs/{sha256}.png.</summary>
    public string ThumbFileFor(string sha256Hex)
    {
        if (!IsSha256Hex(sha256Hex))
            throw new ArgumentException("Not a sha256 hex digest.", nameof(sha256Hex));
        return Path.Combine(ThumbDir, sha256Hex.ToLowerInvariant() + ".png");
    }

    public bool ThumbExists(string sha256Hex) =>
        IsSha256Hex(sha256Hex) && File.Exists(ThumbFileFor(sha256Hex));

    public long ThumbBytes(string sha256Hex) =>
        ThumbExists(sha256Hex) ? new FileInfo(ThumbFileFor(sha256Hex)).Length : 0;

    public bool LibraryFileExists(string sha256Hex) =>
        IsSha256Hex(sha256Hex) && File.Exists(LibraryFileFor(sha256Hex));

    public string NewTempFile() =>
        Path.Combine(TempDir, Guid.NewGuid().ToString("N") + ".stl");

    /// <summary>Atomically write a PNG captured by the browser; rejects non-PNG payloads.</summary>
    public async Task SaveThumbnailAsync(string sha256Hex, Stream png, CancellationToken ct = default)
    {
        string target = ThumbFileFor(sha256Hex);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fs = File.Create(temp))
                await png.CopyToAsync(fs, ct);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public void DeleteLibraryFileIfUnreferenced(string sha256Hex)
    {
        string path = LibraryFileFor(sha256Hex);
        if (File.Exists(path))
            File.Delete(path);
        DeleteThumb(sha256Hex);
    }

    public void DeleteThumb(string sha256Hex)
    {
        string path = ThumbFileFor(sha256Hex);
        if (File.Exists(path))
            File.Delete(path);
    }

    public IReadOnlyList<FileInfo> ListLibraryFiles() =>
        Directory.Exists(LibraryDir)
            ? new DirectoryInfo(LibraryDir).GetFiles("*.stl", SearchOption.TopDirectoryOnly)
            : [];

    public IReadOnlyList<FileInfo> ListThumbFiles() =>
        Directory.Exists(ThumbDir)
            ? new DirectoryInfo(ThumbDir).GetFiles("*.png", SearchOption.TopDirectoryOnly)
            : [];

    public int CleanTempFiles(TimeSpan olderThan)
    {
        if (!Directory.Exists(TempDir))
            return 0;
        int removed = 0;
        DateTime cutoff = DateTime.UtcNow - olderThan;
        foreach (var file in new DirectoryInfo(TempDir).GetFiles("*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    file.Delete();
                    removed++;
                }
            }
            catch (IOException) { /* in use by an active upload */ }
        }
        return removed;
    }
}
