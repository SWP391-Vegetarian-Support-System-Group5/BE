namespace DAL.Entities;

public class BMIType
{
    public int BMITypeId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public ICollection<RecipeBMIType> Recipes { get; set; } = new List<RecipeBMIType>();
}

public class RecipeBMIType
{
    public int RecipeId { get; set; }
    public int BMITypeId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public BMIType BMIType { get; set; } = null!;
}
