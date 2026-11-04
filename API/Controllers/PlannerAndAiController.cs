using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/meal-plans")]
public class MealPlansController(IMealPlanService mealPlanService) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult<MealPlanResponse>> Generate(GenerateMealPlanRequest request, CancellationToken ct)
    {
        var plan = await mealPlanService.GenerateAsync(User.GetRequiredUserId(), request, ct);
        return Created($"/api/meal-plans/{plan.MealPlanId}", plan);
    }

    [HttpPost("{id:int}/regenerate")]
    public async Task<ActionResult<MealPlanResponse>> Regenerate(int id, GenerateMealPlanRequest request, CancellationToken ct)
    {
        var plan = await mealPlanService.RegenerateAsync(id, User.GetRequiredUserId(), request, ct);
        return Ok(plan);
    }

    [HttpGet("{id:int}/meals/{mealId:int}/replacement-options")]
    public Task<IReadOnlyCollection<MealPlanRecipeOptionResponse>> GetReplacementOptions(int id, int mealId, [FromQuery] string? query, CancellationToken ct)
        => mealPlanService.GetReplacementOptionsAsync(id, mealId, User.GetRequiredUserId(), query, ct);

    [HttpPut("{id:int}/meals/{mealId:int}/replace")]
    public async Task<ActionResult<MealPlanResponse>> Replace(int id, int mealId, [FromBody] int? recipeId, CancellationToken ct)
    {
        var plan = await mealPlanService.ReplaceMealAsync(id, mealId, User.GetRequiredUserId(), recipeId, ct);
        return Ok(plan);
    }

    [HttpGet] public Task<IReadOnlyCollection<MealPlanResponse>> Get(CancellationToken ct) => mealPlanService.GetAsync(User.GetRequiredUserId(), ct);
    [HttpGet("{id:int}")] public Task<MealPlanResponse> GetById(int id, CancellationToken ct) => mealPlanService.GetByIdAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct);
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await mealPlanService.DeleteAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct); return NoContent(); }
}
