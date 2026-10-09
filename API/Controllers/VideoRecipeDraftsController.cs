using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/video-recipe-drafts")]
public class VideoRecipeDraftsController(IVideoRecipeDraftService videoRecipeDraftService, VideoRecipeSettings settings) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> VideoTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4", [".mov"] = "video/quicktime", [".webm"] = "video/webm", [".avi"] = "video/avi"
    };

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<VideoRecipeDraftResponse>> Create(IFormFile video, CancellationToken ct)
    {
        if (video.Length == 0) return BadRequest(new { success = false, message = "Video is required.", errors = Array.Empty<string>() });
        if (video.Length > settings.MaximumFileSizeBytes) return BadRequest(new { success = false, message = "Video must be 100 MB or smaller.", errors = Array.Empty<string>() });
        if (!VideoTypes.TryGetValue(Path.GetExtension(video.FileName), out var contentType)) return BadRequest(new { success = false, message = "Use an MP4, MOV, WEBM, or AVI video.", errors = Array.Empty<string>() });
        await using var stream = video.OpenReadStream();
        var draft = await videoRecipeDraftService.CreateAsync(User.GetRequiredUserId(), stream, video.FileName, contentType, ct);
        return AcceptedAtAction(nameof(GetById), new { id = draft.VideoRecipeDraftId }, draft);
    }


    [HttpPost("manual")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<VideoRecipeDraftResponse>> CreateManual(
    IFormFile video,
    [FromForm] string requestJson,
    CancellationToken ct)
    {
        if (video == null || video.Length == 0)
            return BadRequest(new { success = false, message = "Video is required." });

        if (video.Length > settings.MaximumFileSizeBytes)
            return BadRequest(new { success = false, message = "Video must be 100 MB or smaller." });

        if (!VideoTypes.TryGetValue(Path.GetExtension(video.FileName), out var contentType))
            return BadRequest(new { success = false, message = "Use an MP4, MOV, WEBM, or AVI video." });

        CreateManualVideoRecipeDraftRequest request;
        try
        {
            request = System.Text.Json.JsonSerializer.Deserialize<CreateManualVideoRecipeDraftRequest>(
                requestJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new Exception();
        }
        catch
        {
            return BadRequest(new { success = false, message = "The JSON format in requestJson is invalid." });
        }

        if (!TryValidateModel(request))
            return ValidationProblem(ModelState);

        await using var stream = video.OpenReadStream();
        var draft = await videoRecipeDraftService.CreateManualAsync(
            User.GetRequiredUserId(),
            stream,
            video.FileName,
            contentType,
            request,
            ct);

        return CreatedAtAction(nameof(GetById), new { id = draft.VideoRecipeDraftId }, draft);
    }

    [HttpGet("{id:int}")]
    public Task<VideoRecipeDraftResponse> GetById(int id, CancellationToken ct) => videoRecipeDraftService.GetAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct);

    [HttpPut("{id:int}")]
    public Task<VideoRecipeDraftResponse> Update(int id, UpdateVideoRecipeDraftRequest request, CancellationToken ct) => videoRecipeDraftService.UpdateAsync(id, User.GetRequiredUserId(), User.GetRoleName(), request, ct);

    [HttpPost("{id:int}/retry")]
    public Task<VideoRecipeDraftResponse> Retry(int id, CancellationToken ct) => videoRecipeDraftService.RetryAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct);

    [HttpPost("{id:int}/publish")]
    public Task<RecipeResponse> Publish(int id, PublishVideoRecipeDraftRequest request, CancellationToken ct) => videoRecipeDraftService.PublishAsync(id, User.GetRequiredUserId(), User.GetRoleName(), request, ct);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) { await videoRecipeDraftService.DeleteAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct); return NoContent(); }
}
