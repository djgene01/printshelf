using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Services;

/// <summary>
/// Catalogue reads/writes: search/filter/sort (P4), metadata (P1), soft delete,
/// integrity scan and hard purge (P2/P6). All queries are server-side EF.
/// </summary>
public sealed class CatalogService(
    IDbContextFactory<AppDbContext> dbFactory,
    StorageService storage,
    CategoryService categories)
{
    private static readonly string[] SortKeys =
        ["newest", "oldest", "name", "name_desc", "size", "size_desc", "dims"];

    public static IReadOnlyList<(string Key, string Label)> SortOptions { get; } =
    [
        ("newest", "Newest first"),
        ("oldest", "Oldest first"),
        ("name", "Name A→Z"),
        ("name_desc", "Name Z→A"),
        ("size", "Smallest file"),
        ("size_desc", "Largest file"),
        ("dims", "Biggest build volume")
    ];

    public async Task<PagedResult<ModelSummary>> SearchAsync(CatalogQuery query, CancellationToken ct = default)
    {
        string sort = SortKeys.Contains(query.Sort) ? query.Sort : "newest";
        int pageSize = Math.Clamp(query.PageSize, 1, 200);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<StlModel> q = db.Models.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
            q = q.Where(m =>
                EF.Functions.ILike(m.Name, pattern, "\\") ||
                (m.Description != null && EF.Functions.ILike(m.Description, pattern, "\\")) ||
                m.ModelTags.Any(mt => EF.Functions.ILike(mt.Tag.Label, pattern, "\\")));
        }

        if (query.TagId is { } tagId)
            q = q.Where(m => m.ModelTags.Any(mt => mt.TagId == tagId));

        if (!string.IsNullOrWhiteSpace(query.Category))
            q = q.Where(m => m.Category == query.Category);

        int total = await q.CountAsync(ct);
        int totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        int page = Math.Clamp(query.Page, 1, totalPages);

        q = sort switch
        {
            "oldest" => q.OrderBy(m => m.CreatedAt).ThenBy(m => m.Name),
            "name" => q.OrderBy(m => m.Name),
            "name_desc" => q.OrderByDescending(m => m.Name),
            "size" => q.OrderBy(m => m.FileBytes),
            "size_desc" => q.OrderByDescending(m => m.FileBytes),
            "dims" => q.OrderByDescending(m => m.BboxX * m.BboxY * m.BboxZ),
            _ => q.OrderByDescending(m => m.CreatedAt)
        };

        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.Category,
                m.FileSha256,
                m.FileBytes,
                m.TriCount,
                m.BboxX,
                m.BboxY,
                m.BboxZ,
                m.VolumeMm3,
                m.PrintUnit,
                m.CreatedAt,
                Tags = m.ModelTags.Select(mt => mt.Tag.Label).ToList(),
                PrintCount = m.PrintRuns.Count
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new ModelSummary(
            r.Id, r.Name, r.Category, r.FileSha256.Trim(), r.FileBytes, r.TriCount,
            r.BboxX, r.BboxY, r.BboxZ, r.VolumeMm3, r.PrintUnit, r.CreatedAt,
            r.Tags, r.PrintCount, storage.ThumbExists(r.FileSha256))).ToList();

        return new PagedResult<ModelSummary>(items, total, page, pageSize);
    }

    public async Task<CatalogFacets> GetFacetsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var defined = await db.Categories.AsNoTracking()
            .Select(c => c.Name)
            .ToListAsync(ct);
        var used = await db.Models.AsNoTracking()
            .Where(m => m.Category != null && m.Category != "")
            .Select(m => m.Category!)
            .Distinct()
            .ToListAsync(ct);
        var categories = defined
            .Union(used, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tags = await db.Tags.AsNoTracking()
            .Where(t => t.ModelTags.Any(mt => mt.Model.DeletedAt == null))
            .OrderBy(t => t.Label)
            .Select(t => new TagUsage(t.Id, t.Slug, t.Label,
                t.ModelTags.Count(mt => mt.Model.DeletedAt == null)))
            .ToListAsync(ct);

        return new CatalogFacets(categories, tags);
    }

    public async Task<StlModel?> GetModelAsync(Guid id, bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<StlModel> q = db.Models.AsNoTracking();
        if (includeDeleted)
            q = q.IgnoreQueryFilters();

        var model = await q
            .AsSplitQuery()
            .Include(m => m.ModelTags).ThenInclude(mt => mt.Tag)
            .Include(m => m.PrintRuns)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (model is not null)
            model.PrintRuns = model.PrintRuns.OrderByDescending(r => r.PrintedAt).ToList();
        return model;
    }

    public async Task<bool> UpdateMetadataAsync(Guid id, ModelEditInput input, CancellationToken ct = default)
    {
        string name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return false;
        if (name.Length > 180)
            name = name[..180];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var model = await db.Models.Include(m => m.ModelTags)
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null)
            return false;

        model.Name = name;
        model.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        model.Category = string.IsNullOrWhiteSpace(input.Category) ? null : input.Category.Trim();
        model.PrintUnit = input.PrintUnit is "in" or "cm" or "mm" ? input.PrintUnit : "mm";
        model.UpdatedAt = DateTimeOffset.UtcNow;

        var desired = input.TagIds.Distinct().ToHashSet();
        foreach (var link in model.ModelTags.Where(mt => !desired.Contains(mt.TagId)).ToList())
            model.ModelTags.Remove(link);

        var existing = model.ModelTags.Select(mt => mt.TagId).ToHashSet();
        foreach (Guid tagId in desired.Where(t => !existing.Contains(t)))
            model.ModelTags.Add(new ModelTag { ModelId = id, TagId = tagId });

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> AssignCategoryAsync(IReadOnlyCollection<Guid> ids, string? category, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return 0;

        string? target = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (target is { Length: > 80 })
            throw new InvalidOperationException("Category name is too long.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (target is not null &&
            !await db.Categories.AsNoTracking().AnyAsync(c => c.Name == target, ct))
        {
            throw new InvalidOperationException("That category no longer exists.");
        }

        Guid[] selected = ids.Distinct().ToArray();
        return await db.Models
            .Where(m => selected.Contains(m.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Category, target)
                .SetProperty(m => m.UpdatedAt, DateTimeOffset.UtcNow), ct);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var model = await db.Models.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null)
            return false;
        model.DeletedAt = DateTimeOffset.UtcNow;
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var model = await db.Models.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (model is null)
            return false;
        model.DeletedAt = null;
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<DeletedModel>> GetDeletedAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Models.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.DeletedAt != null)
            .OrderByDescending(m => m.DeletedAt)
            .Select(m => new DeletedModel(m.Id, m.Name, m.FileSha256, m.FileBytes, m.DeletedAt!.Value))
            .ToListAsync(ct);
    }

    public async Task<DashboardData> GetDashboardAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        int models = await db.Models.CountAsync(ct);
        int tags = await db.Tags.CountAsync(ct);
        long totalBytes = await db.Models.SumAsync(m => (long?)m.FileBytes, ct) ?? 0;
        int prints = await db.PrintRuns.CountAsync(ct);
        int successes = await db.PrintRuns.CountAsync(r => r.Result == PrintResults.Success, ct);

        var recent = await SearchAsync(new CatalogQuery(Page: 1, PageSize: 6), ct);

        var groups = await db.PrintRuns.AsNoTracking()
            .GroupBy(r => r.ModelId)
            .Select(g => new
            {
                ModelId = g.Key,
                Runs = g.Count(),
                Successes = g.Count(r => r.Result == PrintResults.Success)
            })
            .OrderByDescending(x => x.Runs)
            .Take(5)
            .ToListAsync(ct);

        var ids = groups.Select(g => g.ModelId).ToList();
        var names = await db.Models.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.FileSha256 })
            .ToDictionaryAsync(m => m.Id, ct);

        var mostPrinted = groups
            .Where(g => names.ContainsKey(g.ModelId))
            .Select(g => new MostPrinted(g.ModelId, names[g.ModelId].Name,
                names[g.ModelId].FileSha256.Trim(), g.Runs, g.Successes))
            .ToList();

        return new DashboardData(models, tags, totalBytes, prints, successes, recent.Items, mostPrinted);
    }

    public async Task<IReadOnlyList<ThumbCandidate>> ListMissingPreviewsAsync(int max = 5000, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Models.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Id, m.Name, m.FileSha256, m.Category })
            .ToListAsync(ct);

        var thumbs = storage.ListThumbFiles()
            .Select(f => Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant())
            .ToHashSet();
        var colors = await categories.ColorMapAsync(ct);

        return rows
            .Where(r => !thumbs.Contains(r.FileSha256.Trim().ToLowerInvariant()))
            .Take(Math.Clamp(max, 1, 20000))
            .Select(r => new ThumbCandidate(r.Id, r.Name, ColorFor(colors, r.Category)))
            .ToList();
    }

    public async Task<IReadOnlyList<ThumbCandidate>> ListAllPreviewsAsync(int max = 20000, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Models.AsNoTracking()
            .Where(m => m.DeletedAt == null)
            .OrderBy(m => m.CreatedAt)
            .Take(Math.Clamp(max, 1, 20000))
            .Select(m => new { m.Id, m.Name, m.Category })
            .ToListAsync(ct);
        var colors = await categories.ColorMapAsync(ct);
        return rows
            .Select(r => new ThumbCandidate(r.Id, r.Name, ColorFor(colors, r.Category)))
            .ToList();
    }

    private static string ColorFor(IReadOnlyDictionary<string, string> colors, string? category) =>
        category is not null && colors.TryGetValue(category, out string? color)
            ? color
            : CategoryService.DefaultColor;

    public async Task<IntegrityReport> ScanAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Models.IgnoreQueryFilters().AsNoTracking()
            .Select(m => new { m.Name, m.FileSha256, m.DeletedAt })
            .ToListAsync(ct);

        var files = storage.ListLibraryFiles();
        var thumbs = storage.ListThumbFiles();
        var fileShas = files.Select(f => Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant()).ToHashSet();
        var thumbShas = thumbs.Select(f => Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant()).ToHashSet();
        var rowShas = rows.Select(r => r.FileSha256.Trim().ToLowerInvariant()).ToHashSet();

        var dangling = rows
            .Where(r => !fileShas.Contains(r.FileSha256.Trim().ToLowerInvariant()))
            .Select(r => $"{r.Name} — {r.FileSha256[..12]}…{(r.DeletedAt is null ? "" : " (soft-deleted)")}")
            .ToList();

        var orphans = files
            .Where(f => !rowShas.Contains(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant()))
            .Select(f => new OrphanFile(f.Name, f.Length))
            .OrderByDescending(f => f.Bytes)
            .ToList();

        var live = rows.Where(r => r.DeletedAt is null).ToList();
        var missingThumbs = live
            .Where(r => !thumbShas.Contains(r.FileSha256.Trim().ToLowerInvariant()))
            .Select(r => r.Name)
            .ToList();

        var unreferencedThumbs = thumbs
            .Where(f => !rowShas.Contains(Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant()))
            .Select(f => f.Name)
            .ToList();

        long tempBytes = 0;
        if (Directory.Exists(storage.TempDir))
            tempBytes = new DirectoryInfo(storage.TempDir).GetFiles().Sum(f => f.Length);

        return new IntegrityReport(
            live.Count,
            rows.Count - live.Count,
            files.Count,
            files.Sum(f => f.Length),
            thumbs.Count,
            tempBytes,
            dangling,
            orphans,
            missingThumbs,
            unreferencedThumbs);
    }

    public async Task<int> PurgeAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var set = ids.Distinct().ToList();
        if (set.Count == 0)
            return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Models.IgnoreQueryFilters()
            .Where(m => set.Contains(m.Id))
            .ToListAsync(ct);
        if (rows.Count == 0)
            return 0;

        var shas = rows.Select(r => r.FileSha256.Trim().ToLowerInvariant()).Distinct().ToList();
        db.Models.RemoveRange(rows);
        await db.SaveChangesAsync(ct);

        // Row first, file second: a crash leaves an orphan, never a dangling row.
        foreach (string sha in shas)
        {
            bool stillUsed = await db.Models.IgnoreQueryFilters()
                .AnyAsync(m => m.FileSha256 == sha, ct);
            if (!stillUsed)
                storage.DeleteLibraryFileIfUnreferenced(sha);
        }
        return rows.Count;
    }

    /// <summary>Delete library files and thumbs that no catalogue row references.</summary>
    public async Task<int> PurgeOrphansAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var used = (await db.Models.IgnoreQueryFilters().AsNoTracking()
                .Select(m => m.FileSha256)
                .ToListAsync(ct))
            .Select(s => s.Trim().ToLowerInvariant())
            .ToHashSet();

        int removed = 0;
        foreach (var file in storage.ListLibraryFiles())
        {
            string sha = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant();
            if (used.Contains(sha))
                continue;
            file.Delete();
            storage.DeleteThumb(sha);
            removed++;
        }

        foreach (var thumb in storage.ListThumbFiles())
        {
            string sha = Path.GetFileNameWithoutExtension(thumb.Name).ToLowerInvariant();
            if (!used.Contains(sha))
            {
                thumb.Delete();
                removed++;
            }
        }
        return removed;
    }

    public int CleanStaleTempFiles() => storage.CleanTempFiles(TimeSpan.FromHours(24));

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
