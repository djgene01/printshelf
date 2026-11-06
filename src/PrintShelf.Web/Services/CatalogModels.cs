namespace PrintShelf.Web.Services;

/// <summary>Lightweight catalogue row used by grid/list views (no EF tracking).</summary>
public sealed record ModelSummary(
    Guid Id,
    string Name,
    string? Category,
    string FileSha256,
    long FileBytes,
    int TriCount,
    decimal BboxX,
    decimal BboxY,
    decimal BboxZ,
    decimal? VolumeMm3,
    string PrintUnit,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Tags,
    int PrintCount,
    bool HasThumb)
{
    public string Dimensions => $"{BboxX:0.##} × {BboxY:0.##} × {BboxZ:0.##} {PrintUnit}";
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public sealed record TagUsage(Guid Id, string Slug, string Label, int ModelCount);

public sealed record CatalogFacets(IReadOnlyList<string> Categories, IReadOnlyList<TagUsage> Tags);

public sealed record CatalogQuery(
    string? Search = null,
    Guid? TagId = null,
    string? Category = null,
    string Sort = "newest",
    int Page = 1,
    int PageSize = 24);

/// <summary>UI filter state emitted by <c>CatalogToolbar</c>.</summary>
public sealed record CatalogFilters(string Search, string? Category, Guid? TagId, string Sort, string View)
{
    public bool IsActive => !string.IsNullOrWhiteSpace(Search) ||
                            !string.IsNullOrWhiteSpace(Category) || TagId is not null;
}

public sealed record ModelEditInput(
    string Name,
    string? Description,
    string? Category,
    string PrintUnit,
    IReadOnlyList<Guid> TagIds);

public sealed record DeletedModel(Guid Id, string Name, string FileSha256, long FileBytes, DateTimeOffset DeletedAt);

public sealed record OrphanFile(string FileName, long Bytes);

public sealed record IntegrityReport(
    int LiveModels,
    int DeletedModels,
    int LibraryFiles,
    long LibraryBytes,
    int ThumbFiles,
    long TempBytes,
    IReadOnlyList<string> DanglingRows,
    IReadOnlyList<OrphanFile> OrphanFiles,
    IReadOnlyList<string> MissingThumbnails,
    IReadOnlyList<string> UnreferencedThumbnails);

public sealed record ThumbCandidate(Guid Id, string Name, string Color);

public sealed record MostPrinted(Guid Id, string Name, string FileSha256, int Runs, int Successes)
{
    public double SuccessRate => Runs == 0 ? 0 : Successes * 100.0 / Runs;
}

public sealed record DashboardData(
    int Models,
    int Tags,
    long TotalBytes,
    int Prints,
    int Successes,
    IReadOnlyList<ModelSummary> Recent,
    IReadOnlyList<MostPrinted> MostPrinted);
