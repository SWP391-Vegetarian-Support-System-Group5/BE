using System.Linq.Expressions;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

// Query results and profiles are not cached; selections are read from the current JWT account each request.
public sealed record PersonalRecipeFilter(int? DietTypeId, int[] AllergenIds)
{
    public static async Task<PersonalRecipeFilter> LoadAsync(VegetarianDbContext db, int? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return new(null, []);
        return new(await db.UserProfiles.Where(x => x.UserId == userId).Select(x => x.DietTypeId).SingleOrDefaultAsync(ct),
            await db.UserAllergens.Where(x => x.UserId == userId).Select(x => x.AllergenId).ToArrayAsync(ct));
    }

    public Expression<Func<Recipe, bool>> Predicate => recipe =>
        recipe.Post.Status == "PUBLISHED" && recipe.Ingredients.Any() &&
        !recipe.Ingredients.Any(i => i.Ingredient.Category.DietaryGroup != "PLANT" && i.Ingredient.Category.DietaryGroup != "DAIRY" && i.Ingredient.Category.DietaryGroup != "EGG" && i.Ingredient.Category.DietaryGroup != "HONEY") &&
        (!DietTypeId.HasValue || recipe.DietCompatibilities.Any(d => d.DietTypeId == DietTypeId.Value && d.IsCompatible)) &&
        (AllergenIds.Length == 0 || !recipe.Ingredients.Any(i => !i.Ingredient.IsAllergenProfileVerified ||
            (i.Ingredient.AllergenId.HasValue && AllergenIds.Contains(i.Ingredient.AllergenId.Value)) ||
            i.Ingredient.AllergenLinks.Any(a => AllergenIds.Contains(a.AllergenId))));

    public IQueryable<Recipe> Apply(IQueryable<Recipe> query) => query.Where(Predicate);
    // Category, AllergenLinks, DietCompatibilities and Post must be loaded for in-memory plan validation.
    public bool Matches(Recipe recipe) => Predicate.Compile()(recipe);
}
