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
    IRepository<Allergen> allergens,
    IRepository<DietType> dietTypes,
    IRepository<ModerationFlag> moderationFlags,
    IModerationAiService moderationAiService,
    ICommunityEventPublisher communityEvents,
    RecipeCategoryService recipeCategories,
    IngredientCatalogService ingredientCatalog,
    DAL.Data.VegetarianDbContext db,
    RecipeBmiService recipeBmi,
    IUserNotificationService? notifications = null) : IRecipeService
{
    public async Task<RecipeResponse> CreateAsync(int userId, CreateRecipeRequest request, CancellationToken cancellationToken = default, string? role = null)
    {
        ValidateRecipeRequest(request);
        var imageUrl = ImageUrlValidation.Normalize(request.ImageUrl);
        await recipeBmi.ValidateAsync(request.BmiTypeIds, userId, cancellationToken);
        await ValidateRelationsAsync(request, cancellationToken);
        // Administrators are the editorial authority: their create/edit actions publish directly.
        // Member-submitted recipes still go through AI moderation below.
        var moderationReason = role == "ADMIN" ? null : await moderationAiService.FlagReasonAsync(ModerationText(request), cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var videoUrl = request.VideoUrl?.Trim();
        var now = VietnamTime.Now;
        var post = new Post { UserId = userId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = "RECIPE", VideoUrl = videoUrl, ThumbnailUrl = imageUrl ?? MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(videoUrl), Status = moderationReason is null ? "PUBLISHED" : "DRAFT", CreatedAt = now, UpdatedAt = now };
        await posts.AddAsync(post, cancellationToken); await posts.SaveChangesAsync(cancellationToken);
        if (moderationReason is not null) await moderationFlags.AddAsync(new ModerationFlag { PostId = post.PostId, Source = "AI", Reason = moderationReason, Status = "PENDING" }, cancellationToken);
        var recipe = new Recipe { PostId = post.PostId, RecipeName = request.RecipeName.Trim(), PrepMinutes = request.PrepMinutes, CookMinutes = request.CookMinutes, Servings = request.Servings, VideoUrl = videoUrl, ImageUrl = imageUrl };
        await recipes.AddAsync(recipe, cancellationToken);
        await ReplaceDetailsAsync(recipe.PostId, request, cancellationToken);
        await recipeBmi.SetMappingsAsync(recipe.PostId, request.BmiTypeIds, cancellationToken);
        await recipes.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = await GetAsync(post.PostId, cancellationToken, userId);
        var createdPost = await PostBaseQuery().SingleAsync(x => x.PostId == post.PostId, cancellationToken);
        if (post.Status == "PUBLISHED") await communityEvents.PublishAsync(new CommunityEvent("postCreated", post.PostId, Post: ToPostResponse(createdPost, userId)), cancellationToken);
        return response;
    }

    public async Task<RecipeResponse> GetAsync(int id, CancellationToken cancellationToken = default, int? actorId = null, string? role = null)
    {
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        if (recipe.Post.Status != "PUBLISHED" && recipe.Post.UserId != actorId && role != "ADMIN") throw new ServiceException("Recipe was not found.", 404);
        if (!VegetarianDietRules.IsVerifiedVegetarianRecipe(recipe.Ingredients.Select(x => x.Ingredient.Category.DietaryGroup)) && recipe.Post.UserId != actorId && role != "ADMIN") throw new ServiceException("Recipe was not found.", 404);
        return ToResponse(recipe, actorId);
    }

    public async Task UpdateAsync(int id, int actorId, string role, CreateRecipeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRecipeRequest(request);
        var imageUrl = ImageUrlValidation.Normalize(request.ImageUrl);
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        EnsureOwner(recipe.Post.UserId, actorId, role);
        if (recipe.Post.Status == "REMOVED") throw new ServiceException("Bài đã bị gỡ không thể đăng lại.", 409);
        await recipeBmi.ValidateAsync(request.BmiTypeIds, actorId, cancellationToken);
        await ValidateRelationsAsync(request, cancellationToken);
        // An administrator may correct content directly without routing their work back to AI review.
        var moderationReason = role == "ADMIN" ? null : await moderationAiService.FlagReasonAsync(ModerationText(request), cancellationToken);
        var wasPublished = recipe.Post.Status == "PUBLISHED";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var videoUrl = request.VideoUrl?.Trim();
        recipe.Post.Title = request.Title.Trim(); recipe.Post.Content = request.Content.Trim(); recipe.Post.VideoUrl = videoUrl; recipe.Post.ThumbnailUrl = imageUrl ?? MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(videoUrl); recipe.Post.UpdatedAt = VietnamTime.Now;
        recipe.Post.Status = moderationReason is null ? "PUBLISHED" : "DRAFT";
        var pendingFlags = await moderationFlags.Query().Where(f => f.PostId == id && f.Source == "AI" && f.Status == "PENDING").ToListAsync(cancellationToken);
        foreach (var flag in pendingFlags) flag.Status = "APPROVED";
        if (moderationReason is not null) await moderationFlags.AddAsync(new ModerationFlag { PostId = id, Source = "AI", Reason = moderationReason, Status = "PENDING" }, cancellationToken);
        recipe.RecipeName = request.RecipeName.Trim();
        recipe.PrepMinutes = request.PrepMinutes; recipe.CookMinutes = request.CookMinutes; recipe.Servings = request.Servings; recipe.VideoUrl = videoUrl; recipe.ImageUrl = imageUrl;
        foreach (var item in recipe.Ingredients) ingredients.Remove(item);
        foreach (var item in recipe.Steps) steps.Remove(item);
        foreach (var item in recipe.DietCompatibilities) dietCompatibilities.Remove(item);
        await recipes.SaveChangesAsync(cancellationToken);
        await ReplaceDetailsAsync(id, request, cancellationToken);
        await recipeBmi.SetMappingsAsync(id, request.BmiTypeIds, cancellationToken);
        await recipes.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (recipe.Post.Status == "PUBLISHED") await communityEvents.PublishAsync(new CommunityEvent(wasPublished ? "postUpdated" : "postCreated", id), cancellationToken);
        else if (wasPublished) await communityEvents.PublishAsync(new CommunityEvent("postRemoved", id), cancellationToken);
    }

    public async Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var recipe = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Recipe was not found.", 404);
        EnsureOwner(recipe.Post.UserId, actorId, role);
        if (recipe.Post.Status == "REMOVED") return;
        recipe.Post.Status = "REMOVED"; recipe.Post.UpdatedAt = VietnamTime.Now; await recipes.SaveChangesAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("postRemoved", id), cancellationToken);
        if (role == "ADMIN" && recipe.Post.UserId != actorId && notifications is not null)
            await notifications.CreateAsync(recipe.Post.UserId, "POST_REMOVED", "Bài viết của bạn đã bị quản trị viên gỡ.", id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RecipeResponse>> SearchAsync(string? keyword, int? categoryId, string? categoryType, CancellationToken cancellationToken = default)
    {
        var query = BaseQuery().Where(x => x.Post.Status == "PUBLISHED" && x.Ingredients.Any() && !x.Ingredients.Any(i => i.Ingredient.Category.DietaryGroup == "UNVERIFIED"));
        if (!string.IsNullOrWhiteSpace(keyword)) { var term = keyword.Trim().ToLower(); query = query.Where(x => x.RecipeName.ToLower().Contains(term) || x.Post.Title.ToLower().Contains(term) || x.Ingredients.Any(i => i.Ingredient.Name.ToLower().Contains(term))); }
        if (categoryId.HasValue) query = query.Where(x => x.CategoryMappings.Any(m => m.CategoryId == categoryId && m.Category.IsActive));
        if (!string.IsNullOrWhiteSpace(categoryType)) { var type = RecipeCategoryService.ValidateType(categoryType); query = query.Where(x => x.CategoryMappings.Any(m => m.Category.Type == type && m.Category.IsActive)); }
        return (await query.OrderByDescending(x => x.PostId).ToListAsync(cancellationToken)).Select(x => ToResponse(x)).ToList();
    }

    private async Task ReplaceDetailsAsync(int recipeId, CreateRecipeRequest request, CancellationToken ct)
    {
        var recipeIngredients = await ingredientCatalog.ResolveAsync(request.Ingredients ?? [], ct);
        var groups = recipeIngredients.Select(x => x.Ingredient.Category.DietaryGroup).ToArray();
        var compatibleDietTypeNames = VegetarianDietRules.GetCompatibleDietTypes(groups);
        var compatibleDietTypeIds = await dietTypes.Query().Where(x => compatibleDietTypeNames.Contains(x.Name)).Select(x => x.DietTypeId).ToListAsync(ct);
        if (compatibleDietTypeIds.Count != compatibleDietTypeNames.Count) throw new ServiceException("Diet type configuration is incomplete. Restart the API to seed the supported diet types.", 500);

        foreach (var ingredient in recipeIngredients) await ingredients.AddAsync(new RecipeIngredient { RecipeId = recipeId, IngredientId = ingredient.Ingredient.IngredientId, Amount = ingredient.Amount }, ct);
        foreach (var step in request.Steps ?? []) await steps.AddAsync(new RecipeStep { RecipeId = recipeId, StepNumber = step.StepNumber, Instruction = step.Instruction.Trim() }, ct);
        foreach (var dietTypeId in compatibleDietTypeIds) await dietCompatibilities.AddAsync(new RecipeDietCompatibility { RecipeId = recipeId, DietTypeId = dietTypeId, IsCompatible = true }, ct);
        await recipeCategories.SetMappingsAsync(recipeId, request.CategoryIds, groups, ct);
    }
    private IQueryable<Recipe> BaseQuery() => recipes.Query().AsSplitQuery().Include(x => x.Post).ThenInclude(x => x.ModerationFlags).Include(x => x.BMITypes).ThenInclude(x => x.BMIType).Include(x => x.CategoryMappings).ThenInclude(x => x.Category).Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category).Include(x => x.Steps).Include(x => x.DietCompatibilities);
    private IQueryable<Post> PostBaseQuery() => posts.Query().AsSplitQuery().Include(x => x.User).ThenInclude(x => x.Profile).Include(x => x.Ratings).Include(x => x.Comments).Include(x => x.Recipe).ThenInclude(x => x!.DietCompatibilities).ThenInclude(x => x.DietType);
    private async Task ValidateRelationsAsync(CreateRecipeRequest request, CancellationToken ct)
    {
        await EnsureAllExistAsync((request.Ingredients ?? []).Where(x => x.AllergenId.HasValue).Select(x => x.AllergenId!.Value), allergens.Query().Select(x => x.AllergenId), "One or more allergens were not found.", ct);
        await recipeCategories.ValidateSelectionAsync(request.CategoryIds, (request.Ingredients ?? []).Select(i => i.DietaryGroup), ct);
    }
    private static async Task EnsureAllExistAsync(IEnumerable<int> values, IQueryable<int> availableIds, string message, CancellationToken ct)
    {
        var ids = values.Distinct().ToArray();
        if (ids.Length > 0 && await availableIds.CountAsync(id => ids.Contains(id), ct) != ids.Length) throw new ServiceException(message, 404);
    }
    private static RecipeResponse ToResponse(Recipe x, int? actorId = null) => new(x.PostId, x.Post.Title, x.Post.Content, x.Post.Status, x.VideoUrl ?? x.Post.VideoUrl, x.Post.ThumbnailUrl ?? MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(x.VideoUrl ?? x.Post.VideoUrl), x.PrepMinutes, x.CookMinutes, x.Servings, x.Ingredients.OrderBy(i => i.RecipeIngredientId).Select(i => new IngredientRequest(i.Ingredient.Name, i.Amount, i.Ingredient.Category.DietaryGroup, i.Ingredient.AllergenId, i.Ingredient.IngredientCategoryId)).ToList(), x.Steps.OrderBy(s => s.StepNumber).Select(s => new RecipeStepRequest(s.StepNumber, s.Instruction)).ToList(), x.DietCompatibilities.Where(d => d.IsCompatible).Select(d => d.DietTypeId).ToList(), x.RecipeName, x.CategoryMappings.Where(m => m.Category.IsActive).Select(m => RecipeCategoryService.ToResponse(m.Category)).ToList(), x.BMITypes.Select(m => new BmiTypeResponse(m.BMITypeId, m.BMIType.Code, m.BMIType.Name, m.BMIType.Description)).ToList(), actorId == x.Post.UserId ? x.Post.ModerationFlags.Where(f => f.Source == "AI" && f.Status == "PENDING").OrderByDescending(f => f.FlagId).FirstOrDefault()?.Reason : null, x.ImageUrl);
    private static string ModerationText(CreateRecipeRequest request) => $"Tiêu đề: {request.Title}\nTên món: {request.RecipeName}\nNội dung: {request.Content}\nNguyên liệu: {string.Join(", ", (request.Ingredients ?? []).Select(x => x.IngredientName))}\nCác bước: {string.Join("; ", (request.Steps ?? []).Select(x => x.Instruction))}";
    private static PostResponse ToPostResponse(Post x, int? actorId = null) => new(
        x.PostId,
        x.UserId,
        x.User.Profile?.FullName ?? "Unknown user",
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
        x.UpdatedAt,
        x.Recipe?.DietCompatibilities.Where(d => d.IsCompatible).OrderBy(d => d.DietTypeId).Select(d => new PostDietTypeResponse(d.DietTypeId, d.DietType.Name)).ToList() ?? []);
    private static void ValidateRecipeRequest(CreateRecipeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RecipeName) || request.RecipeName.Trim().Length > 250) throw new ServiceException("Recipe name is required and must not exceed 250 characters.");
        if ((request.Steps ?? []).GroupBy(x => x.StepNumber).Any(g => g.Count() > 1)) throw new ServiceException("Recipe step numbers must be unique.");
        if (request.Servings is <= 0) throw new ServiceException("Servings must be greater than zero.");
        if (request.Ingredients is not { Count: > 0 }) throw new ServiceException("A recipe must include at least one classified ingredient.");
    }
    private static void EnsureOwner(int ownerId, int actorId, string role) { if (ownerId != actorId && role != "ADMIN") throw new ServiceException("You do not have permission to modify this recipe.", 403); }
}
