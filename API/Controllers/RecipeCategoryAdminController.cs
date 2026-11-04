using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
public class RecipeCategoryAdminController(RecipeCategoryService categories) : ControllerBase
{
    [HttpPut("api/admin/recipes/{recipeId:int}/categories")]
    public async Task<IActionResult> UpdateCategories(int recipeId, UpdateRecipeCategoryMappingsRequest request, CancellationToken ct)
    {
        await categories.UpdateRecipeMappingsAsync(recipeId, request.CategoryIds, ct);
        return NoContent();
    }
}
