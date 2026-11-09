using API.Services;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api")]
public class ReferenceDataController(IReferenceDataAndRestaurantService referenceDataService, IngredientCatalogService ingredientCatalog, IRestaurantRouteService routeService) : ControllerBase
{
    [Authorize]
    [HttpGet("diet-types")] public Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypes(CancellationToken ct) => referenceDataService.GetDietTypesAsync(ct);
    [Authorize]
    [HttpGet("ingredient-categories")] public Task<List<IngredientCategoryResponse>> GetIngredientCategories(CancellationToken ct) => ingredientCatalog.GetCategoriesAsync(ct);
    [Authorize]
    [HttpGet("ingredients")] public Task<List<IngredientResponse>> GetIngredients([FromQuery] string? keyword, CancellationToken ct) => ingredientCatalog.GetIngredientsAsync(keyword, ct);
    [Authorize]
    [HttpGet("allergens")] public Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergens(CancellationToken ct) => referenceDataService.GetAllergensAsync(ct);

    [Authorize]
    [HttpGet("restaurants")] public Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurants([FromQuery] string? keyword, [FromQuery] string? food, CancellationToken ct) => referenceDataService.GetRestaurantsAsync(keyword ?? food, ct);
    [Authorize]
    [HttpGet("restaurants/nearby")] public Task<IReadOnlyCollection<RestaurantResponse>> Nearby([FromQuery] decimal latitude, [FromQuery] decimal longitude, [FromQuery] decimal radius, [FromQuery] int? dietTypeId, [FromQuery] string? keyword, [FromQuery] string? ingredient, CancellationToken ct) => referenceDataService.GetNearbyRestaurantsAsync(latitude, longitude, radius, dietTypeId, keyword ?? ingredient, ct);
    [Authorize]
    [HttpGet("restaurants/{id:int}")] public async Task<ActionResult<RestaurantResponse>> GetRestaurant(int id, CancellationToken ct) => (await referenceDataService.GetRestaurantAsync(id, ct)) is { } restaurant ? Ok(restaurant) : NotFound();
    [Authorize]
    [HttpGet("restaurants/{id:int}/route")]
    public async Task<ActionResult<RestaurantRouteResponse>> Route(int id, [FromQuery] decimal latitude, [FromQuery] decimal longitude, CancellationToken ct)
    {
        var restaurant = await referenceDataService.GetRestaurantAsync(id, ct);
        if (restaurant is null) return NotFound();
        if (restaurant.Latitude is null || restaurant.Longitude is null) return BadRequest(new { message = "Nhà hàng chưa có tọa độ để chỉ đường." });
        return Ok(await routeService.GetRouteAsync(latitude, longitude, restaurant.Latitude.Value, restaurant.Longitude.Value, ct));
    }
}
