namespace PrintShelf.Stl;

/// <summary>
/// Machine-readable failure categories for STL ingest (docs/05-testing.md §1).
/// Every rejection must name one of these so uploads can show a specific reason.
/// </summary>
public enum StlErrorCode
{
    EmptyFile,
    NotAnStl,
    Truncated,
    BadTriangleCount,
    TrailingData,
    NonFiniteVertex,
    MalformedAscii,
    TooManyTriangles
}

/// <summary>A rejected STL file. <see cref="Code"/> is what tests assert on.</summary>
public sealed class StlParseException : Exception
{
    public StlErrorCode Code { get; }

    public StlParseException(StlErrorCode code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// Geometry statistics computed by a full pass over the mesh
/// (docs/03-research-and-reuse.md §2, A-05).
/// Dimensions are in whatever unit the file claims — STL has none (R-06).
/// </summary>
public sealed record StlMesh(
    int TriangleCount,
    bool IsBinary,
    double MinX, double MinY, double MinZ,
    double MaxX, double MaxY, double MaxZ,
    double SignedVolume,
    double SurfaceArea,
    int DegenerateTriangles,
    IReadOnlyList<string> Warnings)
{
    public double SizeX => MaxX - MinX;
    public double SizeY => MaxY - MinY;
    public double SizeZ => MaxZ - MinZ;

    /// <summary>Absolute volume estimate in mm³ when the file is in mm.</summary>
    public double VolumeMm3 => Math.Abs(SignedVolume);

    /// <summary>True when the winding produced a negative signed volume (R-07).</summary>
    public bool HasInvertedWinding => SignedVolume < 0 && TriangleCount > 0;
}
