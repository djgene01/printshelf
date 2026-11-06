namespace PrintShelf.Web.Data.Entities;

/// <summary>Catalogued 3D model file (docs/02-architecture.md §3).</summary>
public class StlModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }

    /// <summary>Additive for later formats; MVP is 'stl' only (decision D3).</summary>
    public string Format { get; set; } = "stl";

    /// <summary>SHA-256 hex of the file; links to data/library/{sha}.stl [A-01].</summary>
    public required string FileSha256 { get; set; }
    public long FileBytes { get; set; }

    // Geometry stats, computed at upload [A-05]; 0 until P2 ingest lands.
    public int TriCount { get; set; }
    public decimal BboxX { get; set; }
    public decimal BboxY { get; set; }
    public decimal BboxZ { get; set; }
    public decimal? VolumeMm3 { get; set; }

    /// <summary>STL has no unit metadata; user-declared (R-06). Default mm.</summary>
    public string PrintUnit { get; set; } = "mm";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Soft delete (P1); purge page in P6.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ModelTag> ModelTags { get; set; } = new List<ModelTag>();
    public ICollection<PrintRun> PrintRuns { get; set; } = new List<PrintRun>();
}
