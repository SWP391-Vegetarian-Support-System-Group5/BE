using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/admin")]
public class AdministrationController(IUserAdministrationService userAdministrationService, IReferenceDataAndRestaurantService referenceDataService, IPostService postService, IModerationService moderationService) : ControllerBase
{
    [HttpGet("users")] public Task<IReadOnlyCollection<AdminUserResponse>> GetUsers(CancellationToken ct) => userAdministrationService.GetUsersAsync(ct);
    [HttpGet("users/{id:int}")] public Task<AdminUserResponse> GetUser(int id, CancellationToken ct) => userAdministrationService.GetUserByIdAsync(id, ct);
    [HttpPut("users/{id:int}/status")] public async Task<IActionResult> UpdateUserStatus(int id, UpdateUserStatusRequest request, CancellationToken ct) { await userAdministrationService.UpdateStatusAsync(id, request.IsActive, ct); return NoContent(); }
    [HttpDelete("users/{id:int}")] public async Task<IActionResult> DeleteUser(int id, CancellationToken ct) { await userAdministrationService.DeleteAsync(id, ct); return NoContent(); }

    [HttpGet("posts")] public Task<IReadOnlyCollection<PostResponse>> GetPosts([FromQuery] string? keyword, [FromQuery] int? category, [FromQuery] string? postType, [FromQuery] string? categoryType, [FromQuery] string? status, CancellationToken ct) => postService.GetAsync(keyword, category, postType, categoryType, status, true, cancellationToken: ct);
    [HttpDelete("posts/{id:int}")] public async Task<IActionResult> DeletePost(int id, CancellationToken ct) { await postService.DeleteAsync(id, User.GetRequiredUserId(), "ADMIN", ct); return NoContent(); }
    [HttpPut("posts/{id:int}/status")] public async Task<IActionResult> UpdatePostStatus(int id, UpdatePostStatusRequest request, CancellationToken ct) { await postService.SetStatusAsync(id, request.Status, ct); return NoContent(); }
    [HttpGet("comments")] public Task<IReadOnlyCollection<CommentResponse>> GetComments(CancellationToken ct) => postService.GetAllCommentsAsync(ct);
    [HttpDelete("comments/{id:int}")] public async Task<IActionResult> DeleteComment(int id, CancellationToken ct) { await postService.DeleteCommentAsync(id, User.GetRequiredUserId(), "ADMIN", ct); return NoContent(); }

