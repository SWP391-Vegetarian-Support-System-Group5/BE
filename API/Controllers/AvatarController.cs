using System.ComponentModel.DataAnnotations;
using API.Infrastructure;
using BLL.Common;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

public sealed class AvatarUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
}

[ApiController]
[Authorize]
[Route("api/users/me/avatar")]
public sealed class AvatarController(UserAvatarService avatars) : ControllerBase
{
    [HttpPut]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UserAvatarService.MaximumBytes + 1024 * 1024)]
    public async Task<AvatarResponse> Upload([FromForm] AvatarUploadForm form, CancellationToken ct)
    {
        if (form.File.Length > UserAvatarService.MaximumBytes) throw new ServiceException("Ảnh đại diện tối đa 5 MB.", 413);
        await using var stream = new MemoryStream((int)form.File.Length);
        await form.File.CopyToAsync(stream, ct);
        return await avatars.UploadAsync(User.GetRequiredUserId(), stream.ToArray(), form.File.ContentType, ct);
    }

    [HttpDelete]
    public Task<AvatarResponse> Delete(CancellationToken ct) => avatars.DeleteAsync(User.GetRequiredUserId(), ct);
}
