using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;

namespace PrintShelf.Web.Services;

/// <summary>
/// Development seeder: `dotnet run --project src/PrintShelf.Web -- seed 1000 [--reset]`.
/// Creates real (tiny, unique) STL files so the integrity scan stays clean, plus
/// tags and print runs for the dashboard/aggregates (docs/05-testing.md §6).
/// </summary>
public static class SeedData
{
    private static readonly (string Name, string Color)[] Categories =
    [
        ("Functional", "#67e8f9"),
        ("Miniature", "#86efac"),
        ("Tools", "#fcd34d"),
        ("Decor", "#d8b4fe"),
        ("Spare parts", "#fca5a5"),
        ("Prototype", "#93c5fd")
    ];

    private static readonly string[] TagLabels =
        ["functional", "decor", "miniature", "tool", "gift", "prototype", "spare-part", "vase"];

    private static readonly string[] Printers =
        ["Prusa MK4", "Bambu X1 Carbon", "Ender 3 V2", "Voron 2.4"];

    private static readonly string[] Materials = ["PLA", "PETG", "ABS", "ASA", "TPU"];

    private static readonly string[] Adjectives =
        ["Modular", "Twisted", "Compact", "Heavy-duty", "Snap-fit", "Hollow", "Reinforced", "Low-poly"];

    private static readonly string[] Nouns =
        ["bracket", "planter", "gear", "clip", "hook", "vase", "box", "stand", "mount", "spacer", "cap", "tray"];

    public static async Task RunAsync(
        IServiceProvider services,
        int count,
        bool reset,
        CancellationToken ct = default)
    {
        var dbFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var storage = services.GetRequiredService<StorageService>();
        storage.EnsureDirectories();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (reset)
        {
            Console.WriteLine("Resetting catalogue (rows + library + thumbs)…");
            await db.PrintRuns.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.ModelTags.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.Models.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
            await db.Tags.ExecuteDeleteAsync(ct);
            foreach (var f in storage.ListLibraryFiles()) f.Delete();
            foreach (var t in storage.ListThumbFiles()) t.Delete();
        }
        else if (await db.Models.CountAsync(ct) > 0)
        {
            Console.WriteLine($"Catalogue already has {await db.Models.CountAsync(ct)} models; use --reset to reseed.");
            return;
        }

        var tags = TagLabels.Select(label => new Tag
        {
            Slug = TagService.Slugify(label),
            Label = char.ToUpperInvariant(label[0]) + label[1..]
        }).ToList();
        db.Tags.AddRange(tags);

        var existingCategories = await db.Categories.Select(c => c.Name).ToListAsync(ct);
        foreach (var (name, color) in Categories)
        {
            if (!existingCategories.Contains(name, StringComparer.OrdinalIgnoreCase))
                db.Categories.Add(new Category { Name = name, Color = color });
        }

        await db.SaveChangesAsync(ct);

        var rng = new Random(20261105);
        var models = new List<StlModel>(count);
        var links = new List<ModelTag>(count * 2);

        for (int i = 0; i < count; i++)
        {
            float scale = 0.4f + (float)(rng.NextDouble() * 4.6); // 4–50 mm across
            byte[] stl = BuildCube(scale);
            string sha = Convert.ToHexString(SHA256.HashData(stl)).ToLowerInvariant();

            string path = storage.LibraryFileFor(sha);
            if (!File.Exists(path))
                await File.WriteAllBytesAsync(path, stl, ct);

            var model = new StlModel
            {
                Name = $"{Adjectives[i % Adjectives.Length]} {Nouns[(i / Adjectives.Length) % Nouns.Length]} {i + 1:0000}",
                Description = i % 7 == 0 ? "Seeded demo model for browse/search checks." : null,
                Category = Categories[i % Categories.Length].Name,
                FileSha256 = sha,
                FileBytes = stl.Length,
                TriCount = 12,
                BboxX = Math.Round((decimal)(10 * scale), 3),
                BboxY = Math.Round((decimal)(10 * scale), 3),
                BboxZ = Math.Round((decimal)(10 * scale), 3),
                VolumeMm3 = Math.Round((decimal)(1000.0 * scale * scale * scale), 3),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-rng.Next(0, 400)).AddMinutes(-i),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            models.Add(model);

            int tagCount = 1 + rng.Next(3);
            foreach (var tag in tags.OrderBy(_ => rng.Next()).Take(tagCount))
                links.Add(new ModelTag { Model = model, Tag = tag });
        }

        db.Models.AddRange(models);
        db.ModelTags.AddRange(links);
        await db.SaveChangesAsync(ct);

        var runs = new List<PrintRun>();
        for (int i = 0; i < models.Count; i += 4)
        {
            var model = models[i];
            int runCount = 1 + rng.Next(4);
            for (int r = 0; r < runCount; r++)
            {
                int roll = rng.Next(100);
                runs.Add(new PrintRun
                {
                    ModelId = model.Id,
                    Printer = Printers[rng.Next(Printers.Length)],
                    Material = Materials[rng.Next(Materials.Length)],
                    Color = rng.Next(4) switch { 0 => "Black", 1 => "White", 2 => "Orange", _ => "Grey" },
                    NozzleMm = rng.Next(2) == 0 ? 0.4m : 0.6m,
                    LayerMm = rng.Next(3) switch { 0 => 0.12m, 1 => 0.2m, _ => 0.28m },
                    DurationMin = 20 + rng.Next(400),
                    Result = roll < 78 ? PrintResults.Success : roll < 90 ? PrintResults.Failed : PrintResults.Partial,
                    Cost = Math.Round((decimal)(0.5 + rng.NextDouble() * 6), 2),
                    PrintedAt = DateTimeOffset.UtcNow.AddDays(-rng.Next(1, 180))
                });
            }
        }
        db.PrintRuns.AddRange(runs);
        await db.SaveChangesAsync(ct);

        Console.WriteLine($"Seeded {models.Count} models, {links.Count} tag links, {runs.Count} print runs, {tags.Count} tags.");
        Console.WriteLine($"Library: {storage.LibraryDir}");
    }

    /// <summary>The 10 mm unit cube from the fixtures, uniformly scaled.</summary>
    private static byte[] BuildCube(float scale)
    {
        (float X, float Y, float Z)[][] triangles =
        [
            [(0,0,0),(0,10,0),(10,10,0)],
            [(0,0,0),(10,10,0),(10,0,0)],
            [(0,0,10),(10,0,10),(10,10,10)],
            [(0,0,10),(10,10,10),(0,10,10)],
            [(0,0,0),(10,0,0),(10,0,10)],
            [(0,0,0),(10,0,10),(0,0,10)],
            [(0,10,0),(0,10,10),(10,10,10)],
            [(0,10,0),(10,10,10),(10,10,0)],
            [(0,0,0),(0,0,10),(0,10,10)],
            [(0,0,0),(0,10,10),(0,10,0)],
            [(10,0,0),(10,10,0),(10,10,10)],
            [(10,0,0),(10,10,10),(10,0,10)]
        ];

        using var ms = new MemoryStream(84 + 50 * 12);
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        w.Write(Encoding.ASCII.GetBytes("PrintShelf seed cube".PadRight(80, '\0')));
        w.Write((uint)triangles.Length);
        foreach (var tri in triangles)
        {
            w.Write(0f); w.Write(0f); w.Write(1f);
            foreach (var v in tri)
            {
                w.Write(v.X * scale);
                w.Write(v.Y * scale);
                w.Write(v.Z * scale);
            }
            w.Write((ushort)0);
        }
        w.Flush();
        return ms.ToArray();
    }
}
