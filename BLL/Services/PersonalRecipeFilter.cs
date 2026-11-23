using System.Linq.Expressions;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

// Query results and profiles are not cached; selections are read from the current JWT account each request.
public sealed record PersonalRecipeFilter(int? DietTypeId, int[] AllergenIds)
{
    public string[] AllergenNames { get; init; } = [];
    public int[] KeywordIngredientIds { get; init; } = [];

    public static async Task<PersonalRecipeFilter> LoadAsync(VegetarianDbContext db, int? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return new(null, []);
        var diet = await db.UserProfiles.Where(x => x.UserId == userId).Select(x => x.DietTypeId).SingleOrDefaultAsync(ct);
        var selected = await db.UserAllergens.Where(x => x.UserId == userId)
            .Select(x => new { x.AllergenId, x.Allergen.Name }).ToListAsync(ct);
        if (selected.Count == 0) return new(diet, []);
        var names = selected.Select(x => x.Name).ToArray();
        // Normalize the ingredient catalog once, then apply IDs in SQL before ordering/limits.
        // Do not materialize the recipe/post result set to run string matching.
        var catalog = await db.Ingredients.AsNoTracking()
            .Select(x => new { x.IngredientId, x.Name, x.IsAllergenProfileVerified }).ToListAsync(ct);
        return new(diet, selected.Select(x => x.AllergenId).ToArray())
        {
            AllergenNames = names,
            KeywordIngredientIds = catalog.Where(x => AllergenKeywordRules.Matches(names, x.Name, x.IsAllergenProfileVerified))
                .Select(x => x.IngredientId).ToArray()
        };
    }

    // Exclude recorded allergen matches. Incomplete verification is metadata, not a blanket exclusion.
    public Expression<Func<Recipe, bool>> Predicate => recipe =>
        recipe.Post.Status == "PUBLISHED" && recipe.Ingredients.Any() &&
        !recipe.Ingredients.Any(i => i.Ingredient.Category.DietaryGroup != "PLANT" && i.Ingredient.Category.DietaryGroup != "DAIRY" && i.Ingredient.Category.DietaryGroup != "EGG" && i.Ingredient.Category.DietaryGroup != "HONEY") &&
        (!DietTypeId.HasValue || recipe.DietCompatibilities.Any(d => d.DietTypeId == DietTypeId.Value && d.IsCompatible)) &&
        (AllergenIds.Length == 0 || !recipe.Ingredients.Any(i =>
            KeywordIngredientIds.Contains(i.IngredientId) ||
            (i.Ingredient.AllergenId.HasValue && AllergenIds.Contains(i.Ingredient.AllergenId.Value)) ||
            i.Ingredient.AllergenLinks.Any(a => AllergenIds.Contains(a.AllergenId))));

    public IQueryable<Recipe> Apply(IQueryable<Recipe> query) => query.Where(Predicate);
    // Category, AllergenLinks, DietCompatibilities and Post must be loaded for in-memory plan validation.
    public bool Matches(Recipe recipe) => Predicate.Compile()(recipe) &&
        !recipe.Ingredients.Any(i => AllergenKeywordRules.Matches(AllergenNames, i.Ingredient.Name, i.Ingredient.IsAllergenProfileVerified));
}
