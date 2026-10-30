using BLL.Common;
using BLL.DTOs;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class RecipeCategoryService(VegetarianDbContext db)
{
    public static readonly string[] Types = ["MEAL_TYPE", "CUISINE", "DISH_TYPE", "LIFESTYLE", "DIETARY_TYPE"];
    public static readonly IReadOnlyDictionary<string, string> DietSlugs = new Dictionary<string, string>
    {
        ["vegan"] = "VEGAN", ["lacto-vegetarian"] = "LACTO_VEGETARIAN",
        ["ovo-vegetarian"] = "OVO_VEGETARIAN", ["lacto-ovo-vegetarian"] = "LACTO_OVO_VEGETARIAN"
    };
    public static RecipeCategoryResponse ToResponse(RecipeCategory c) => new(c.Id, c.Name, c.Slug, c.Type, c.Description, c.ImageUrl, c.IsActive, c.CreatedAt);
    public static string ValidateType(string type)
    {
        var value = type.Trim().ToUpperInvariant();
        if (!Types.Contains(value)) throw new ServiceException("Invalid recipe category type.");
        return value;
    }
    public async Task<IReadOnlyCollection<RecipeCategoryResponse>> ListAsync(string? type, bool includeInactive, CancellationToken ct)
    {
        var query = db.RecipeCategories.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(type)) { var value = ValidateType(type); query = query.Where(c => c.Type == value); }
        return (await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(ct)).Select(ToResponse).ToList();
    }
    public async Task<RecipeCategoryResponse> SaveAsync(int? id, RecipeCategoryRequest request, CancellationToken ct)
    {
        var type = ValidateType(request.Type);
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(-[a-z0-9]+)*$") || string.IsNullOrWhiteSpace(request.Name)) throw new ServiceException("Category name and valid slug are required.");
        if (type == "DIETARY_TYPE" && !DietSlugs.ContainsKey(slug)) throw new ServiceException("Unsupported vegetarian diet category.");
        var category = id.HasValue ? await db.RecipeCategories.FindAsync([id.Value], ct) ?? throw new ServiceException("Category not found.", 404) : new RecipeCategory { CreatedAt = VietnamTime.Now };
        if (id.HasValue && category.Type != type) throw new ServiceException("Category type cannot be changed.", 409);
        if (id.HasValue && type == "DIETARY_TYPE" && category.Slug != slug) throw new ServiceException("Dietary category identity cannot be changed.", 409);
        if (await db.RecipeCategories.AnyAsync(c => c.Id != (id ?? 0) && c.Type == type && c.Slug == slug, ct)) throw new ServiceException("Category already exists.", 409);
        var imageUrl = ImageUrlValidation.Normalize(request.ImageUrl);
        category.Name = request.Name.Trim(); category.Slug = slug; category.Type = type; category.Description = request.Description?.Trim(); category.ImageUrl = imageUrl; category.IsActive = request.IsActive;
        if (!id.HasValue) db.RecipeCategories.Add(category);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        {
            throw new ServiceException("Category already exists.", 409);
        }
        if (type == "DIETARY_TYPE") await BackfillDietaryAsync(ct);
        return ToResponse(category);
    }
    public async Task ValidateSelectionAsync(IEnumerable<int>? ids, IEnumerable<string> groups, CancellationToken ct)
    {
        groups = groups.Select(VegetarianDietRules.NormalizeIngredientGroup).ToArray();
        var selected = (ids ?? []).Distinct().ToArray();
        var categories = await db.RecipeCategories.Where(c => selected.Contains(c.Id) && c.IsActive).ToListAsync(ct);
        if (categories.Count != selected.Length) throw new ServiceException("One or more recipe categories are missing or inactive.");
        var compatible = VegetarianDietRules.GetCompatibleDietTypes(groups);
        if (categories.Any(c => c.Type == "DIETARY_TYPE" && (!DietSlugs.TryGetValue(c.Slug, out var diet) || !compatible.Contains(diet)))) throw new ServiceException("Selected dietary category conflicts with recipe ingredients.");
    }
    public async Task SetMappingsAsync(int recipeId, IEnumerable<int>? ids, IEnumerable<string> groups, CancellationToken ct)
    {
        var compatible = VegetarianDietRules.GetCompatibleDietTypes(groups);
        var dietary = await db.RecipeCategories.Where(c => c.Type == "DIETARY_TYPE").ToListAsync(ct);
        var selected = (ids ?? []).ToHashSet();
        selected.ExceptWith(dietary.Select(c => c.Id));
        selected.UnionWith(dietary.Where(c => c.IsActive && DietSlugs.TryGetValue(c.Slug, out var diet) && compatible.Contains(diet)).Select(c => c.Id));
        db.RecipeCategoryMappings.RemoveRange(await db.RecipeCategoryMappings.Where(m => m.RecipeId == recipeId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        foreach (var id in selected) db.RecipeCategoryMappings.Add(new RecipeCategoryMapping { RecipeId = recipeId, CategoryId = id });
    }

    public async Task UpdateRecipeMappingsAsync(int recipeId, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var recipe = await db.Recipes.Include(r => r.Post).Include(r => r.Ingredients).ThenInclude(i => i.Ingredient).ThenInclude(i => i.Category)
            .SingleOrDefaultAsync(r => r.PostId == recipeId && r.Post.Status != "REMOVED", ct)
            ?? throw new ServiceException("Recipe was not found.", 404);
        var groups = recipe.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup).ToArray();
        var selected = ids.Distinct().ToArray();
        await ValidateSelectionAsync(selected, groups, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await SetMappingsAsync(recipeId, selected, groups, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    public async Task SeedAsync(CancellationToken ct)
    {
        var catalog = new Dictionary<string, string[]>
        {
            ["MEAL_TYPE"] = ["Breakfast", "Lunch", "Dinner", "Snack", "Dessert"],
            ["CUISINE"] = ["Vietnamese", "Japanese", "Korean", "Thai", "Indian", "Chinese", "Italian", "Mediterranean", "Mexican", "Western"],
            ["DISH_TYPE"] = ["Salad", "Soup", "Main Course", "Side Dish", "Pasta & Noodles", "Rice & Grain Bowls", "Sandwich & Wrap", "Smoothies & Drinks", "Baked Goods"],
            ["LIFESTYLE"] = ["Quick & Easy", "High Protein", "Meal Prep", "Budget-Friendly", "Low Carb", "One-Pot Meals", "Weight Management"],
            ["DIETARY_TYPE"] = ["Vegan", "Lacto-Vegetarian", "Ovo-Vegetarian", "Lacto-Ovo Vegetarian"]
        };
        var existing = (await db.RecipeCategories.ToListAsync(ct)).Select(c => c.Type + ":" + c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, names) in catalog)
            foreach (var name in names)
            {
                var slug = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant().Replace("&", "and"), "[^a-z0-9]+", "-").Trim('-');
                if (existing.Add(type + ":" + slug)) db.RecipeCategories.Add(new RecipeCategory { Name = name, Slug = slug, Type = type, CreatedAt = VietnamTime.Now });
            }
        await db.SaveChangesAsync(ct);
        await BackfillDietaryAsync(ct);
    }
    public async Task BackfillDietaryAsync(CancellationToken ct)
    {
        var dietary = await db.RecipeCategories.Where(c => c.Type == "DIETARY_TYPE").ToListAsync(ct);
        var recipes = await db.Recipes.Include(r => r.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category).ToListAsync(ct);
        var existing = await db.RecipeCategoryMappings.Where(m => m.Category.Type == "DIETARY_TYPE").ToListAsync(ct);
        var expected = new HashSet<(int RecipeId, int CategoryId)>();
        foreach (var r in recipes.Where(r => VegetarianDietRules.IsVerifiedVegetarianRecipe(r.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup))))
        {
            var compatible = VegetarianDietRules.GetCompatibleDietTypes(r.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup));
            foreach (var c in dietary.Where(c => c.IsActive && DietSlugs.TryGetValue(c.Slug, out var diet) && compatible.Contains(diet))) expected.Add((r.PostId, c.Id));
        }
        var current = existing.Select(m => (m.RecipeId, m.CategoryId)).ToHashSet();
        db.RecipeCategoryMappings.RemoveRange(existing.Where(m => !expected.Contains((m.RecipeId, m.CategoryId))));
        foreach (var (recipeId, categoryId) in expected.Except(current)) db.RecipeCategoryMappings.Add(new RecipeCategoryMapping { RecipeId = recipeId, CategoryId = categoryId });
        await db.SaveChangesAsync(ct);
    }
}
