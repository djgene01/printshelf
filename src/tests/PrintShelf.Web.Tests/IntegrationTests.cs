using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PrintShelf.Stl;
using PrintShelf.Web.Data;
using PrintShelf.Web.Data.Entities;
using PrintShelf.Web.Services;

namespace PrintShelf.Web.Tests;

/// <summary>
/// Service/integration tests against a real local PostgreSQL database
/// (docs/05-testing.md §2). The scratch database is recreated once per run.
/// Override with PRINTSHELF_TEST_CONN.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string DefaultConnection =
        "Host=127.0.0.1;Port=5434;Database=printshelf_test;Username=printshelf;Password=printshelf_dev_pw";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("PRINTSHELF_TEST_CONN") ?? DefaultConnection;

    public DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "printshelf-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var db = new AppDbContext(Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        try { if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true); }
        catch (IOException) { }
        return Task.CompletedTask;
    }
}

public sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}

[CollectionDefinition("postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

[Collection("postgres")]
public sealed class ServiceIntegrationTests(PostgresFixture postgres)
{
    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    private static string FixturePath(string name)
    {
        string fixture = Path.Combine(RepoRoot(), "src", "tests", "PrintShelf.Stl.Tests", "fixtures", name);
        return File.Exists(fixture) ? fixture : Path.Combine(RepoRoot(), "tools", "samples", name);
    }

    private sealed record Services(
        IngestService Ingest, CatalogService Catalog, TagService Tags,
        PrintRunService Runs, CsvExportService Csv, StorageService Storage,
        IDbContextFactory<AppDbContext> DbFactory);

    private Services CreateServices()
    {
        var dbFactory = new TestDbContextFactory(postgres.Options);
        var storage = new StorageService(new StorageOptions
        {
            LibraryPath = "library",
            ThumbPath = "thumbs",
            TempPath = "tmp"
        }, postgres.StorageRoot);
        storage.EnsureDirectories();

        var pending = new PendingUploadStore();
        var upload = new UploadOptions { MaxBytes = 200L * 1024 * 1024 };
        var ingest = new IngestService(dbFactory, storage, pending, upload, NullLogger<IngestService>.Instance);

        var categoryService = new CategoryService(dbFactory);

        return new Services(
            ingest,
            new CatalogService(dbFactory, storage, categoryService),
            new TagService(dbFactory),
            new PrintRunService(dbFactory),
            new CsvExportService(dbFactory),
            storage,
            dbFactory);
    }

    private static async Task<UploadResult> IngestAsync(Services s, string fixture, string? name = null)
    {
        string path = FixturePath(fixture);
        await using var stream = File.OpenRead(path);
        return await s.Ingest.IngestAsync(stream, (name ?? Path.GetFileNameWithoutExtension(fixture)) + ".stl",
            new FileInfo(path).Length);
    }

    [Fact]
    public async Task Ingest_stores_file_and_exact_stats()
    {
        var s = CreateServices();

        var result = await IngestAsync(s, "solid_binary_header.stl", "stats-cube");

        Assert.Equal(UploadStatus.Added, result.Status);
        var model = Assert.IsType<StlModel>(result.Model);
        Assert.Equal(12, model.TriCount);
        Assert.Equal(10m, model.BboxX);
        Assert.Equal(10m, model.BboxY);
        Assert.Equal(10m, model.BboxZ);
        Assert.Equal(1000m, model.VolumeMm3);
        Assert.Equal(684, model.FileBytes);
        Assert.True(File.Exists(s.Storage.LibraryFileFor(model.FileSha256.Trim())));

        await using var db = await s.DbFactory.CreateDbContextAsync();
        Assert.True(await db.Models.AnyAsync(m => m.Id == model.Id));
    }

    [Fact]
    public async Task Duplicate_upload_prompts_then_keep_both_shares_one_file()
    {
        var s = CreateServices();

        var first = await IngestAsync(s, "pyramid.stl", "dupe-pyramid");
        Assert.Equal(UploadStatus.Added, first.Status);

        var second = await IngestAsync(s, "pyramid.stl", "dupe-pyramid");
        Assert.Equal(UploadStatus.Duplicate, second.Status);
        Assert.NotNull(second.DuplicateToken);
        Assert.Single(second.Duplicates);

        var kept = await s.Ingest.ResolveDuplicateAsync(second.DuplicateToken, keepBoth: true);
        Assert.Equal(UploadStatus.Added, kept.Status);
        Assert.NotNull(kept.Model);
        Assert.NotEqual(first.Model!.Id, kept.Model!.Id);
        Assert.Equal(first.Model.FileSha256, kept.Model.FileSha256);
        Assert.Equal(1, s.Storage.ListLibraryFiles().Count(f => f.Name.StartsWith(first.Model.FileSha256.Trim())));
    }

    [Fact]
    public async Task Truncated_and_oversize_uploads_are_rejected_without_rows()
    {
        var s = CreateServices();
        int before = await CountModels(s);

        var truncated = await IngestAsync(s, "truncated_cube.stl", "broken-cube");
        Assert.Equal(UploadStatus.Rejected, truncated.Status);
        Assert.Contains("truncated", truncated.Message, StringComparison.OrdinalIgnoreCase);

        await using (var stream = new MemoryStream(new byte[100]))
        {
            var oversize = await s.Ingest.IngestAsync(stream, "huge.stl", s.Ingest.MaxBytes + 1);
            Assert.Equal(UploadStatus.Rejected, oversize.Status);
            Assert.Contains("larger than", oversize.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(before, await CountModels(s));
    }

    [Fact]
    public async Task Search_filter_and_sort_round_trip()
    {
        var s = CreateServices();
        var added = await IngestAsync(s, "torus.stl", "search-torus");
        var model = added.Model!;

        var byName = await s.Catalog.SearchAsync(new CatalogQuery(Search: "search-torus"));
        Assert.Contains(byName.Items, m => m.Id == model.Id);

        var miss = await s.Catalog.SearchAsync(new CatalogQuery(Search: "zzzz-no-such-model"));
        Assert.DoesNotContain(miss.Items, m => m.Id == model.Id);

        var tag = await s.Tags.CreateAsync("integration-tag");
        Assert.True(tag.Ok);
        bool updated = await s.Catalog.UpdateMetadataAsync(model.Id,
            new ModelEditInput(model.Name, "updated", "Testing", "mm", [tag.Tag!.Id]));
        Assert.True(updated);

        var byTag = await s.Catalog.SearchAsync(new CatalogQuery(TagId: tag.Tag.Id));
        Assert.Single(byTag.Items);
        Assert.Equal("Testing", byTag.Items[0].Category);

        var byCategory = await s.Catalog.SearchAsync(new CatalogQuery(Category: "Testing"));
        Assert.Single(byCategory.Items);

        var sorted = await s.Catalog.SearchAsync(new CatalogQuery(Sort: "name", PageSize: 200));
        var names = sorted.Items.Select(i => i.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);

        var facets = await s.Catalog.GetFacetsAsync();
        Assert.Contains("Testing", facets.Categories);
    }

    [Fact]
    public async Task Soft_delete_hides_restore_shows_and_purge_removes_file()
    {
        var s = CreateServices();
        var added = await IngestAsync(s, "cube_ascii.stl", "soft-target");
        var model = added.Model!;
        string path = s.Storage.LibraryFileFor(model.FileSha256.Trim());
        Assert.True(File.Exists(path));

        Assert.True(await s.Catalog.SoftDeleteAsync(model.Id));
        Assert.Null(await s.Catalog.GetModelAsync(model.Id));
        Assert.Contains(await s.Catalog.GetDeletedAsync(), m => m.Id == model.Id);

        Assert.True(await s.Catalog.RestoreAsync(model.Id));
        Assert.NotNull(await s.Catalog.GetModelAsync(model.Id));

        Assert.True(await s.Catalog.SoftDeleteAsync(model.Id));
        Assert.Equal(1, await s.Catalog.PurgeAsync([model.Id]));
        Assert.Null(await s.Catalog.GetModelAsync(model.Id, includeDeleted: true));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Print_runs_crud_and_aggregate_are_correct()
    {
        var s = CreateServices();
        var added = await IngestAsync(s, "zero_triangles.stl", "run-target");
        var model = added.Model!;
        var today = DateTimeOffset.Now; // non-UTC offset: must be normalized before Npgsql sees it

        var first = await s.Runs.AddAsync(model.Id, new PrintRunInput(
            "Prusa MK4", "PLA", "Black", 0.4m, 0.2m, 60, PrintResults.Success, 2.50m, null, today));
        Assert.True(first.Ok);
        Assert.Equal(TimeSpan.Zero, first.Run!.PrintedAt.Offset);

        var second = await s.Runs.AddAsync(model.Id, new PrintRunInput(
            "Prusa MK4", "PETG", "White", 0.4m, 0.2m, 30, PrintResults.Failed, 1.00m, "clog", today));
        Assert.True(second.Ok);

        var aggregate = await s.Runs.GetAggregateAsync(model.Id);
        Assert.Equal(2, aggregate.Runs);
        Assert.Equal(1, aggregate.Successes);
        Assert.Equal(1, aggregate.Failures);
        Assert.Equal(90, aggregate.TotalMinutes);
        Assert.Equal(3.50m, aggregate.TotalCost);

        var edited = await s.Runs.UpdateAsync(second.Run!.Id, new PrintRunInput(
            "Bambu X1", "PETG", "White", 0.6m, 0.28m, 45, PrintResults.Success, 1.50m, null, today));
        Assert.True(edited.Ok);
        aggregate = await s.Runs.GetAggregateAsync(model.Id);
        Assert.Equal(2, aggregate.Successes);
        Assert.Equal(105, aggregate.TotalMinutes);
        Assert.Contains("Bambu X1", await s.Runs.SuggestPrintersAsync());

        Assert.True(await s.Runs.DeleteAsync(first.Run!.Id));
        Assert.Equal(1, (await s.Runs.GetAggregateAsync(model.Id)).Runs);
    }

    [Fact]
    public async Task Integrity_scan_finds_orphans_and_cleanup_removes_them()
    {
        var s = CreateServices();
        string sha = new string('b', 64);
        string orphan = s.Storage.LibraryFileFor(sha);
        await File.WriteAllBytesAsync(orphan, new byte[128]);

        var report = await s.Catalog.ScanAsync();
        Assert.Contains(report.OrphanFiles, f => f.FileName == Path.GetFileName(orphan));

        int removed = await s.Catalog.PurgeOrphansAsync();
        Assert.True(removed >= 1);
        Assert.False(File.Exists(orphan));

        var after = await s.Catalog.ScanAsync();
        Assert.DoesNotContain(after.OrphanFiles, f => f.FileName == Path.GetFileName(orphan));
    }

    [Fact]
    public async Task Csv_exports_are_utf8_bom_with_one_row_per_model()
    {
        var s = CreateServices();
        await IngestAsync(s, "nan_vertex.stl", "should-not-exist"); // rejected, no row
        var added = await IngestAsync(s, "cube_binary.stl", "csv-cube");
        Assert.Equal(UploadStatus.Added, added.Status);
        await s.Runs.AddAsync(added.Model!.Id, new PrintRunInput(
            "Ender 3", "PLA", null, 0.4m, 0.2m, 20, PrintResults.Success, null, null, DateTimeOffset.UtcNow));

        int modelCount = await CountModels(s);
        byte[] csv = await s.Csv.ModelsCsvAsync();

        Assert.Equal(0xEF, csv[0]);
        Assert.Equal(0xBB, csv[1]);
        Assert.Equal(0xBF, csv[2]);
        string text = Encoding.UTF8.GetString(csv, 3, csv.Length - 3);
        string[] lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Name,Category,Tags", lines[0]);
        Assert.Equal(modelCount + 1, lines.Length);
        Assert.Contains(lines, l => l.Contains("csv-cube"));

        byte[] runs = await s.Csv.PrintRunsCsvAsync();
        string runsText = Encoding.UTF8.GetString(runs, 3, runs.Length - 3);
        Assert.StartsWith("Model,Printer", runsText);
        Assert.Contains("csv-cube", runsText);
    }

    [Fact]
    public async Task Catalogue_query_stays_under_300ms_on_1000_rows()
    {
        var s = CreateServices();
        string prefix = "perf-" + Guid.NewGuid().ToString("N")[..6] + "-";
        var rows = Enumerable.Range(0, 1000).Select(i => new StlModel
        {
            Name = $"{prefix}{i:0000}",
            Category = "Perf",
            FileSha256 = i.ToString("x").PadLeft(64, '0'),
            FileBytes = 684,
            TriCount = 12,
            BboxX = 10m,
            BboxY = 10m,
            BboxZ = 10m,
            VolumeMm3 = 1000m,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
        }).ToList();

        await using var db = await s.DbFactory.CreateDbContextAsync();
        db.Models.AddRange(rows);
        await db.SaveChangesAsync();

        try
        {
            var query = new CatalogQuery(Search: prefix + "05", Category: "Perf", Sort: "name", PageSize: 48);
            var warm = await s.Catalog.SearchAsync(query);
            Assert.NotEmpty(warm.Items);

            var samples = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var page = await s.Catalog.SearchAsync(query);
                sw.Stop();
                Assert.NotEmpty(page.Items);
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            double median = samples.OrderBy(x => x).ElementAt(2);
            Assert.True(median < 300, $"median catalogue query was {median:0.0} ms (limit 300 ms): {string.Join(", ", samples.Select(x => x.ToString("0.0")))}");
        }
        finally
        {
            db.Models.RemoveRange(rows);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Integrity_scan_flags_a_row_whose_file_is_missing()
    {
        var s = CreateServices();
        string prefix = "dangling-" + Guid.NewGuid().ToString("N")[..6];
        var added = await IngestAsync(s, "cube_inverted.stl", prefix);
        Assert.Equal(UploadStatus.Added, added.Status);
        var model = added.Model!;
        string path = s.Storage.LibraryFileFor(model.FileSha256.Trim());

        File.Delete(path);

        var report = await s.Catalog.ScanAsync();
        Assert.Contains(report.DanglingRows, row => row.Contains(model.Name, StringComparison.OrdinalIgnoreCase));

        await s.Catalog.PurgeAsync([model.Id]);
    }

    [Fact]
    public async Task Category_crud_renames_and_uncategorises_models()
    {
        var categories = new CategoryService(new TestDbContextFactory(postgres.Options));
        string name = "Category " + Guid.NewGuid().ToString("N")[..8];
        string renamed = name + " renamed";

        var created = await categories.CreateAsync(name, "#FF0000");
        Assert.Equal(CategorySaveStatus.Saved, created.Status);
        Assert.Equal("#ff0000", created.Category!.Color);
        Assert.Equal(CategorySaveStatus.DuplicateName, (await categories.CreateAsync(name.ToUpperInvariant(), "#00ff00")).Status);
        Assert.Equal(CategorySaveStatus.InvalidColor, (await categories.CreateAsync(name + " x", "red")).Status);

        Guid modelId = Guid.NewGuid();
        await using (var db = new TestDbContextFactory(postgres.Options).CreateDbContext())
        {
            db.Models.Add(new StlModel
            {
                Id = modelId,
                Name = "category-model-" + modelId.ToString("N")[..6],
                Category = name,
                FileSha256 = new string('c', 64)
            });
            await db.SaveChangesAsync();
        }

        var updated = await categories.UpdateAsync(created.Category.Id, renamed, "#00AAFF");
        Assert.Equal(CategorySaveStatus.Saved, updated.Status);
        Assert.Equal(1, updated.Category!.ModelCount);
        Assert.Equal("#00aaff", await categories.ColorForAsync(renamed));

        await using (var db = new TestDbContextFactory(postgres.Options).CreateDbContext())
        {
            Assert.Equal(renamed, (await db.Models.FindAsync(modelId))!.Category);
        }

        Assert.True(await categories.DeleteAsync(created.Category.Id));
        await using (var db = new TestDbContextFactory(postgres.Options).CreateDbContext())
        {
            Assert.Null((await db.Models.FindAsync(modelId))!.Category);
            db.Models.Remove((await db.Models.FindAsync(modelId))!);
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain(await categories.ListAsync(), c => c.Id == created.Category.Id);
    }

    [Fact]
    public async Task Bulk_category_assignment_updates_only_selected_models()
    {
        var s = CreateServices();
        var categories = new CategoryService(new TestDbContextFactory(postgres.Options));
        string category = "Bulk " + Guid.NewGuid().ToString("N")[..8];
        var created = await categories.CreateAsync(category, "#22c55e");
        Assert.Equal(CategorySaveStatus.Saved, created.Status);

        var first = await IngestAsync(s, "cube.stl", "bulk-a-" + Guid.NewGuid().ToString("N")[..6]);
        var second = await IngestAsync(s, "cube_inverted.stl", "bulk-b-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(UploadStatus.Added, first.Status);
        Assert.Equal(UploadStatus.Added, second.Status);

        int changed = await s.Catalog.AssignCategoryAsync([first.Model!.Id], category);
        Assert.Equal(1, changed);
        await using (var db = new TestDbContextFactory(postgres.Options).CreateDbContext())
        {
            Assert.Equal(category, (await db.Models.FindAsync(first.Model.Id))!.Category);
            Assert.Null((await db.Models.FindAsync(second.Model!.Id))!.Category);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => s.Catalog.AssignCategoryAsync([first.Model.Id], "missing-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(2, await s.Catalog.AssignCategoryAsync([first.Model.Id, second.Model!.Id], null));
        Assert.True(await categories.DeleteAsync(created.Category!.Id));
        await s.Catalog.PurgeAsync([first.Model.Id, second.Model.Id]);
    }

    [Fact]
    public async Task Slicer_launch_passes_stored_stl_to_selected_app()
    {
        var s = CreateServices();
        var added = await IngestAsync(s, "cube.stl", "slicer-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.Equal(UploadStatus.Added, added.Status);

        string directory = Path.Combine(Path.GetTempPath(), "printshelf-slicer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string fakeExecutable = Path.Combine(directory, "orca-slicer.exe");
        File.WriteAllText(fakeExecutable, "");

        try
        {
            var locator = new SlicerLocator(new SlicerOptions { OrcaPath = fakeExecutable });
            var starter = new RecordingSlicerStarter();
            var slicer = new SlicerLaunchService(locator, s.DbFactory, s.Storage, starter);

            var result = await slicer.LaunchAsync(added.Model!.Id, "orca");

            Assert.True(result.Success, result.Message);
            Assert.Equal(Path.GetFullPath(fakeExecutable), starter.ExecutablePath);
            Assert.Equal(s.Storage.LibraryFileFor(added.Model.FileSha256.Trim()), starter.FilePath);
        }
        finally
        {
            await s.Catalog.PurgeAsync([added.Model!.Id]);
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingSlicerStarter : ISlicerProcessStarter
    {
        public string? ExecutablePath { get; private set; }
        public string? FilePath { get; private set; }

        public void Start(string executablePath, string filePath)
        {
            ExecutablePath = executablePath;
            FilePath = filePath;
        }
    }

    private static async Task<int> CountModels(Services s)
    {
        await using var db = await s.DbFactory.CreateDbContextAsync();
        return await db.Models.CountAsync();
    }
}
