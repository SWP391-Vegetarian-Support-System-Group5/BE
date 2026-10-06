using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api")]
public class ReferenceDataController(IReferenceDataAndRestaurantService referenceDataService) : ControllerBase
{
    [HttpGet("diet-types")] public Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypes(CancellationToken ct) => referenceDataService.GetDietTypesAsync(ct);
    [HttpGet("categories")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetCategories(CancellationToken ct) => referenceDataService.GetCategoriesAsync(ct);
    [HttpGet("allergens")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergens(CancellationToken ct) => referenceDataService.GetAllergensAsync(ct);
    [HttpGet("tags")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetTags(CancellationToken ct) => referenceDataService.GetTagsAsync(ct);
    [Authorize(Roles = "ADMIN")]
    [HttpPost("tags")] public async Task<ActionResult<CatalogItemResponse>> CreateTag(TagRequest request, CancellationToken ct) => Created("/api/tags", await referenceDataService.CreateTagAsync(request, ct));
    [Authorize(Roles = "ADMIN")]
    [HttpPut("tags/{id:int}")] public async Task<IActionResult> UpdateTag(int id, TagRequest request, CancellationToken ct) { await referenceDataService.UpdateTagAsync(id, request, ct); return NoContent(); }
    [Authorize(Roles = "ADMIN")]
    [HttpDelete("tags/{id:int}")] public async Task<IActionResult> DeleteTag(int id, CancellationToken ct) { await referenceDataService.DeleteTagAsync(id, ct); return NoContent(); }

    [HttpGet("restaurants")] public Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurants([FromQuery] string? food, CancellationToken ct) => referenceDataService.GetRestaurantsAsync(food, ct);
    [HttpGet("restaurants/nearby")] public Task<IReadOnlyCollection<RestaurantResponse>> Nearby([FromQuery] decimal latitude, [FromQuery] decimal longitude, [FromQuery] decimal radius, CancellationToken ct) => referenceDataService.GetNearbyRestaurantsAsync(latitude, longitude, radius, ct);
    [HttpGet("restaurants/{id:int}")] public async Task<ActionResult<RestaurantResponse>> GetRestaurant(int id, CancellationToken ct) => (await referenceDataService.GetRestaurantAsync(id, ct)) is { } restaurant ? Ok(restaurant) : NotFound();
    [Authorize]
    [HttpPost("restaurants/{restaurantId:int}/reviews")] public async Task<ActionResult<RestaurantReviewResponse>> CreateReview(int restaurantId, RestaurantReviewRequest request, CancellationToken ct) => Created($"/api/restaurants/{restaurantId}/reviews", await referenceDataService.CreateRestaurantReviewAsync(restaurantId, User.GetRequiredUserId(), request, ct));
    [HttpGet("restaurants/{restaurantId:int}/reviews")] public Task<IReadOnlyCollection<RestaurantReviewResponse>> GetReviews(int restaurantId, CancellationToken ct) => referenceDataService.GetRestaurantReviewsAsync(restaurantId, ct);
}
