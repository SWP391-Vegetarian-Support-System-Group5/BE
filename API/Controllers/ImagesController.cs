using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace API.Controllers;

public sealed class UploadImageForm
{
    [Required] public IFormFile File { get; set; } = null!;
}

[ApiController]
[Authorize]
[RequestSizeLimit(6 * 1024 * 1024)]
public class ImagesController(MediaImageService images) : ControllerBase
{
    [HttpPut("api/users/me/avatar")]
    [Consumes("multipart/form-data")]
    public async Task<ImageResponse> Avatar([FromForm] UploadImageForm form, CancellationToken ct) =>
        await images.UploadAvatarAsync(User.GetRequiredUserId(), await ReadAsync(form.File, ct), ct);

    [HttpDelete("api/users/me/avatar")]
    public Task<ImageResponse> DeleteAvatar(CancellationToken ct) => images.DeleteAvatarAsync(User.GetRequiredUserId(), ct);

    [HttpPut("api/posts/{postId:int}/image")]
    [Consumes("multipart/form-data")]
    public async Task<ImageResponse> Post(int postId, [FromForm] UploadImageForm form, CancellationToken ct) =>
        await images.UploadPostAsync(postId, User.GetRequiredUserId(), User.GetRoleName(), await ReadAsync(form.File, ct), ct);

    [HttpDelete("api/posts/{postId:int}/image")]
    public Task<ImageResponse> DeletePost(int postId, CancellationToken ct) => images.DeletePostAsync(postId, User.GetRequiredUserId(), User.GetRoleName(), ct);

    [HttpPut("api/meal-plans/{planId:int}/meals/{mealId:int}/image")]
    [Consumes("multipart/form-data")]
    public async Task<ImageResponse> Meal(int planId, int mealId, [FromForm] UploadImageForm form, CancellationToken ct) =>
        await images.UploadMealAsync(planId, mealId, User.GetRequiredUserId(), await ReadAsync(form.File, ct), ct);

    [HttpPost("api/meal-plans/{planId:int}/meals/{mealId:int}/image/generate")]
    [EnableRateLimiting("image-generation")]
    public Task<ImageResponse> Generate(int planId, int mealId, GenerateMealImageRequest request, CancellationToken ct) =>
        images.GenerateMealAsync(planId, mealId, User.GetRequiredUserId(), request, ct);

    [HttpDelete("api/meal-plans/{planId:int}/meals/{mealId:int}/image")]
    public Task<ImageResponse> DeleteMeal(int planId, int mealId, CancellationToken ct) => images.DeleteMealAsync(planId, mealId, User.GetRequiredUserId(), ct);

    private static async Task<ImageFile> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > ImageValidation.MaximumBytes) throw new ServiceException("Image must not exceed 5 MB.", 413);
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > ImageValidation.MaximumBytes) throw new ServiceException("Image must not exceed 5 MB.", 413);
            await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
        }
        var bytes = buffer.ToArray();
        return new(bytes, ImageValidation.Validate(bytes, file.ContentType));
    }
}
