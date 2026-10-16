using System.ComponentModel.DataAnnotations;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly IReferenceDataAndRestaurantService _restaurantService;
    private readonly IRecipeService _recipeService;
    private readonly IPostService _postService;

    public SearchController(IReferenceDataAndRestaurantService restaurantService, IRecipeService recipeService, IPostService postService)
    {
        _restaurantService = restaurantService;
        _recipeService = recipeService;
        _postService = postService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(UnifiedSearchResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnifiedSearchResponse>> Search(
        [FromQuery] string keyword,
        [FromQuery, Range(1, 20)] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
            throw new ServiceException("Search keyword is required.");
        if (normalizedKeyword.Length > 100)
            throw new ServiceException("Search keyword cannot exceed 100 characters.");

        // These calls stay sequential because the services share the request-scoped EF DbContext.
        // Restaurant search includes food names; recipe search includes ingredient names.
        var restaurants = (await _restaurantService.GetRestaurantsAsync(normalizedKeyword, false, cancellationToken)).Take(limit).ToArray();
        var recipes = (await _recipeService.SearchAsync(normalizedKeyword, null, null, cancellationToken)).Take(limit).ToArray();
        var posts = await _postService.GetAsync(normalizedKeyword, null, "BLOG", null, null, false, null, limit, cancellationToken);

        return Ok(new UnifiedSearchResponse(normalizedKeyword, restaurants, recipes, posts));
    }
}
