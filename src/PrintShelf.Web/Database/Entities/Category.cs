namespace PrintShelf.Web.Data.Entities;

/// <summary>
/// User-managed catalogue category. Models reference it by name (Category
/// string) so existing catalogues migrate without rewriting every row.
/// Color is the material color used by the 3D preview.
/// </summary>
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string Color { get; set; } = "#c7d2de";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
