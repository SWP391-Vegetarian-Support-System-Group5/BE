using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/recipe-categories")]
public class RecipeCategoriesController(RecipeCategoryService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<RecipeCategoryResponse>> Get(string? type, bool includeInactive, CancellationToken ct) => service.ListAsync(type, includeInactive && User.IsInRole("ADMIN"), ct);
    [Authorize(Roles = "ADMIN")]
    [HttpPost]
    public async Task<ActionResult<RecipeCategoryResponse>> Create(RecipeCategoryRequest request, CancellationToken ct)
    {
        var result = await service.SaveAsync(null, request, ct);
        return Created($"/api/recipe-categories/{result.Id}", result);
    }
    [Authorize(Roles = "ADMIN")]
    [HttpPut("{id:int}")]
    public Task<RecipeCategoryResponse> Update(int id, RecipeCategoryRequest request, CancellationToken ct) => service.SaveAsync(id, request, ct);
}
