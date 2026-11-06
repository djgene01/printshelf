namespace PrintShelf.Web.Services;

/// <summary>Client-side pre-checks so obviously wrong files never reach the server (FR-02).</summary>
public static class UploadValidation
{
    public static string? Validate(string? fileName, long bytes, long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "No file name.";
        if (!string.Equals(Path.GetExtension(fileName), ".stl", StringComparison.OrdinalIgnoreCase))
            return "Only .stl files are accepted (binary or ASCII STL).";
        if (bytes == 0)
            return "The file is empty.";
        if (bytes > maxBytes)
            return $"File is larger than the {maxBytes / (1024d * 1024d):0.#} MB upload limit.";
        return null;
    }
}
