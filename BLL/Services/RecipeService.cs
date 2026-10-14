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
    IRepository<Category> categories,
    IRepository<Allergen> allergens,
    IRepository<DietType> dietTypes,
    ICommunityEventPublisher communityEvents,
    RecipeCategoryService recipeCategories,
    DAL.Data.VegetarianDbContext db) : IRecipeService
{
    public async Task<RecipeResponse> CreateAsync(int userId, CreateRecipeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRecipeRequest(request);
        await ValidateRelationsAsync(request, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var videoUrl = request.VideoUrl?.Trim();
        var now = VietnamTime.Now;
        var post = new Post { UserId = userId, CategoryId = request.CategoryId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = "RECIPE", VideoUrl = videoUrl, ThumbnailUrl = MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(videoUrl), Status = "PUBLISHED", CreatedAt = now, UpdatedAt = now };
        await posts.AddAsync(post, cancellationToken); await posts.SaveChangesAsync(cancellationToken);
        var recipe = new Recipe { PostId = post.PostId, RecipeName = request.RecipeName.Trim(), PrepMinutes = request.PrepMinutes, CookMinutes = request.CookMinutes, Servings = request.Servings, CaloriesPerServing = request.CaloriesPerServing, VideoUrl = videoUrl };
        await recipes.AddAsync(recipe, cancellationToken);
        await ReplaceDetailsAsync(recipe.PostId, request, cancellationToken);
        await recipes.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = await GetAsync(post.PostId, cancellationToken);
        var createdPost = await PostBaseQuery().SingleAsync(x => x.PostId == post.PostId, cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("postCreated", post.PostId, Post: ToPostResponse(createdPost, userId)), cancellationToken);
        return response;
    }

    public async Task<RecipeResponse> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        if (recipe.Post.Status != "PUBLISHED" || !VegetarianDietRules.IsVerifiedVegetarianRecipe(recipe.Ingredients.Select(x => x.DietaryGroup))) throw new ServiceException("Recipe was not found.", 404);
        return ToResponse(recipe);
    }

    public async Task UpdateAsync(int id, int actorId, string role, CreateRecipeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRecipeRequest(request);
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        EnsureOwner(recipe.Post.UserId, actorId, role);
        await ValidateRelationsAsync(request, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var videoUrl = request.VideoUrl?.Trim();
        recipe.Post.Title = request.Title.Trim(); recipe.Post.Content = request.Content.Trim(); recipe.Post.CategoryId = request.CategoryId; recipe.Post.VideoUrl = videoUrl; recipe.Post.ThumbnailUrl = MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(videoUrl); recipe.Post.UpdatedAt = VietnamTime.Now;
        recipe.RecipeName = request.RecipeName.Trim();
        recipe.PrepMinutes = request.PrepMinutes; recipe.CookMinutes = request.CookMinutes; recipe.Servings = request.Servings; recipe.CaloriesPerServing = request.CaloriesPerServing; recipe.VideoUrl = videoUrl;
        foreach (var item in recipe.Ingredients) ingredients.Remove(item);
        foreach (var item in recipe.Steps) steps.Remove(item);
        foreach (var item in recipe.DietCompatibilities) dietCompatibilities.Remove(item);
        await recipes.SaveChangesAsync(cancellationToken);
        await ReplaceDetailsAsync(id, request, cancellationToken);
        await recipes.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("postUpdated", id), cancellationToken);
    }

    public async Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        EnsureOwner(recipe.Post.UserId, actorId, role); recipe.Post.Status = "REMOVED"; recipe.Post.UpdatedAt = VietnamTime.Now; await recipes.SaveChangesAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("postRemoved", id), cancellationToken);
    }

    public async Task<IReadOnlyCollection<RecipeResponse>> SearchAsync(string? keyword, int? categoryId, string? categoryType, CancellationToken cancellationToken = default)
    {
        var query = BaseQuery().Where(x => x.Post.Status == "PUBLISHED" && x.Ingredients.Any() && !x.Ingredients.Any(i => i.DietaryGroup == "UNVERIFIED"));
        if (!string.IsNullOrWhiteSpace(keyword)) { var term = keyword.Trim().ToLower(); query = query.Where(x => x.RecipeName.ToLower().Contains(term) || x.Post.Title.ToLower().Contains(term) || x.Ingredients.Any(i => i.IngredientName.ToLower().Contains(term)) || (x.Post.Category != null && x.Post.Category.Name.ToLower().Contains(term))); }
        if (categoryId.HasValue) query = query.Where(x => x.CategoryMappings.Any(m => m.CategoryId == categoryId && m.Category.IsActive));
        if (!string.IsNullOrWhiteSpace(categoryType)) { var type = RecipeCategoryService.ValidateType(categoryType); query = query.Where(x => x.CategoryMappings.Any(m => m.Category.Type == type && m.Category.IsActive)); }
        return (await query.OrderByDescending(x => x.PostId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    }

    private async Task ReplaceDetailsAsync(int recipeId, CreateRecipeRequest request, CancellationToken ct)
    {
        var recipeIngredients = (request.Ingredients ?? []).Select(item => new { Item = item, DietaryGroup = VegetarianDietRules.NormalizeIngredientGroup(item.DietaryGroup) }).ToList();
        var compatibleDietTypeNames = VegetarianDietRules.GetCompatibleDietTypes(recipeIngredients.Select(x => x.DietaryGroup));
        var compatibleDietTypeIds = await dietTypes.Query().Where(x => x.IsActive && compatibleDietTypeNames.Contains(x.Name)).Select(x => x.DietTypeId).ToListAsync(ct);
        if (compatibleDietTypeIds.Count != compatibleDietTypeNames.Count) throw new ServiceException("Diet type configuration is incomplete. Restart the API to seed the supported diet types.", 500);

        foreach (var ingredient in recipeIngredients) await ingredients.AddAsync(new RecipeIngredient { RecipeId = recipeId, IngredientName = ingredient.Item.IngredientName.Trim(), Amount = ingredient.Item.Amount?.Trim(), DietaryGroup = ingredient.DietaryGroup, AllergenId = ingredient.Item.AllergenId }, ct);
        foreach (var step in request.Steps ?? []) await steps.AddAsync(new RecipeStep { RecipeId = recipeId, StepNumber = step.StepNumber, Instruction = step.Instruction.Trim() }, ct);
        foreach (var dietTypeId in compatibleDietTypeIds) await dietCompatibilities.AddAsync(new RecipeDietCompatibility { RecipeId = recipeId, DietTypeId = dietTypeId, IsCompatible = true }, ct);
        await recipeCategories.SetMappingsAsync(recipeId, request.CategoryIds, recipeIngredients.Select(i => i.DietaryGroup), ct);
    }
    private IQueryable<Recipe> BaseQuery() => recipes.Query().AsSplitQuery().Include(x => x.Post).ThenInclude(x => x.Category).Include(x => x.CategoryMappings).ThenInclude(x => x.Category).Include(x => x.Ingredients).Include(x => x.Steps).Include(x => x.DietCompatibilities);
    private IQueryable<Post> PostBaseQuery() => posts.Query().Include(x => x.User).ThenInclude(x => x.Profile).Include(x => x.Category).Include(x => x.Ratings).Include(x => x.Comments);
    private async Task ValidateRelationsAsync(CreateRecipeRequest request, CancellationToken ct)
    {
        if (request.CategoryId.HasValue && !await categories.Query().AnyAsync(x => x.CategoryId == request.CategoryId.Value, ct)) throw new ServiceException("Category was not found.", 404);
        await EnsureAllExistAsync((request.Ingredients ?? []).Where(x => x.AllergenId.HasValue).Select(x => x.AllergenId!.Value), allergens.Query().Where(x => x.IsActive).Select(x => x.AllergenId), "One or more allergens were not found or are inactive.", ct);
        await recipeCategories.ValidateSelectionAsync(request.CategoryIds, (request.Ingredients ?? []).Select(i => i.DietaryGroup), ct);
    }
    private static async Task EnsureAllExistAsync(IEnumerable<int> values, IQueryable<int> availableIds, string message, CancellationToken ct)
    {
        var ids = values.Distinct().ToArray();
        if (ids.Length > 0 && await availableIds.CountAsync(id => ids.Contains(id), ct) != ids.Length) throw new ServiceException(message, 404);
    }
    private static RecipeResponse ToResponse(Recipe x) => new(x.PostId, x.Post.Title, x.Post.Content, x.Post.Status, x.VideoUrl ?? x.Post.VideoUrl, x.Post.ThumbnailUrl ?? MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(x.VideoUrl ?? x.Post.VideoUrl), x.PrepMinutes, x.CookMinutes, x.Servings, x.CaloriesPerServing, x.Ingredients.OrderBy(i => i.RecipeIngredientId).Select(i => new IngredientRequest(i.IngredientName, i.Amount, i.DietaryGroup, i.AllergenId)).ToList(), x.Steps.OrderBy(s => s.StepNumber).Select(s => new RecipeStepRequest(s.StepNumber, s.Instruction)).ToList(), x.DietCompatibilities.Where(d => d.IsCompatible).Select(d => d.DietTypeId).ToList(), x.RecipeName, x.CategoryMappings.Where(m => m.Category.IsActive).Select(m => RecipeCategoryService.ToResponse(m.Category)).ToList());
    private static PostResponse ToPostResponse(Post x, int? actorId = null) => new(
        x.PostId,
        x.UserId,
        x.User.Profile?.FullName ?? "Unknown user",
        x.CategoryId,
        x.Category?.Name,
        x.Title,
        x.Content,
        x.PostType,
        x.VideoUrl,
        x.ThumbnailUrl ?? MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(x.VideoUrl),
        x.Status,
        x.Ratings.Count == 0 ? 0 : x.Ratings.Average(r => r.Rating),
        x.Ratings.Count,
        x.Comments.Count(c => c.Status == "PUBLISHED"),
        actorId.HasValue ? x.Ratings.FirstOrDefault(r => r.UserId == actorId.Value)?.Rating : null,
        x.CreatedAt,
        x.UpdatedAt);
    private static void ValidateRecipeRequest(CreateRecipeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecipeName) || request.RecipeName.Trim().Length > 250) throw new ServiceException("Recipe name is required and must not exceed 250 characters.");
        if ((request.Steps ?? []).GroupBy(x => x.StepNumber).Any(g => g.Count() > 1)) throw new ServiceException("Recipe step numbers must be unique.");
        if (request.Servings is <= 0) throw new ServiceException("Servings must be greater than zero.");
        if (request.CaloriesPerServing is <= 0) throw new ServiceException("Calories per serving must be greater than zero.");
        if (request.Ingredients is not { Count: > 0 }) throw new ServiceException("A recipe must include at least one classified ingredient.");
    }
    private static void EnsureOwner(int ownerId, int actorId, string role) { if (ownerId != actorId && role != "ADMIN") throw new ServiceException("You do not have permission to modify this recipe.", 403); }
}
