using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Services;

public sealed record CategoryInfo(Guid Id, string Name, string Color, int ModelCount);

public enum CategorySaveStatus { Saved, InvalidName, InvalidColor, DuplicateName, NotFound }

public sealed record CategorySaveResult(CategorySaveStatus Status, CategoryInfo? Category = null);

/// <summary>
/// Catalogue categories are a first-class, user-managed list. Models keep the
/// category name in <see cref="StlModel.Category"/> (loose reference) so an
/// existing catalogue migrates without rewriting rows; renaming a category
/// updates every model that used the old name.
/// </summary>
public sealed partial class CategoryService(IDbContextFactory<AppDbContext> dbFactory)
{
    public const string DefaultColor = "#c7d2de";

    public async Task<IReadOnlyList<CategoryInfo>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var counts = await db.Models.AsNoTracking()
            .Where(m => m.Category != null && m.Category != "")
            .GroupBy(m => m.Category!)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countMap = counts
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count), StringComparer.OrdinalIgnoreCase);

        return categories
            .Select(c => new CategoryInfo(c.Id, c.Name, c.Color, countMap.GetValueOrDefault(c.Name)))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, string>> ColorMapAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Categories.AsNoTracking()
            .Select(c => new { c.Name, c.Color })
            .ToListAsync(ct);
        return rows
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Color, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> ColorForAsync(string? category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            return DefaultColor;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        string? color = await db.Categories.AsNoTracking()
            .Where(c => c.Name == category)
            .Select(c => c.Color)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(color) ? DefaultColor : color;
    }

    public async Task<CategorySaveResult> CreateAsync(string name, string color, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 80)
            return new(CategorySaveStatus.InvalidName);
        if (!TryNormalizeColor(color, out string normalized))
            return new(CategorySaveStatus.InvalidColor);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        string lower = name.ToLowerInvariant();
        if (await db.Categories.AnyAsync(c => c.Name.ToLower() == lower, ct))
            return new(CategorySaveStatus.DuplicateName);

        var entity = new Category { Name = name, Color = normalized };
        db.Categories.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return new(CategorySaveStatus.DuplicateName);
        }

        return new(CategorySaveStatus.Saved,
            new CategoryInfo(entity.Id, entity.Name, entity.Color, 0));
    }

    public async Task<CategorySaveResult> UpdateAsync(Guid id, string name, string color, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 80)
            return new(CategorySaveStatus.InvalidName);
        if (!TryNormalizeColor(color, out string normalized))
            return new(CategorySaveStatus.InvalidColor);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
            return new(CategorySaveStatus.NotFound);

        string lower = name.ToLowerInvariant();
        if (await db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == lower, ct))
            return new(CategorySaveStatus.DuplicateName);

        string oldName = entity.Name;
        entity.Name = name;
        entity.Color = normalized;

        if (!string.Equals(oldName, name, StringComparison.Ordinal))
        {
            // Rename soft-deleted rows too; restoring a model must not resurrect
            // a category name that no longer exists.
            await db.Models.IgnoreQueryFilters()
                .Where(m => m.Category == oldName)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Category, name), ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return new(CategorySaveStatus.DuplicateName);
        }

        int count = await db.Models.AsNoTracking().CountAsync(m => m.Category == name, ct);
        return new(CategorySaveStatus.Saved, new CategoryInfo(entity.Id, entity.Name, entity.Color, count));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
            return false;

        await db.Models.IgnoreQueryFilters()
            .Where(m => m.Category == entity.Name)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Category, (string?)null), ct);
        db.Categories.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool TryNormalizeColor(string? value, out string color)
    {
        color = DefaultColor;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string candidate = value.Trim();
        if (!HexColor().IsMatch(candidate))
            return false;
        color = candidate.ToLowerInvariant();
        return true;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
