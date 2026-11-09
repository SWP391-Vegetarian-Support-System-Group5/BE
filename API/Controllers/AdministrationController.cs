using API.Infrastructure;
using API.Services;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/admin")]
public class AdministrationController(IUserAdministrationService userAdministrationService, IReferenceDataAndRestaurantService referenceDataService, IPostService postService, IModerationService moderationService, IngredientCatalogService ingredientCatalog, IOpenStreetMapService mapService) : ControllerBase
{
    [HttpGet("users")] public Task<IReadOnlyCollection<UserResponse>> GetUsers(CancellationToken ct) => userAdministrationService.GetUsersAsync(ct);
    [HttpGet("users/{id:int}")] public Task<UserResponse> GetUser(int id, CancellationToken ct) => userAdministrationService.GetUserByIdAsync(id, ct);
    [HttpGet("users/{id:int}/activity")] public Task<AdminUserActivityResponse> GetUserActivity(int id, CancellationToken ct) => userAdministrationService.GetActivityAsync(id, ct);
    [HttpPut("users/{id:int}/status")] public async Task<IActionResult> UpdateUserStatus(int id, UpdateUserStatusRequest request, CancellationToken ct) { await userAdministrationService.UpdateStatusAsync(id, request.IsActive, ct); return NoContent(); }
    [HttpDelete("users/{id:int}")] public async Task<IActionResult> DeleteUser(int id, CancellationToken ct) { await userAdministrationService.DeleteAsync(id, ct); return NoContent(); }

    [HttpGet("posts")] public Task<IReadOnlyCollection<PostResponse>> GetPosts([FromQuery] string? keyword, [FromQuery] int? category, [FromQuery] string? postType, [FromQuery] string? categoryType, [FromQuery] string? status, CancellationToken ct) => postService.GetAsync(keyword, category, postType, categoryType, status, true, cancellationToken: ct);
    [HttpDelete("posts/{id:int}")] public async Task<IActionResult> DeletePost(int id, CancellationToken ct) { await postService.DeleteAsync(id, User.GetRequiredUserId(), "ADMIN", ct); return NoContent(); }
    [HttpGet("comments")] public Task<IReadOnlyCollection<CommentResponse>> GetComments(CancellationToken ct) => postService.GetAllCommentsAsync(ct);
    [HttpDelete("comments/{id:int}")] public async Task<IActionResult> DeleteComment(int id, CancellationToken ct) { await postService.DeleteCommentAsync(id, User.GetRequiredUserId(), "ADMIN", ct); return NoContent(); }
    [HttpPut("comments/{id:int}/visibility")] public async Task<IActionResult> SetCommentVisibility(int id, UpdateCommentVisibilityRequest request, CancellationToken ct) { await postService.SetCommentVisibilityAsync(id, request.Hidden, ct); return NoContent(); }
    [HttpPut("ingredients/{id:int}")] public Task<IngredientResponse> UpdateIngredient(int id, UpdateIngredientRequest request, CancellationToken ct) => ingredientCatalog.UpdateAsync(id, request, ct);
    [HttpPost("ingredient-categories")] public async Task<ActionResult<IngredientCategoryResponse>> CreateIngredientCategory(IngredientCategoryRequest request, CancellationToken ct) => Created("/api/ingredient-categories", await ingredientCatalog.CreateCategoryAsync(request, ct));
    [HttpPut("ingredient-categories/{id:int}")] public Task<IngredientCategoryResponse> UpdateIngredientCategory(int id, IngredientCategoryRequest request, CancellationToken ct) => ingredientCatalog.UpdateCategoryAsync(id, request, ct);

    [HttpPost("allergens")] public async Task<ActionResult<CatalogItemResponse>> CreateAllergen(AllergenRequest request, CancellationToken ct) => Created("/api/allergens", await referenceDataService.CreateAllergenAsync(request, ct));
    [HttpPut("allergens/{id:int}")] public async Task<IActionResult> UpdateAllergen(int id, AllergenRequest request, CancellationToken ct) { await referenceDataService.UpdateAllergenAsync(id, request, ct); return NoContent(); }
    [HttpDelete("allergens/{id:int}")] public async Task<IActionResult> DeleteAllergen(int id, CancellationToken ct) { await referenceDataService.DeleteAllergenAsync(id, ct); return NoContent(); }

    [HttpPost("restaurants")]
    public async Task<ActionResult<RestaurantResponse>> CreateRestaurant(RestaurantRequest request, CancellationToken ct) =>
        Created("/api/restaurants", await referenceDataService.CreateRestaurantAsync(await ResolveAddressAsync(request, ct), ct));

    [HttpPut("restaurants/{id:int}")]
    public async Task<IActionResult> UpdateRestaurant(int id, RestaurantRequest request, CancellationToken ct)
    {
        var current = await referenceDataService.GetRestaurantAsync(id, ct) ?? throw new ServiceException("Restaurant was not found.", 404);
        var resolved = string.Equals(current.Address.Trim(), request.Address.Trim(), StringComparison.OrdinalIgnoreCase)
            && current.Latitude.HasValue && current.Longitude.HasValue
            ? request with { Latitude = current.Latitude, Longitude = current.Longitude }
            : await ResolveAddressAsync(request, ct);
        await referenceDataService.UpdateRestaurantAsync(id, resolved, ct);
        return NoContent();
    }

    private async Task<RestaurantRequest> ResolveAddressAsync(RestaurantRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Address)) throw new ServiceException("Address is required.", 400);
        GeocodeResponse? location;
        try { location = await mapService.GeocodeAsync(request.Address, ct); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ServiceException("Map service is temporarily unavailable. Please try again.", 503, innerException: error);
        }
        if (location is null) throw new ServiceException("Address could not be found. Please enter a more detailed address.", 422);
        return request with { Latitude = location.Latitude, Longitude = location.Longitude };
    }
    [HttpDelete("restaurants/{id:int}")] public async Task<IActionResult> DeleteRestaurant(int id, CancellationToken ct) { await referenceDataService.DeleteRestaurantAsync(id, ct); return NoContent(); }

    [HttpGet("moderation")] public Task<IReadOnlyCollection<ModerationFlagResponse>> GetModeration(CancellationToken ct) => moderationService.GetAsync(ct);
    [HttpGet("moderation/{id:int}")] public Task<ModerationFlagResponse> GetModerationFlag(int id, CancellationToken ct) => moderationService.GetByIdAsync(id, ct);
    [HttpPost("moderation/{id:int}/approve")] public async Task<IActionResult> Approve(int id, CancellationToken ct) { await moderationService.ReviewAsync(id, User.GetRequiredUserId(), false, ct); return NoContent(); }
    [HttpPost("moderation/{id:int}/remove")] public async Task<IActionResult> Remove(int id, CancellationToken ct) { await moderationService.ReviewAsync(id, User.GetRequiredUserId(), true, ct); return NoContent(); }
}
