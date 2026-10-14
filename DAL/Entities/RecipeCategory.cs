namespace DAL.Entities;

public class RecipeCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public ICollection<RecipeCategoryMapping> Mappings { get; set; } = new List<RecipeCategoryMapping>();
}

public class RecipeCategoryMapping
{
    public int RecipeId { get; set; }
    public int CategoryId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public RecipeCategory Category { get; set; } = null!;
}
