using BLL.Common;
using BLL.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/admin/uploads")]
public class AdminUploadsController : ControllerBase
{
    private const long MaxImageSize = 5 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> AllowedImageTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    private readonly IWebHostEnvironment _environment;

    public AdminUploadsController(IWebHostEnvironment environment) => _environment = environment;

    [HttpPost("images")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImageUploadResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ImageUploadResponse>> UploadImage([FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            throw new ServiceException("An image file is required.");
        if (file.Length > MaxImageSize)
            throw new ServiceException("Image size cannot exceed 5 MB.", StatusCodes.Status413PayloadTooLarge);
        if (!AllowedImageTypes.TryGetValue(file.ContentType, out var extension))
            throw new ServiceException("Only JPG, PNG, WEBP, and GIF images are supported.");

        // Never trust the uploaded path or filename; generate both server-side.
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var uploadDirectory = Path.Combine(webRoot, "uploads", "images");
        Directory.CreateDirectory(uploadDirectory);

        var physicalPath = Path.Combine(uploadDirectory, fileName);
        await using (var stream = System.IO.File.Create(physicalPath))
            await file.CopyToAsync(stream, cancellationToken);

        var relativeUrl = $"/uploads/images/{fileName}";
        return Created(relativeUrl, new ImageUploadResponse(relativeUrl, fileName, file.Length, file.ContentType));
    }
}
