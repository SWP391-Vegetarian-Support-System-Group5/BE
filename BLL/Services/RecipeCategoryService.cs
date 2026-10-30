using BLL.Common;
using BLL.DTOs;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class RecipeCategoryService(VegetarianDbContext db)
{
    public static readonly string[] Types = ["MEAL_TYPE", "CUISINE", "DISH_TYPE", "LIFESTYLE"];
    public static readonly IReadOnlyList<(string Type, string Name, string Slug)> DefaultCategories =
    [
        ("DISH_TYPE", "Món từ ngũ cốc", "mon-tu-ngu-coc"),
        ("DISH_TYPE", "Bánh", "banh"),
        ("DISH_TYPE", "Món ăn sáng", "mon-an-sang"),
        ("DISH_TYPE", "Bánh mì & bánh kẹp", "banh-mi-banh-kep"),
        ("DISH_TYPE", "Tráng miệng", "trang-mieng"),
        ("DISH_TYPE", "Mì", "mi"),
        ("DISH_TYPE", "Salad & món ăn kèm", "salad-mon-an-kem"),
        ("DISH_TYPE", "Nước sốt", "nuoc-sot"),
        ("DISH_TYPE", "Ăn nhẹ", "an-nhe"),
        ("DISH_TYPE", "Súp & món hầm", "sup-mon-ham")
    ];
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
        var category = id.HasValue ? await db.RecipeCategories.FindAsync([id.Value], ct) ?? throw new ServiceException("Category not found.", 404) : new RecipeCategory { CreatedAt = VietnamTime.Now };
        if (id.HasValue && category.Type != type) throw new ServiceException("Category type cannot be changed.", 409);
        if (await db.RecipeCategories.AnyAsync(c => c.Id != (id ?? 0) && c.Type == type && c.Slug == slug, ct)) throw new ServiceException("Category already exists.", 409);
        var imageUrl = ImageUrlValidation.Normalize(request.ImageUrl);
        category.Name = request.Name.Trim(); category.Slug = slug; category.Type = type; category.Description = request.Description?.Trim(); category.ImageUrl = imageUrl; category.IsActive = request.IsActive;
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
        if (categories.Any(c => !Types.Contains(c.Type))) throw new ServiceException("Unsupported recipe category type.");
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
    public async Task SeedAsync(CancellationToken ct)
    {
        var obsoleteSlugs = new[] { "vegan", "lacto-vegetarian", "ovo-vegetarian", "lacto-ovo-vegetarian" };
        var obsolete = await db.RecipeCategories.Where(c => c.Type == "DIETARY_TYPE" && obsoleteSlugs.Contains(c.Slug)).ToListAsync(ct);
        if (obsolete.Count > 0)
        {
            var ids = obsolete.Select(c => c.Id).ToArray();
            db.RecipeCategoryMappings.RemoveRange(await db.RecipeCategoryMappings.Where(m => ids.Contains(m.CategoryId)).ToListAsync(ct));
            db.RecipeCategories.RemoveRange(obsolete);
        }
        var existing = (await db.RecipeCategories.ToListAsync(ct)).Select(c => c.Type + ":" + c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, name, slug) in DefaultCategories)
            if (existing.Add(type + ":" + slug)) db.RecipeCategories.Add(new RecipeCategory { Name = name, Slug = slug, Type = type, CreatedAt = VietnamTime.Now });
        await db.SaveChangesAsync(ct);
    }
}