    [HttpPost("categories")] public async Task<ActionResult<CatalogItemResponse>> CreateCategory(CategoryRequest request, CancellationToken ct) => Created("/api/categories", await referenceDataService.CreateCategoryAsync(request, ct));
    [HttpPut("categories/{id:int}")] public async Task<IActionResult> UpdateCategory(int id, CategoryRequest request, CancellationToken ct) { await referenceDataService.UpdateCategoryAsync(id, request, ct); return NoContent(); }
    [HttpDelete("categories/{id:int}")] public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct) { await referenceDataService.DeleteCategoryAsync(id, ct); return NoContent(); }
    [HttpPost("allergens")] public async Task<ActionResult<CatalogItemResponse>> CreateAllergen(AllergenRequest request, CancellationToken ct) => Created("/api/allergens", await referenceDataService.CreateAllergenAsync(request, ct));
    [HttpGet("allergens")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergens(CancellationToken ct) => referenceDataService.GetAllergensAsync(true, ct);
    [HttpPut("allergens/{id:int}")] public async Task<IActionResult> UpdateAllergen(int id, AllergenRequest request, CancellationToken ct) { await referenceDataService.UpdateAllergenAsync(id, request, ct); return NoContent(); }
    [HttpPut("allergens/{id:int}/status")] public async Task<IActionResult> UpdateAllergenStatus(int id, UpdateCatalogStatusRequest request, CancellationToken ct) { await referenceDataService.UpdateAllergenStatusAsync(id, request.IsActive, ct); return NoContent(); }
    [HttpDelete("allergens/{id:int}")] public async Task<IActionResult> DeleteAllergen(int id, CancellationToken ct) { await referenceDataService.DeleteAllergenAsync(id, ct); return NoContent(); }

    [HttpGet("diet-types")] public Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypes(CancellationToken ct) => referenceDataService.GetDietTypesAsync(true, ct);
    [HttpPut("diet-types/{id:int}/status")] public async Task<IActionResult> UpdateDietTypeStatus(int id, UpdateCatalogStatusRequest request, CancellationToken ct) { await referenceDataService.UpdateDietTypeStatusAsync(id, request.IsActive, ct); return NoContent(); }

    [HttpGet("restaurants")] public Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurants([FromQuery] string? food, CancellationToken ct) => referenceDataService.GetRestaurantsAsync(food, true, ct);
    [HttpGet("restaurants/{id:int}")] public async Task<ActionResult<RestaurantResponse>> GetRestaurant(int id, CancellationToken ct) => (await referenceDataService.GetRestaurantAsync(id, true, ct)) is { } restaurant ? Ok(restaurant) : NotFound();
    [HttpPost("restaurants")] public async Task<ActionResult<RestaurantResponse>> CreateRestaurant(RestaurantRequest request, CancellationToken ct) => Created("/api/restaurants", await referenceDataService.CreateRestaurantAsync(request, ct));
    [HttpPut("restaurants/{id:int}")] public async Task<IActionResult> UpdateRestaurant(int id, RestaurantRequest request, CancellationToken ct) { await referenceDataService.UpdateRestaurantAsync(id, request, ct); return NoContent(); }
    [HttpPut("restaurants/{id:int}/status")] public async Task<IActionResult> UpdateRestaurantStatus(int id, UpdateCatalogStatusRequest request, CancellationToken ct) { await referenceDataService.UpdateRestaurantStatusAsync(id, request.IsActive, ct); return NoContent(); }
    [HttpDelete("restaurants/{id:int}")] public async Task<IActionResult> DeleteRestaurant(int id, CancellationToken ct) { await referenceDataService.DeleteRestaurantAsync(id, ct); return NoContent(); }
    [HttpGet("restaurants/{restaurantId:int}/foods")] public Task<IReadOnlyCollection<RestaurantFoodResponse>> GetRestaurantFoods(int restaurantId, [FromQuery] bool includeUnavailable, CancellationToken ct) => referenceDataService.GetRestaurantFoodsAsync(restaurantId, includeUnavailable, ct);
    [HttpPost("restaurants/{restaurantId:int}/foods")] public async Task<ActionResult<RestaurantFoodResponse>> CreateRestaurantFood(int restaurantId, RestaurantFoodRequest request, CancellationToken ct) { var food = await referenceDataService.CreateRestaurantFoodAsync(restaurantId, request, ct); return Created($"/api/admin/restaurants/{restaurantId}/foods/{food.RestaurantFoodId}", food); }
    [HttpPut("restaurants/{restaurantId:int}/foods/{foodId:int}")] public Task<RestaurantFoodResponse> UpdateRestaurantFood(int restaurantId, int foodId, RestaurantFoodRequest request, CancellationToken ct) => referenceDataService.UpdateRestaurantFoodAsync(restaurantId, foodId, request, ct);
    [HttpDelete("restaurants/{restaurantId:int}/foods/{foodId:int}")] public async Task<IActionResult> DeleteRestaurantFood(int restaurantId, int foodId, CancellationToken ct) { await referenceDataService.DeleteRestaurantFoodAsync(restaurantId, foodId, ct); return NoContent(); }

    [HttpGet("moderation")] public Task<IReadOnlyCollection<ModerationFlagResponse>> GetModeration(CancellationToken ct) => moderationService.GetAsync(ct);
    [HttpGet("moderation/{id:int}")] public Task<ModerationFlagResponse> GetModerationFlag(int id, CancellationToken ct) => moderationService.GetByIdAsync(id, ct);
    [HttpPost("moderation/{id:int}/approve")] public async Task<IActionResult> Approve(int id, CancellationToken ct) { await moderationService.ReviewAsync(id, User.GetRequiredUserId(), false, ct); return NoContent(); }
    [HttpPost("moderation/{id:int}/remove")] public async Task<IActionResult> Remove(int id, CancellationToken ct) { await moderationService.ReviewAsync(id, User.GetRequiredUserId(), true, ct); return NoContent(); }
}
