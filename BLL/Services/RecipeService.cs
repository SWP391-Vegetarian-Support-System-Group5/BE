using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class RecipeService(
    IRepository<Post> posts,
    IRepository<Recipe> recipes,
    IRepository<RecipeIngredient> ingredients,
    IRepository<RecipeStep> steps,
    IRepository<RecipeDietCompatibility> dietCompatibilities,
    IRepository<PostTag> postTags,
    IRepository<Category> categories,
    IRepository<Allergen> allergens,
    IRepository<DietType> dietTypes,
    IRepository<Tag> tags) : IRecipeService
{
    public async Task<RecipeResponse> CreateAsync(int userId, CreateRecipeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRecipeRequest(request);
        await ValidateRelationsAsync(request, cancellationToken);
        var post = new Post { UserId = userId, CategoryId = request.CategoryId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = "RECIPE", VideoUrl = request.VideoUrl?.Trim(), Status = "PUBLISHED" };
        await posts.AddAsync(post, cancellationToken); await posts.SaveChangesAsync(cancellationToken);
        var recipe = new Recipe { PostId = post.PostId, PrepMinutes = request.PrepMinutes, CookMinutes = request.CookMinutes, Servings = request.Servings, CaloriesPerServing = request.CaloriesPerServing, ProteinPerServing = request.ProteinPerServing, CarbsPerServing = request.CarbsPerServing, FatPerServing = request.FatPerServing, VideoUrl = request.VideoUrl?.Trim() };
        await recipes.AddAsync(recipe, cancellationToken);
        await ReplaceDetailsAsync(recipe.PostId, request, cancellationToken);
        await recipes.SaveChangesAsync(cancellationToken);
        return await GetAsync(post.PostId, cancellationToken);
    }

    public async Task<RecipeResponse> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        if (recipe.Post.Status != "PUBLISHED") throw new ServiceException("Recipe was not found.", 404);
        return ToResponse(recipe);
    }

    public async Task UpdateAsync(int id, int actorId, string role, CreateRecipeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRecipeRequest(request);
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        EnsureOwner(recipe.Post.UserId, actorId, role);
        await ValidateRelationsAsync(request, cancellationToken);
        recipe.Post.Title = request.Title.Trim(); recipe.Post.Content = request.Content.Trim(); recipe.Post.CategoryId = request.CategoryId; recipe.Post.VideoUrl = request.VideoUrl?.Trim();
        recipe.PrepMinutes = request.PrepMinutes; recipe.CookMinutes = request.CookMinutes; recipe.Servings = request.Servings; recipe.CaloriesPerServing = request.CaloriesPerServing; recipe.ProteinPerServing = request.ProteinPerServing; recipe.CarbsPerServing = request.CarbsPerServing; recipe.FatPerServing = request.FatPerServing; recipe.VideoUrl = request.VideoUrl?.Trim();
        foreach (var item in recipe.Ingredients) ingredients.Remove(item);
        foreach (var item in recipe.Steps) steps.Remove(item);
        foreach (var item in recipe.DietCompatibilities) dietCompatibilities.Remove(item);
        var existingTags = await postTags.Query().Where(x => x.PostId == id).ToListAsync(cancellationToken);
        foreach (var tag in existingTags) postTags.Remove(tag);
        await recipes.SaveChangesAsync(cancellationToken);
        await ReplaceDetailsAsync(id, request, cancellationToken);
        await recipes.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        EnsureOwner(recipe.Post.UserId, actorId, role); recipe.Post.Status = "REMOVED"; await recipes.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<RecipeResponse>> SearchAsync(string? keyword, int? categoryId, int? tagId, CancellationToken cancellationToken = default)
    {
        var query = BaseQuery().Where(x => x.Post.Status == "PUBLISHED");
        if (!string.IsNullOrWhiteSpace(keyword)) { var term = keyword.Trim().ToLower(); query = query.Where(x => x.Post.Title.ToLower().Contains(term) || x.Ingredients.Any(i => i.IngredientName.ToLower().Contains(term)) || (x.Post.Category != null && x.Post.Category.Name.ToLower().Contains(term)) || x.Post.PostTags.Any(pt => pt.Tag.Name.ToLower().Contains(term))); }
        if (categoryId.HasValue) query = query.Where(x => x.Post.CategoryId == categoryId);
        if (tagId.HasValue) query = query.Where(x => x.Post.PostTags.Any(pt => pt.TagId == tagId));
        return (await query.OrderByDescending(x => x.PostId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    }

    private async Task ReplaceDetailsAsync(int recipeId, CreateRecipeRequest request, CancellationToken ct)
    {
        foreach (var item in request.Ingredients ?? []) await ingredients.AddAsync(new RecipeIngredient { RecipeId = recipeId, IngredientName = item.IngredientName.Trim(), Amount = item.Amount?.Trim(), AllergenId = item.AllergenId }, ct);
        foreach (var step in request.Steps ?? []) await steps.AddAsync(new RecipeStep { RecipeId = recipeId, StepNumber = step.StepNumber, Instruction = step.Instruction.Trim() }, ct);
        foreach (var dietTypeId in (request.CompatibleDietTypeIds ?? []).Distinct()) await dietCompatibilities.AddAsync(new RecipeDietCompatibility { RecipeId = recipeId, DietTypeId = dietTypeId, IsCompatible = true }, ct);
        foreach (var tagId in (request.TagIds ?? []).Distinct()) await postTags.AddAsync(new PostTag { PostId = recipeId, TagId = tagId }, ct);
    }
    private IQueryable<Recipe> BaseQuery() => recipes.Query().Include(x => x.Post).ThenInclude(x => x.Category).Include(x => x.Post).ThenInclude(x => x.PostTags).ThenInclude(x => x.Tag).Include(x => x.Ingredients).Include(x => x.Steps).Include(x => x.DietCompatibilities);
    private async Task ValidateRelationsAsync(CreateRecipeRequest request, CancellationToken ct)
    {
        if (request.CategoryId.HasValue && !await categories.Query().AnyAsync(x => x.CategoryId == request.CategoryId.Value, ct)) throw new ServiceException("Category was not found.", 404);
        await EnsureAllExistAsync((request.Ingredients ?? []).Where(x => x.AllergenId.HasValue).Select(x => x.AllergenId!.Value), allergens.Query().Select(x => x.AllergenId), "One or more allergens were not found.", ct);
        await EnsureAllExistAsync(request.CompatibleDietTypeIds ?? [], dietTypes.Query().Select(x => x.DietTypeId), "One or more diet types were not found.", ct);
        await EnsureAllExistAsync(request.TagIds ?? [], tags.Query().Select(x => x.TagId), "One or more tags were not found.", ct);
    }
    private static async Task EnsureAllExistAsync(IEnumerable<int> values, IQueryable<int> availableIds, string message, CancellationToken ct)
    {
        var ids = values.Distinct().ToArray();
        if (ids.Length > 0 && await availableIds.CountAsync(id => ids.Contains(id), ct) != ids.Length) throw new ServiceException(message, 404);
    }
    private static RecipeResponse ToResponse(Recipe x) => new(x.PostId, x.Post.Title, x.Post.Content, x.Post.Status, x.CaloriesPerServing, x.Servings, x.Ingredients.OrderBy(i => i.RecipeIngredientId).Select(i => new IngredientRequest(i.IngredientName, i.Amount, i.AllergenId)).ToList(), x.Steps.OrderBy(s => s.StepNumber).Select(s => new RecipeStepRequest(s.StepNumber, s.Instruction)).ToList(), x.DietCompatibilities.Where(d => d.IsCompatible).Select(d => d.DietTypeId).ToList());
    private static void ValidateRecipeRequest(CreateRecipeRequest request)
    {
        if ((request.Steps ?? []).GroupBy(x => x.StepNumber).Any(g => g.Count() > 1)) throw new ServiceException("Recipe step numbers must be unique.");
        if (request.CaloriesPerServing is < 0 || request.Servings is <= 0) throw new ServiceException("Calories must be non-negative and servings must be greater than zero.");
    }
    private static void EnsureOwner(int ownerId, int actorId, string role) { if (ownerId != actorId && role != "ADMIN") throw new ServiceException("You do not have permission to modify this recipe.", 403); }
}
