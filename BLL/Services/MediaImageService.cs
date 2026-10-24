using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public sealed class MediaImageService(
    IRepository<UserProfile> profiles, IRepository<Post> posts, IRepository<MealPlanMeal> meals,
    IImageStorage storage, IMealImageGenerator generator, ILogger<MediaImageService> logger)
{
    private sealed record Target(string? Url, string? PublicId, bool Generated, string Folder,
        Action<string?, string?, bool> Set, Func<CancellationToken, Task<int>> Save,
        string? RecipeName = null, IReadOnlyCollection<string>? Ingredients = null);

    private async Task<Target> AvatarAsync(int userId, CancellationToken ct)
    {
        var p = await profiles.Query().SingleOrDefaultAsync(x => x.UserId == userId, ct)
            ?? throw new ServiceException("User profile was not found.", 404);
        return new(p.AvatarUrl, p.AvatarPublicId, false, $"veggiemate/images/avatars/{userId}",
            (url, id, _) => { p.AvatarUrl = url; p.AvatarPublicId = id; }, profiles.SaveChangesAsync);
    }
    private async Task<Target> PostAsync(int postId, int userId, string role, CancellationToken ct)
    {
        var p = await posts.FindAsync(postId) ?? throw new ServiceException("Post was not found.", 404);
        if (p.UserId != userId && role != "ADMIN") throw new ServiceException("You cannot change this post's image.", 403);
        if (p.Status == "REMOVED") throw new ServiceException("Post was removed.", 404);
        return new(p.ImageUrl, p.ImagePublicId, false, $"veggiemate/images/posts/{postId}",
            (url, id, _) => { p.ImageUrl = url; p.ImagePublicId = id; p.UpdatedAt = VietnamTime.Now; }, posts.SaveChangesAsync);
    }
    private async Task<Target> MealAsync(int planId, int mealId, int userId, CancellationToken ct)
    {
        var m = await meals.Query().Include(x => x.MealPlan).Include(x => x.Recipe)
            .ThenInclude(x => x.Ingredients).ThenInclude(x => x.Ingredient)
            .SingleOrDefaultAsync(x => x.MealPlanMealId == mealId && x.MealPlanId == planId && x.MealPlan.UserId == userId, ct)
            ?? throw new ServiceException("Meal was not found in your meal plan.", 404);
        return new(m.ImageUrl, m.ImagePublicId, m.ImageIsGenerated, $"veggiemate/images/meals/{userId}/{mealId}",
            (url, id, generated) => { m.ImageUrl = url; m.ImagePublicId = id; m.ImageIsGenerated = generated; }, meals.SaveChangesAsync,
            m.Recipe.RecipeName, m.Recipe.Ingredients.Select(x => x.Ingredient.Name).Take(50).ToList());
    }

    public async Task<ImageResponse> UploadAvatarAsync(int userId, ImageFile file, CancellationToken ct) => await ReplaceAsync(await AvatarAsync(userId, ct), file, false, ct);
    public async Task<ImageResponse> UploadPostAsync(int postId, int userId, string role, ImageFile file, CancellationToken ct) => await ReplaceAsync(await PostAsync(postId, userId, role, ct), file, false, ct);
    public async Task<ImageResponse> UploadMealAsync(int planId, int mealId, int userId, ImageFile file, CancellationToken ct) => await ReplaceAsync(await MealAsync(planId, mealId, userId, ct), file, false, ct);
    public async Task<ImageResponse> GenerateMealAsync(int planId, int mealId, int userId, GenerateMealImageRequest request, CancellationToken ct)
    {
        var target = await MealAsync(planId, mealId, userId, ct); // Authorize before paid external calls.
        var file = await generator.GenerateAsync(target.RecipeName!, target.Ingredients!, request.Style, ct);
        return await ReplaceAsync(target, file, true, ct);
    }
    public async Task<ImageResponse> DeleteAvatarAsync(int userId, CancellationToken ct) => await RemoveAsync(await AvatarAsync(userId, ct), ct);
    public async Task<ImageResponse> DeletePostAsync(int postId, int userId, string role, CancellationToken ct) => await RemoveAsync(await PostAsync(postId, userId, role, ct), ct);
    public async Task<ImageResponse> DeleteMealAsync(int planId, int mealId, int userId, CancellationToken ct) => await RemoveAsync(await MealAsync(planId, mealId, userId, ct), ct);

    private async Task<ImageResponse> ReplaceAsync(Target target, ImageFile file, bool generated, CancellationToken ct)
    {
        ImageValidation.Validate(file.Bytes, file.MimeType);
        var image = await storage.UploadAsync(file, target.Folder, ct);
        try
        {
            target.Set(image.Url, image.PublicId, generated);
            await target.Save(ct);
        }
        catch (Exception ex)
        {
            target.Set(target.Url, target.PublicId, target.Generated);
            await CleanupAsync(image.PublicId);
            if (ex is DbUpdateConcurrencyException) throw new ServiceException("Image changed during this request. Reload and retry.", 409);
            throw;
        }
        var deleted = await CleanupAsync(target.PublicId);
        return new(image.Url, generated, deleted);
    }
    private async Task<ImageResponse> RemoveAsync(Target target, CancellationToken ct)
    {
        target.Set(null, null, false);
        try { await target.Save(ct); }
        catch (DbUpdateConcurrencyException) { throw new ServiceException("Image changed during this request. Reload and retry.", 409); }
        return new(null, false, await CleanupAsync(target.PublicId));
    }
    private async Task<bool> CleanupAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return true;
        // Cleanup should still finish after a client disconnects; bound its duration.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var deleted = await storage.DeleteAsync(id, timeout.Token);
            if (!deleted) logger.LogWarning("Image no longer referenced but cloud cleanup is pending: {PublicId}", id);
            return deleted;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { logger.LogWarning("Cloud cleanup is pending for {PublicId} ({ExceptionType}).", id, ex.GetType().Name); return false; }
    }
}
