using API.Infrastructure;
using API.Services;
using BLL.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/media/images")]
public class MediaImagesController(IImageStorage images) : ControllerBase
{
    public const long MaximumImageBytes = 8L * 1024 * 1024;
    private static readonly HashSet<string> Purposes = ["recipes", "restaurants", "categories"];

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(9L * 1024 * 1024)]
    public async Task<ActionResult<object>> Upload([FromForm] IFormFile image, [FromQuery] string purpose, CancellationToken ct)
    {
        if (!Purposes.Contains(purpose)) throw new ServiceException("Mục đích tải ảnh không hợp lệ.", 400);
        if (purpose != "recipes" && !User.IsInRole("ADMIN")) return Forbid();
        if (image is null || image.Length == 0 || image.Length > MaximumImageBytes)
            throw new ServiceException("Ảnh phải có dung lượng từ 1 byte đến 8 MB.", 400);

        await using var stream = new MemoryStream((int)image.Length);
        await image.CopyToAsync(stream, ct);
        var extension = DetectImageExtension(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        if (extension is null) throw new ServiceException("Chỉ hỗ trợ ảnh JPEG, PNG, WebP hoặc GIF hợp lệ.", 400);

        var url = await images.UploadAsync(stream, extension, purpose, ct);
        return Ok(new { imageUrl = url });
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ".jpg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        if (data.Length >= 6 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))) return ".gif";
        return null;
    }
}
