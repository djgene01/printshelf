namespace PrintShelf.Web.Data.Entities;

/// <summary>
/// One recorded attempt to print a model (docs/02-architecture.md §3, P5).
/// Result is constrained at DB level to the values in <see cref="PrintResults"/>.
/// </summary>
public class PrintRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ModelId { get; set; }
    public StlModel Model { get; set; } = null!;

    public required string Printer { get; set; }
    public string? Material { get; set; }
    public string? Color { get; set; }

    public decimal? NozzleMm { get; set; }
    public decimal? LayerMm { get; set; }
    public int? DurationMin { get; set; }

    /// <summary>'success' | 'failed' | 'partial' — enforced by CK_print_runs_result.</summary>
    public string Result { get; set; } = PrintResults.Success;

    /// <summary>Optional user-entered cost (filament + electricity), not computed.</summary>
    public decimal? Cost { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset PrintedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PrintResults
{
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Partial = "partial";

    public static readonly string[] All = [Success, Failed, Partial];

    public static bool IsValid(string? value) => value is Success or Failed or Partial;
}
