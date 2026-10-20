using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
public class RecipeBmiController(RecipeBmiService service, RecipeCategoryService categories) : ControllerBase
{
    [HttpGet("api/bmi-types")]
    public Task<List<BmiTypeResponse>> List(CancellationToken ct) => service.ListAsync(ct);

    [Authorize(Roles = "ADMIN")]
    [HttpGet("api/admin/recipes/bmi-types")]
    public Task<List<AdminRecipeBmiResponse>> Recipes(CancellationToken ct) => service.RecipesAsync(ct);

    [Authorize(Roles = "ADMIN")]
    [HttpPut("api/admin/recipes/{recipeId:int}/bmi-types")]
    public async Task<IActionResult> Update(int recipeId, UpdateRecipeBmiTypesRequest request, CancellationToken ct)
    {
        await service.UpdateAsync(recipeId, request.BmiTypeIds, User.GetRequiredUserId(), ct);
        return NoContent();
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPut("api/admin/recipes/{recipeId:int}/categories")]
    public async Task<IActionResult> UpdateCategories(int recipeId, UpdateRecipeCategoryMappingsRequest request, CancellationToken ct)
    {
        await categories.UpdateRecipeMappingsAsync(recipeId, request.CategoryIds, ct);
        return NoContent();
    }
}
