namespace DAL.Entities;

public class IngredientCategory
{
    public int IngredientCategoryId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DietaryGroup { get; set; } = "UNVERIFIED";
    public ICollection<Ingredient> Ingredients { get; set; } = new List<Ingredient>();
}

public class Ingredient
{
    public int IngredientId { get; set; }
    public string Name { get; set; } = null!;
    public int IngredientCategoryId { get; set; }
    public int? AllergenId { get; set; }
    public IngredientCategory Category { get; set; } = null!;
    public Allergen? Allergen { get; set; }
    public bool IsAllergenProfileVerified { get; set; }
    public string? AllergenEvidence { get; set; }
    public ICollection<IngredientAllergen> AllergenLinks { get; set; } = new List<IngredientAllergen>();
    public ICollection<RecipeIngredient> Recipes { get; set; } = new List<RecipeIngredient>();
}
