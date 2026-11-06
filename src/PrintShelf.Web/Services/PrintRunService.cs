using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Services;

public sealed record PrintRunInput(
    string Printer,
    string? Material,
    string? Color,
    decimal? NozzleMm,
    decimal? LayerMm,
    int? DurationMin,
    string Result,
    decimal? Cost,
    string? Notes,
    DateTimeOffset PrintedAt);

public sealed record PrintRunResult(bool Ok, string? Error, PrintRun? Run);

public sealed record PrintAggregate(
    int Runs,
    int Successes,
    int Failures,
    int Partial,
    int TotalMinutes,
    decimal TotalCost,
    DateTimeOffset? LastPrinted)
{
    public double SuccessRate => Runs == 0 ? 0 : Successes * 100.0 / Runs;
    public string TotalTimeDisplay => TotalMinutes >= 60
        ? $"{TotalMinutes / 60}h {TotalMinutes % 60}m"
        : $"{TotalMinutes}m";
}

/// <summary>Print-run log CRUD + per-model aggregates (P5, FR-09).</summary>
public sealed class PrintRunService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<IReadOnlyList<PrintRun>> GetForModelAsync(Guid modelId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PrintRuns.AsNoTracking()
            .Where(r => r.ModelId == modelId)
            .OrderByDescending(r => r.PrintedAt)
            .ToListAsync(ct);
    }

    public static PrintAggregate Aggregate(IEnumerable<PrintRun> runs)
    {
        var list = runs as IReadOnlyCollection<PrintRun> ?? runs.ToList();
        if (list.Count == 0)
            return new PrintAggregate(0, 0, 0, 0, 0, 0, null);

        return new PrintAggregate(
            list.Count,
            list.Count(r => r.Result == PrintResults.Success),
            list.Count(r => r.Result == PrintResults.Failed),
            list.Count(r => r.Result == PrintResults.Partial),
            list.Sum(r => r.DurationMin ?? 0),
            list.Sum(r => r.Cost ?? 0),
            list.Max(r => r.PrintedAt));
    }

    public async Task<PrintRunResult> AddAsync(Guid modelId, PrintRunInput input, CancellationToken ct = default)
    {
        string? error = Validate(input);
        if (error is not null)
            return new PrintRunResult(false, error, null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Models.AnyAsync(m => m.Id == modelId, ct))
            return new PrintRunResult(false, "Model not found.", null);

        var run = new PrintRun { ModelId = modelId, Printer = input.Printer.Trim() };
        Apply(run, input);
        db.PrintRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return new PrintRunResult(false, "Could not save the print run: " + (ex.InnerException?.Message ?? ex.Message), null);
        }
        return new PrintRunResult(true, null, run);
    }

    public async Task<PrintRunResult> UpdateAsync(Guid id, PrintRunInput input, CancellationToken ct = default)
    {
        string? error = Validate(input);
        if (error is not null)
            return new PrintRunResult(false, error, null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.PrintRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
            return new PrintRunResult(false, "Print run not found.", null);

        Apply(run, input);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            return new PrintRunResult(false, "Could not save the print run: " + (ex.InnerException?.Message ?? ex.Message), null);
        }
        return new PrintRunResult(true, null, run);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var run = await db.PrintRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
            return false;
        db.PrintRuns.Remove(run);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PrintAggregate> GetAggregateAsync(Guid modelId, CancellationToken ct = default)
    {
        var runs = await GetForModelAsync(modelId, ct);
        return Aggregate(runs);
    }

    public async Task<IReadOnlyList<string>> SuggestMaterialsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PrintRuns.AsNoTracking()
            .Where(r => r.Material != null && r.Material != "")
            .Select(r => r.Material!)
            .Distinct().OrderBy(m => m).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SuggestPrintersAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PrintRuns.AsNoTracking()
            .Select(r => r.Printer)
            .Distinct().OrderBy(p => p).ToListAsync(ct);
    }

    private static void Apply(PrintRun run, PrintRunInput input)
    {
        run.Printer = input.Printer.Trim();
        run.Material = NullIfBlank(input.Material);
        run.Color = NullIfBlank(input.Color);
        run.NozzleMm = input.NozzleMm;
        run.LayerMm = input.LayerMm;
        run.DurationMin = input.DurationMin;
        run.Result = input.Result;
        run.Cost = input.Cost;
        run.Notes = NullIfBlank(input.Notes);
        run.PrintedAt = (input.PrintedAt == default ? DateTimeOffset.UtcNow : input.PrintedAt).ToUniversalTime();
    }

    private static string? Validate(PrintRunInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Printer))
            return "Printer is required.";
        if (input.Printer.Trim().Length > 120)
            return "Printer name is too long (max 120).";
        if (!PrintResults.IsValid(input.Result))
            return "Result must be success, failed or partial.";
        if (input.NozzleMm is <= 0 or > 10)
            return "Nozzle diameter must be between 0.01 and 10 mm.";
        if (input.LayerMm is <= 0 or > 10)
            return "Layer height must be between 0.001 and 10 mm.";
        if (input.DurationMin is < 0)
            return "Duration cannot be negative.";
        if (input.Cost is < 0)
            return "Cost cannot be negative.";
        return null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
