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
    [HttpPost("generate")] public async Task<ActionResult<MealPlanResponse>> Generate(GenerateMealPlanRequest request, CancellationToken ct) { var plan = await mealPlanService.GenerateAsync(User.GetRequiredUserId(), request, ct); return Created($"/api/meal-plans/{plan.MealPlanId}", plan); }
    [HttpGet] public Task<IReadOnlyCollection<MealPlanResponse>> Get(CancellationToken ct) => mealPlanService.GetAsync(User.GetRequiredUserId(), ct);
    [HttpGet("{id:int}")] public Task<MealPlanResponse> GetById(int id, CancellationToken ct) => mealPlanService.GetByIdAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct);
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await mealPlanService.DeleteAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct); return NoContent(); }
}

[ApiController]
[Route("api/chat")]
public class ChatController(IChatService chatService) : ControllerBase
{
    [HttpPost("sessions")] public async Task<ActionResult<ChatSessionResponse>> Create(CancellationToken ct) { var session = await chatService.CreateSessionAsync(User.Identity?.IsAuthenticated == true ? User.GetRequiredUserId() : null, ct); return Created($"/api/chat/sessions/{session.ChatSessionId}", session); }
    [Authorize]
    [HttpGet("sessions")] public Task<IReadOnlyCollection<ChatSessionResponse>> Get(CancellationToken ct) => chatService.GetSessionsAsync(User.GetRequiredUserId(), ct);
    [HttpGet("sessions/{id:int}")] public Task<ChatSessionResponse> GetById(int id, [FromHeader(Name = "X-Guest-Chat-Token")] string? guestAccessToken, CancellationToken ct) => chatService.GetSessionAsync(id, User.Identity?.IsAuthenticated == true ? User.GetRequiredUserId() : null, User.Identity?.IsAuthenticated == true ? User.GetRoleName() : null, guestAccessToken, ct);
    [HttpPost("sessions/{id:int}/messages")] public Task<ChatReplyResponse> Send(int id, ChatMessageRequest request, [FromHeader(Name = "X-Guest-Chat-Token")] string? guestAccessToken, CancellationToken ct) => chatService.SendAsync(id, User.Identity?.IsAuthenticated == true ? User.GetRequiredUserId() : null, User.Identity?.IsAuthenticated == true ? User.GetRoleName() : null, guestAccessToken, request, ct);
}

[ApiController]
[Authorize]
[Route("api/ai")]
public class AiFeaturesController(IIngredientRecognitionService ingredientRecognitionService) : ControllerBase
{
    [HttpPost("ingredient-recognition")]
    public async Task<ActionResult<IReadOnlyCollection<IngredientRecognitionResponse>>> Recognize(IFormFile image, CancellationToken ct)
    {
        if (image.Length == 0) return BadRequest(new { success = false, message = "Image is required.", errors = Array.Empty<string>() });
        await using var stream = image.OpenReadStream();
        return Ok(await ingredientRecognitionService.RecognizeAsync(stream, image.FileName, ct));
    }
}
