using BLL.Common;
using BLL.DTOs;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class IngredientCatalogService(VegetarianDbContext db, RecipeCategoryService recipeCategories, ICommunityEventPublisher communityEvents)
{
    public Task<List<IngredientCategoryResponse>> GetCategoriesAsync(CancellationToken ct) =>
        db.IngredientCategories.AsNoTracking().OrderBy(x => x.IngredientCategoryId)
            .Select(x => new IngredientCategoryResponse(x.IngredientCategoryId, x.Code, x.Name, x.DietaryGroup)).ToListAsync(ct);

    public Task<List<IngredientResponse>> GetIngredientsAsync(string? keyword, CancellationToken ct)
    {
        var query = db.Ingredients.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword)) query = query.Where(x => x.Name.Contains(keyword.Trim()));
        return query.OrderBy(x => x.Name)
            .Select(x => new IngredientResponse(x.IngredientId, x.Name, x.IngredientCategoryId, x.Category.DietaryGroup, x.AllergenId)).ToListAsync(ct);
    }

    public async Task<IngredientCategoryResponse> CreateCategoryAsync(IngredientCategoryRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var group = NormalizeCategoryGroup(request.DietaryGroup);
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_]*$"))
            throw new ServiceException("Category name or code is invalid.");
        if (await db.IngredientCategories.AnyAsync(x => x.Code == code, ct)) throw new ServiceException("Ingredient category code already exists.", 409);
        var category = new IngredientCategory { Code = code, Name = name, DietaryGroup = group };
        db.IngredientCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return new(category.IngredientCategoryId, category.Code, category.Name, category.DietaryGroup);
    }

    public async Task<IngredientCategoryResponse> UpdateCategoryAsync(int id, IngredientCategoryRequest request, CancellationToken ct)
    {
        var category = await db.IngredientCategories.FindAsync([id], ct) ?? throw new ServiceException("Ingredient category was not found.", 404);
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var group = NormalizeCategoryGroup(request.DietaryGroup);
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_]*$"))
            throw new ServiceException("Category name or code is invalid.");
        if (await db.IngredientCategories.AnyAsync(x => x.IngredientCategoryId != id && x.Code == code, ct)) throw new ServiceException("Ingredient category code already exists.", 409);
        if (IsBaseGroup(category.Code) && (code != category.Code || group != category.Code))
            throw new ServiceException("A built-in dietary category cannot change its code or dietary group.");
        var affectedRecipes = await db.RecipeIngredients.Where(x => x.Ingredient.IngredientCategoryId == id).Select(x => x.RecipeId).Distinct().ToListAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        category.Code = code; category.Name = name; category.DietaryGroup = group;
        await db.SaveChangesAsync(ct);
        await RefreshCompatibilityAsync(ct);
        await recipeCategories.BackfillDietaryAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var recipeId in affectedRecipes) await communityEvents.PublishAsync(new CommunityEvent("postUpdated", recipeId), ct);
        return new(category.IngredientCategoryId, category.Code, category.Name, category.DietaryGroup);
    }

    private static bool IsBaseGroup(string code) => code is "PLANT" or "DAIRY" or "EGG" or "HONEY" or "UNVERIFIED";
    private static string NormalizeCategoryGroup(string value)
    {
        var group = value.Trim().ToUpperInvariant();
        if (group != "UNVERIFIED") VegetarianDietRules.NormalizeIngredientGroup(group);
        return group;
    }

    public async Task<IngredientResponse> UpdateAsync(int id, UpdateIngredientRequest request, CancellationToken ct)
    {
        var ingredient = await db.Ingredients.SingleOrDefaultAsync(x => x.IngredientId == id, ct) ?? throw new ServiceException("Ingredient was not found.", 404);
        var category = await db.IngredientCategories.FindAsync([request.IngredientCategoryId], ct) ?? throw new ServiceException("Ingredient category was not found.", 404);
        if (request.AllergenId.HasValue && !await db.Allergens.AnyAsync(x => x.AllergenId == request.AllergenId.Value, ct)) throw new ServiceException("Allergen was not found.", 404);
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ServiceException("Ingredient name is required.");
        if (await db.Ingredients.AnyAsync(x => x.IngredientId != id && x.Name == name, ct)) throw new ServiceException("An ingredient with this name already exists.", 409);
        var affectedRecipes = await db.RecipeIngredients.Where(x => x.IngredientId == id).Select(x => x.RecipeId).Distinct().ToListAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ingredient.Name = name; ingredient.IngredientCategoryId = category.IngredientCategoryId; ingredient.Category = category; ingredient.AllergenId = request.AllergenId;
        await db.SaveChangesAsync(ct);
        await RefreshCompatibilityAsync(ct);
        await recipeCategories.BackfillDietaryAsync(ct);
        await transaction.CommitAsync(ct);
        foreach (var recipeId in affectedRecipes) await communityEvents.PublishAsync(new CommunityEvent("postUpdated", recipeId), ct);
        return new(ingredient.IngredientId, ingredient.Name, category.IngredientCategoryId, category.DietaryGroup, ingredient.AllergenId);
    }

    public async Task<IReadOnlyList<(Ingredient Ingredient, string? Amount)>> ResolveAsync(IEnumerable<IngredientRequest> requests, CancellationToken ct)
    {
        var items = requests.Select(x => (Request: x, Name: x.IngredientName.Trim(), Code: VegetarianDietRules.NormalizeIngredientGroup(x.DietaryGroup))).ToList();
        var names = items.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var categories = await db.IngredientCategories.ToDictionaryAsync(x => x.Code, ct);
        var existing = await db.Ingredients.Include(x => x.Category).Where(x => names.Contains(x.Name)).ToListAsync(ct);
        var byName = existing.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var requestedAllergens = items.Where(x => x.Request.AllergenId.HasValue).Select(x => x.Request.AllergenId!.Value).Distinct().ToArray();
        if (await db.Allergens.CountAsync(x => requestedAllergens.Contains(x.AllergenId), ct) != requestedAllergens.Length)
            throw new ServiceException("One or more allergens were not found.", 404);

        var result = new List<(Ingredient, string?)>();
        foreach (var item in items)
        {
            if (!categories.TryGetValue(item.Code, out var category)) throw new ServiceException("Ingredient categories have not been configured.", 500);
            if (!byName.TryGetValue(item.Name, out var ingredient))
            {
                ingredient = new Ingredient { Name = item.Name, Category = category, AllergenId = item.Request.AllergenId };
                db.Ingredients.Add(ingredient);
                byName.Add(item.Name, ingredient);
            }
            else if (ingredient.Category.DietaryGroup != item.Code || (item.Request.AllergenId.HasValue && ingredient.AllergenId != item.Request.AllergenId))
            {
                throw new ServiceException($"Ingredient '{ingredient.Name}' conflicts with its existing category or allergen. Use the classification in the ingredient catalog.", 409);
            }
            result.Add((ingredient, item.Request.Amount?.Trim()));
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task RefreshCompatibilityAsync(CancellationToken ct)
    {
        var recipes = await db.Recipes.Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category).ToListAsync(ct);
        var diets = await db.DietTypes.ToDictionaryAsync(x => x.Name, x => x.DietTypeId, ct);
        var expected = recipes.SelectMany(recipe => VegetarianDietRules.GetCompatibleDietTypes(recipe.Ingredients.Select(x => x.Ingredient.Category.DietaryGroup))
            .Where(diets.ContainsKey).Select(name => (RecipeId: recipe.PostId, DietTypeId: diets[name]))).ToHashSet();
        var existing = await db.RecipeDietCompatibilities.ToListAsync(ct);
        db.RecipeDietCompatibilities.RemoveRange(existing.Where(x => !expected.Contains((x.RecipeId, x.DietTypeId))));
        foreach (var item in existing.Where(x => expected.Contains((x.RecipeId, x.DietTypeId)))) item.IsCompatible = true;
        var current = existing.Select(x => (x.RecipeId, x.DietTypeId)).ToHashSet();
        foreach (var pair in expected.Except(current)) db.RecipeDietCompatibilities.Add(new RecipeDietCompatibility { RecipeId = pair.RecipeId, DietTypeId = pair.DietTypeId, IsCompatible = true });
        await db.SaveChangesAsync(ct);
    }
}
