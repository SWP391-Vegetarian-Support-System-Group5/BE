using System.ComponentModel.DataAnnotations;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/search")]
public class SearchController(IReferenceDataAndRestaurantService restaurants, IRecipeService recipes, IPostService posts) : ControllerBase
{
    [HttpGet]
    public async Task<UnifiedSearchResponse> Search([FromQuery] string keyword, [FromQuery, Range(1, 20)] int limit = 5, CancellationToken ct = default)
    {
        var term = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(term)) throw new ServiceException("Search keyword is required.");
        if (term.Length > 100) throw new ServiceException("Search keyword cannot exceed 100 characters.");

        // Sequential calls are intentional because these services share one scoped EF DbContext.
        var restaurantResults = (await restaurants.GetRestaurantsAsync(term, false, ct)).Take(limit).ToArray();
        var recipeResults = (await recipes.SearchAsync(term, null, null, ct)).Take(limit).ToArray();
        var postResults = await posts.GetAsync(term, null, null, null, null, false, null, limit, ct);
        return new(term, restaurantResults, recipeResults, postResults);
    }
}
