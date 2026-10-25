namespace Remindr.Models;

public sealed class Category
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3B82F6";
    public int SortOrder { get; set; }
}
