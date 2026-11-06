using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Components;
using PrintShelf.Web.Data;
using PrintShelf.Web.Services;

// English-only UI with invariant number formatting (dots for decimals, commas
// for thousands) so 32.76 mm never renders as 32,76 (A-06).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- options
var storageOptions = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
var uploadOptions = builder.Configuration.GetSection("Upload").Get<UploadOptions>() ?? new UploadOptions();
var slicerOptions = builder.Configuration.GetSection("Slicers").Get<SlicerOptions>() ?? new SlicerOptions();

var connectionString = Environment.GetEnvironmentVariable("PRINTSHELF_CONN")
    ?? builder.Configuration.GetConnectionString("PrintShelf")
    ?? throw new InvalidOperationException(
        "Connection string 'PrintShelf' missing. Set it in appsettings.Development.json, user-secrets or PRINTSHELF_CONN.");

// ---------------------------------------------------------------- services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o =>
    {
        // Blazor Server uploads stream through the SignalR circuit (R-02).
        // Must match the upload cap, otherwise files >64 MB die before ingest.
        o.MaximumReceiveMessageSize = uploadOptions.MaxBytes;
        o.StreamBufferCapacity = 50;
    });

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton(uploadOptions);
builder.Services.AddSingleton(slicerOptions);
builder.Services.AddSingleton<SlicerLocator>();
builder.Services.AddSingleton<ISlicerProcessStarter, SlicerProcessStarter>();
builder.Services.AddSingleton(sp => new StorageService(
    sp.GetRequiredService<StorageOptions>(),
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<PendingUploadStore>();

builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<PrintRunService>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<SlicerLaunchService>();

// Upload size limits: Kestrel + form + circuit (docs/02 §5, P1 checklist).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = uploadOptions.MaxBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = uploadOptions.MaxBytes);

var app = builder.Build();

// ---------------------------------------------------------------- database
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    if (!await db.Database.CanConnectAsync())
        throw new InvalidOperationException($"Cannot reach PostgreSQL: {connectionString}");
    await db.Database.MigrateAsync();
}

// Create data/library, data/thumbs, data/tmp on startup (P0 acceptance).
app.Services.GetRequiredService<StorageService>().EnsureDirectories();

// ---------------------------------------------------------------- seed CLI
if (args.Length > 0 && string.Equals(args[0], "seed", StringComparison.OrdinalIgnoreCase))
{
    int count = args.Length > 1 && int.TryParse(args[1], out int parsed) ? Math.Clamp(parsed, 1, 100_000) : 1000;
    bool reset = args.Any(a => string.Equals(a, "--reset", StringComparison.OrdinalIgnoreCase));
    await SeedData.RunAsync(app.Services, count, reset);
    return;
}

// ---------------------------------------------------------------- pipeline
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// LAN-only MVP has no auth (D5): plain HTTP on localhost, no HSTS/redirect
// (docs/06-deployment.md §3). Revisit at P7.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.UseAntiforgery();
app.MapStaticAssets();

// ---------------------------------------------------------------- endpoints
app.MapGet("/health", async (IDbContextFactory<AppDbContext> factory) =>
{
    try
    {
        await using var db = await factory.CreateDbContextAsync();
        bool dbOk = await db.Database.CanConnectAsync();
        return dbOk
            ? Results.Ok(new { status = "ok", database = "ok", time = DateTimeOffset.UtcNow })
            : Results.Json(new { status = "degraded", database = "unreachable", time = DateTimeOffset.UtcNow },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "degraded", database = "unreachable", error = ex.GetType().Name },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Streams the original STL for the three.js viewer (P3).
app.MapGet("/files/{id:guid}", async (Guid id, CatalogService catalog, StorageService storage) =>
{
    var model = await catalog.GetModelAsync(id);
    if (model is null)
        return Results.NotFound();

    string path = storage.LibraryFileFor(model.FileSha256.Trim());
    if (!File.Exists(path))
        return Results.NotFound();

    return Results.File(path, "model/stl", enableRangeProcessing: true);
}).WithName("ModelFile");

// Thumbnail captured by the browser at first preview (U-02=yes).
app.MapGet("/thumbs/{id:guid}", async (Guid id, CatalogService catalog, StorageService storage) =>
{
    var model = await catalog.GetModelAsync(id);
    if (model is null)
        return Results.NotFound();

    string path = storage.ThumbFileFor(model.FileSha256.Trim());
    return File.Exists(path)
        ? Results.File(path, "image/png", enableRangeProcessing: true)
        : Results.NotFound();
}).WithName("ModelThumb");

app.MapPost("/api/models/{id:guid}/thumbnail", async (
    Guid id,
    HttpRequest request,
    CatalogService catalog,
    StorageService storage) =>
{
    const long maxThumbBytes = 4L * 1024 * 1024;
    if (request.ContentLength > maxThumbBytes)
        return Results.BadRequest(new { error = "Thumbnail is too large." });

    var model = await catalog.GetModelAsync(id);
    if (model is null)
        return Results.NotFound();

    using var buffer = new MemoryStream();
    byte[] chunk = new byte[64 * 1024];
    int read;
    while ((read = await request.Body.ReadAsync(chunk)) > 0)
    {
        if (buffer.Length + read > maxThumbBytes)
            return Results.BadRequest(new { error = "Thumbnail is too large." });
        buffer.Write(chunk, 0, read);
    }

    byte[] png = buffer.ToArray();
    byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    if (png.Length < 100 || !png.AsSpan(0, 8).SequenceEqual(signature))
        return Results.BadRequest(new { error = "Body must be a PNG image." });

    using var pngStream = new MemoryStream(png);
    await storage.SaveThumbnailAsync(model.FileSha256.Trim(), pngStream);
    return Results.NoContent();
}).DisableAntiforgery();

// CSV exports (P6, FR-10): UTF-8 BOM.
app.MapGet("/export/models.csv", async (CsvExportService csv, CancellationToken ct) =>
        Results.File(await csv.ModelsCsvAsync(ct), "text/csv; charset=utf-8", "printshelf-models.csv"))
    .WithName("ExportModels");

app.MapGet("/export/print-runs.csv", async (CsvExportService csv, CancellationToken ct) =>
        Results.File(await csv.PrintRunsCsvAsync(ct), "text/csv; charset=utf-8", "printshelf-print-runs.csv"))
    .WithName("ExportPrintRuns");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed for WebApplicationFactory-based tests.</summary>
public partial class Program;
