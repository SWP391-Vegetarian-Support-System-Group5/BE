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
    [Authorize]
    [HttpGet("diet-types")] public Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypes(CancellationToken ct) => referenceDataService.GetDietTypesAsync(false, ct);
    [HttpGet("categories")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetCategories(CancellationToken ct) => referenceDataService.GetCategoriesAsync(ct);
    [Authorize]
    [HttpGet("allergens")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergens(CancellationToken ct) => referenceDataService.GetAllergensAsync(false, ct);

    [Authorize]
    [HttpGet("restaurants")] public Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurants([FromQuery] string? food, CancellationToken ct) => referenceDataService.GetRestaurantsAsync(food, false, ct);
    [Authorize]
    [HttpGet("restaurants/nearby")] public Task<IReadOnlyCollection<RestaurantResponse>> Nearby([FromQuery] decimal latitude, [FromQuery] decimal longitude, [FromQuery] decimal radius, CancellationToken ct) => referenceDataService.GetNearbyRestaurantsAsync(latitude, longitude, radius, ct);
    [Authorize]
    [HttpGet("restaurants/{id:int}")] public async Task<ActionResult<RestaurantResponse>> GetRestaurant(int id, CancellationToken ct) => (await referenceDataService.GetRestaurantAsync(id, false, ct)) is { } restaurant ? Ok(restaurant) : NotFound();
    [Authorize]
    [HttpGet("restaurants/{restaurantId:int}/foods")] public Task<IReadOnlyCollection<RestaurantFoodResponse>> GetRestaurantFoods(int restaurantId, CancellationToken ct) => referenceDataService.GetRestaurantFoodsAsync(restaurantId, false, ct);
    [Authorize]
    [HttpPost("restaurants/{restaurantId:int}/reviews")] public async Task<ActionResult<RestaurantReviewResponse>> CreateReview(int restaurantId, RestaurantReviewRequest request, CancellationToken ct) => Created($"/api/restaurants/{restaurantId}/reviews", await referenceDataService.CreateRestaurantReviewAsync(restaurantId, User.GetRequiredUserId(), request, ct));
    [Authorize]
    [HttpGet("restaurants/{restaurantId:int}/reviews")] public Task<IReadOnlyCollection<RestaurantReviewResponse>> GetReviews(int restaurantId, CancellationToken ct) => referenceDataService.GetRestaurantReviewsAsync(restaurantId, ct);
}
