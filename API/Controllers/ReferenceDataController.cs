using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api")]
public class ReferenceDataController(IReferenceDataAndRestaurantService referenceDataService, IngredientCatalogService ingredientCatalog) : ControllerBase
{
    [Authorize]
    [HttpGet("diet-types")] public Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypes(CancellationToken ct) => referenceDataService.GetDietTypesAsync(false, ct);
    [Authorize]
    [HttpGet("ingredient-categories")] public Task<List<IngredientCategoryResponse>> GetIngredientCategories(CancellationToken ct) => ingredientCatalog.GetCategoriesAsync(ct);
    [Authorize]
    [HttpGet("ingredients")] public Task<List<IngredientResponse>> GetIngredients([FromQuery] string? keyword, CancellationToken ct) => ingredientCatalog.GetIngredientsAsync(keyword, ct);
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
}
