using BLL.Common;
using BLL.DTOs;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class RecipeCategoryService(VegetarianDbContext db)
{
    public static RecipeCategoryResponse ToResponse(RecipeCategory c) => new(c.Id, c.Name, c.IsActive, c.CreatedAt);

    public async Task<IReadOnlyCollection<RecipeCategoryResponse>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var query = db.RecipeCategories.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(c => c.IsActive);
        return (await query.OrderBy(c => c.Name).ToListAsync(ct)).Select(ToResponse).ToList();
    }

    public async Task<RecipeCategoryResponse> SaveAsync(int? id, RecipeCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ServiceException("Category name is required.");
        var category = id.HasValue ? await db.RecipeCategories.FindAsync([id.Value], ct) ?? throw new ServiceException("Category not found.", 404) : new RecipeCategory { CreatedAt = VietnamTime.Now };
        if (await db.RecipeCategories.AnyAsync(c => c.Id != (id ?? 0) && c.Name == name, ct)) throw new ServiceException("Category already exists.", 409);
        category.Name = name;
        category.IsActive = request.IsActive;
        if (!id.HasValue) db.RecipeCategories.Add(category);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        {
            throw new ServiceException("Category already exists.", 409);
        }
        return ToResponse(category);
    }
    public async Task ValidateSelectionAsync(IEnumerable<int>? ids, IEnumerable<string> groups, CancellationToken ct)
    {
        var selected = (ids ?? []).Distinct().ToArray();
        var categories = await db.RecipeCategories.Where(c => selected.Contains(c.Id) && c.IsActive).ToListAsync(ct);
        if (categories.Count != selected.Length) throw new ServiceException("One or more recipe categories are missing or inactive.");
    }
    public async Task SetMappingsAsync(int recipeId, IEnumerable<int>? ids, IEnumerable<string> groups, CancellationToken ct)
    {
        var selected = (ids ?? []).ToHashSet();
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
}
