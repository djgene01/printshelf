namespace PrintShelf.Web.Data.Entities;

public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>URL-safe unique key, compared case-insensitively via a lower() index.</summary>
    public required string Slug { get; set; }
    public required string Label { get; set; }

    public ICollection<ModelTag> ModelTags { get; set; } = new List<ModelTag>();
}

public class ModelTag
{
    public Guid ModelId { get; set; }
    public StlModel Model { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
