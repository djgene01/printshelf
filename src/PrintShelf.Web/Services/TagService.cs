using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Services;

public sealed record TagEditResult(bool Ok, string? Error, Tag? Tag);

/// <summary>Tag CRUD (P1). Slugs are normalized to lowercase so uniqueness is case-insensitive.</summary>
public sealed partial class TagService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<IReadOnlyList<TagUsage>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Tags.AsNoTracking()
            .OrderBy(t => t.Label)
            .Select(t => new TagUsage(t.Id, t.Slug, t.Label,
                t.ModelTags.Count(mt => mt.Model.DeletedAt == null)))
            .ToListAsync(ct);
    }

    public async Task<TagEditResult> CreateAsync(string? label, CancellationToken ct = default)
    {
        string clean = (label ?? string.Empty).Trim();
        if (clean.Length == 0)
            return new TagEditResult(false, "Tag name is required.", null);
        if (clean.Length > 64)
            clean = clean[..64];

        string slug = Slugify(clean);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Tags.AnyAsync(t => t.Slug == slug, ct))
            return new TagEditResult(false, $"Tag “{clean}” already exists.", null);

        var tag = new Tag { Slug = slug, Label = clean };
        db.Tags.Add(tag);
        await db.SaveChangesAsync(ct);
        return new TagEditResult(true, null, tag);
    }

    public async Task<TagEditResult> RenameAsync(Guid id, string? label, CancellationToken ct = default)
    {
        string clean = (label ?? string.Empty).Trim();
        if (clean.Length == 0)
            return new TagEditResult(false, "Tag name is required.", null);
        if (clean.Length > 64)
            clean = clean[..64];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
            return new TagEditResult(false, "Tag not found.", null);

        string slug = Slugify(clean);
        if (await db.Tags.AnyAsync(t => t.Slug == slug && t.Id != id, ct))
            return new TagEditResult(false, $"Tag “{clean}” already exists.", null);

        tag.Label = clean;
        tag.Slug = slug;
        await db.SaveChangesAsync(ct);
        return new TagEditResult(true, null, tag);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null)
            return false;
        db.Tags.Remove(tag); // model_tags rows cascade
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static string Slugify(string label)
    {
        string lowered = label.Trim().ToLowerInvariant();
        string dashed = WhitespaceAndPunctuation().Replace(lowered, "-").Trim('-');
        if (dashed.Length == 0)
            dashed = "tag";
        return dashed.Length > 64 ? dashed[..64].Trim('-') : dashed;
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceAndPunctuation();
}
