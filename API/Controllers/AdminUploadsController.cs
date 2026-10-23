using BLL.Common;
using BLL.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController, Authorize(Roles = "ADMIN")]
[Route("api/admin/uploads")]
public class AdminUploadsController(IWebHostEnvironment environment) : ControllerBase
{
    private const long MaxSize = 5 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif" };

    [HttpPost("images"), Consumes("multipart/form-data")]
    public async Task<ActionResult<ImageUploadResponse>> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ServiceException("An image file is required.");
        if (file.Length > MaxSize) throw new ServiceException("Image size cannot exceed 5 MB.", 413);
        if (!Types.TryGetValue(file.ContentType, out var extension)) throw new ServiceException("Only JPG, PNG, WEBP, and GIF images are supported.");
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "images"); Directory.CreateDirectory(directory);
        await using var stream = System.IO.File.Create(Path.Combine(directory, fileName)); await file.CopyToAsync(stream, ct);
        var url = $"/uploads/images/{fileName}";
        return Created(url, new ImageUploadResponse(url, fileName, file.Length, file.ContentType));
    }
}
