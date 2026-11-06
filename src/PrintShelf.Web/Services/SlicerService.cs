using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;

namespace PrintShelf.Web.Services;

/// <summary>Optional explicit executable paths. Standard Windows install locations are detected automatically.</summary>
public sealed class SlicerOptions
{
    public string? OrcaPath { get; set; }
    public string? PrusaPath { get; set; }
    public string? CuraPath { get; set; }
}

/// <summary>A supported slicer and the executable found on this PC.</summary>
public sealed record SlicerInstall(string Key, string Name, string? ExecutablePath)
{
    public bool Available => !string.IsNullOrWhiteSpace(ExecutablePath);
}

/// <summary>
/// Finds locally installed desktop slicers. Detection is intentionally boring:
/// check the explicit setting/env var, then the usual Windows install roots.
/// It never launches anything.
/// </summary>
public sealed class SlicerLocator
{
    private readonly SlicerOptions _options;

    public SlicerLocator(SlicerOptions options) => _options = options;

    public IReadOnlyList<SlicerInstall> Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return
            [
                new SlicerInstall("orca", "OrcaSlicer", null),
                new SlicerInstall("prusa", "PrusaSlicer", null),
                new SlicerInstall("cura", "Cura", null)
            ];
        }

        return
        [
            new SlicerInstall("orca", "OrcaSlicer",
                Find(_options.OrcaPath, "PRINTSHELF_ORCA_PATH", OrcaCandidates())),
            new SlicerInstall("prusa", "PrusaSlicer",
                Find(_options.PrusaPath, "PRINTSHELF_PRUSA_PATH", PrusaCandidates())),
            new SlicerInstall("cura", "Cura",
                Find(_options.CuraPath, "PRINTSHELF_CURA_PATH", CuraCandidates()))
        ];
    }

    private static string? Find(string? configured, string environmentVariable, IEnumerable<string> candidates)
    {
        string? environmentPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (Existing(environmentPath) is { } fromEnvironment)
            return fromEnvironment;
        if (Existing(configured) is { } fromConfiguration)
            return fromConfiguration;

        foreach (string candidate in candidates)
        {
            if (Existing(candidate) is { } found)
                return found;
        }
        return null;
    }

    private static string? Existing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            string full = Path.GetFullPath(path.Trim());
            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static IEnumerable<string> OrcaCandidates()
    {
        foreach (string programFiles in ProgramRoots())
        {
            yield return Path.Combine(programFiles, "OrcaSlicer", "orca-slicer.exe");
            yield return Path.Combine(programFiles, "OrcaSlicer", "OrcaSlicer.exe");
        }

        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        yield return Path.Combine(localPrograms, "OrcaSlicer", "orca-slicer.exe");
        yield return Path.Combine(localPrograms, "OrcaSlicer", "OrcaSlicer.exe");
    }

    private static IEnumerable<string> PrusaCandidates()
    {
        foreach (string programFiles in ProgramRoots())
        {
            yield return Path.Combine(programFiles, "Prusa3D", "PrusaSlicer", "prusa-slicer.exe");
            yield return Path.Combine(programFiles, "PrusaSlicer", "prusa-slicer.exe");
        }

        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        yield return Path.Combine(localPrograms, "PrusaSlicer", "prusa-slicer.exe");
    }

    private static IEnumerable<string> CuraCandidates()
    {
        foreach (string programFiles in ProgramRoots())
        {
            foreach (string directory in CuraDirectories(programFiles))
            {
                yield return Path.Combine(directory, "UltiMaker-Cura.exe");
                yield return Path.Combine(directory, "Ultimaker-Cura.exe");
                yield return Path.Combine(directory, "Cura.exe");
            }
        }

        string localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        foreach (string directory in CuraDirectories(localPrograms))
        {
            yield return Path.Combine(directory, "UltiMaker-Cura.exe");
            yield return Path.Combine(directory, "Ultimaker-Cura.exe");
            yield return Path.Combine(directory, "Cura.exe");
        }
    }

    private static IEnumerable<string> CuraDirectories(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        IEnumerable<string> matches;
        try
        {
            matches = Directory.GetDirectories(root, "UltiMaker Cura*", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetDirectories(root, "Ultimaker Cura*", SearchOption.TopDirectoryOnly));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string directory in matches.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            yield return directory;
    }

    private static IEnumerable<string> ProgramRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }
}

/// <summary>Starts a slicer process with the model path as its only argument.</summary>
public interface ISlicerProcessStarter
{
    void Start(string executablePath, string filePath);
}

public sealed class SlicerProcessStarter : ISlicerProcessStarter
{
    public void Start(string executablePath, string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
        };
        startInfo.ArgumentList.Add(filePath);

        using var process = Process.Start(startInfo);
    }
}

public enum SlicerLaunchStatus
{
    Launched,
    SlicerUnavailable,
    ModelNotFound,
    FileMissing,
    Failed
}

public sealed record SlicerLaunchResult(SlicerLaunchStatus Status, string Message)
{
    public bool Success => Status == SlicerLaunchStatus.Launched;
}

/// <summary>
/// Resolves a catalogue model to its content-addressed STL and hands that one
/// file to an allow-listed slicer executable. The path is never supplied by the client.
/// </summary>
public sealed class SlicerLaunchService
{
    private readonly SlicerLocator _locator;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly StorageService _storage;
    private readonly ISlicerProcessStarter _starter;

    public SlicerLaunchService(
        SlicerLocator locator,
        IDbContextFactory<AppDbContext> dbFactory,
        StorageService storage,
        ISlicerProcessStarter starter)
    {
        _locator = locator;
        _dbFactory = dbFactory;
        _storage = storage;
        _starter = starter;
    }

    public async Task<SlicerLaunchResult> LaunchAsync(Guid modelId, string? slicerKey, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(SlicerLaunchStatus.SlicerUnavailable, "Slicer launch is available on Windows only.");

        SlicerInstall? slicer = _locator.Detect()
            .FirstOrDefault(s => string.Equals(s.Key, slicerKey, StringComparison.OrdinalIgnoreCase));

        if (slicer is null)
            return new(SlicerLaunchStatus.SlicerUnavailable, "Choose a supported slicer first.");
        if (!slicer.Available)
            return new(SlicerLaunchStatus.SlicerUnavailable, $"{slicer.Name} is not installed on this PC.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var model = await db.Models.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modelId, ct);
        if (model is null)
            return new(SlicerLaunchStatus.ModelNotFound, "This model no longer exists.");

        string filePath;
        try
        {
            filePath = _storage.LibraryFileFor(model.FileSha256.Trim());
        }
        catch (ArgumentException)
        {
            return new(SlicerLaunchStatus.FileMissing, "The stored file path is invalid.");
        }

        if (!File.Exists(filePath))
            return new(SlicerLaunchStatus.FileMissing, "The STL file is missing from local storage.");
        if (!File.Exists(slicer.ExecutablePath))
            return new(SlicerLaunchStatus.SlicerUnavailable, $"{slicer.Name} could not be found. It may have been uninstalled.");

        try
        {
            _starter.Start(slicer.ExecutablePath!, filePath);
            return new(SlicerLaunchStatus.Launched, $"Opening “{model.Name}” in {slicer.Name}.");
        }
        catch (Exception ex)
        {
            return new(SlicerLaunchStatus.Failed, $"Could not launch {slicer.Name}: {ex.Message}");
        }
    }
}
