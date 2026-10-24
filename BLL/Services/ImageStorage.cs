using BLL.Common;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public record ImageFile(byte[] Bytes, string MimeType);
public record StoredImage(string Url, string PublicId);
public interface IImageStorage
{
    Task<StoredImage> UploadAsync(ImageFile file, string folder, CancellationToken ct);
    Task<bool> DeleteAsync(string publicId, CancellationToken ct);
}

public static class ImageValidation
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public static string Validate(byte[] bytes, string? declaredType)
    {
        if (bytes.Length == 0) throw new ServiceException("Image is empty.");
        if (bytes.Length > MaximumBytes) throw new ServiceException("Image must not exceed 5 MB.", 413);
        var b = bytes.AsSpan();
        var type = b.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
            : b.StartsWith(new byte[] { 255, 216, 255 }) ? "image/jpeg"
            : b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp"
            : throw new ServiceException("Only JPEG, PNG and WebP images are accepted.", 415);
        if (!string.Equals(type, declaredType, StringComparison.OrdinalIgnoreCase))
            throw new ServiceException("Image content does not match its Content-Type.", 415);
        return type;
    }
}

public sealed class CloudinaryImageStorage(CloudinarySettings settings, ILogger<CloudinaryImageStorage> logger) : IImageStorage
{
    private Cloudinary CreateClient()
    {
        if (!settings.IsConfigured) throw new ServiceException("Cloudinary image storage is not configured.", 503);
        var cloud = new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret));
        cloud.Api.Timeout = 60000;
        cloud.Api.Client.Timeout = TimeSpan.FromSeconds(60);
        return cloud;
    }

    public async Task<StoredImage> UploadAsync(ImageFile file, string folder, CancellationToken ct)
    {
        ImageValidation.Validate(file.Bytes, file.MimeType);
        var cloud = CreateClient();
        var extension = file.MimeType switch { "image/png" => "png", "image/webp" => "webp", _ => "jpg" };
        using var stream = new MemoryStream(file.Bytes, false);
        try
        {
            var result = await cloud.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription($"image.{extension}", stream),
                Folder = folder, PublicId = Guid.NewGuid().ToString("N"),
                Overwrite = false, UniqueFilename = false, UseFilename = false
            }, ct);
            if (result.Error is not null || result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId))
                throw new ServiceException("Cloudinary could not store the image. The current image was kept.", 502);
            return new StoredImage(result.SecureUrl.ToString(), result.PublicId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServiceException("Image upload timed out. The current image was kept.", 503); }
        catch (HttpRequestException)
        { throw new ServiceException("Image storage is temporarily unavailable.", 503); }
    }

    public async Task<bool> DeleteAsync(string publicId, CancellationToken ct)
    {
        try
        {
            // Only server-generated IDs from this feature may be destroyed, never URLs sent by FE.
            if (!publicId.StartsWith("veggiemate/images/", StringComparison.Ordinal)) return false;
            var result = await CreateClient().DestroyAsync(new DeletionParams(publicId) { Invalidate = true }).WaitAsync(ct);
            return result.Error is null && result.Result is "ok" or "not found";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            logger.LogWarning("Image cleanup could not finish ({ExceptionType}); retry cleanup for asset {PublicId}.", ex.GetType().Name, publicId);
            return false;
        }
    }
}
