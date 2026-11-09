using BLL.Common;
using BLL.Services;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace API.Services;

public sealed class CloudinaryAvatarStorage(Cloudinary cloudinary, CloudinarySettings settings, ILogger<CloudinaryAvatarStorage> logger) : IAvatarImageStorage
{
    private string Folder(int userId) => $"{settings.ImageFolder.TrimEnd('/')}/avatars/{userId}";

    public async Task<StoredAvatar> UploadAsync(int userId, byte[] bytes, string contentType, CancellationToken ct)
    {
        if (!settings.IsConfigured) throw new ServiceException("Cloudinary chưa được cấu hình để lưu ảnh đại diện.", 503);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await using var stream = new MemoryStream(bytes, writable: false);
        var extension = contentType.ToLowerInvariant() switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        try
        {
            var result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription("avatar" + extension, stream),
                Folder = Folder(userId), PublicId = Guid.NewGuid().ToString("N"),
                Overwrite = false, UseFilename = false, UniqueFilename = false,
            }, timeout.Token);
            if (result.Error is not null || result.SecureUrl is null || string.IsNullOrWhiteSpace(result.PublicId))
                throw new ServiceException("Chưa tải được ảnh đại diện lên Cloudinary.", 503);
            return new(ImageUrlValidation.Normalize(result.SecureUrl.ToString())!, result.PublicId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServiceException("Tải ảnh đại diện quá thời gian chờ. Ảnh cũ được giữ lại.", 503); }
        catch (HttpRequestException)
        { throw new ServiceException("Chưa kết nối được dịch vụ ảnh. Ảnh cũ được giữ lại.", 503); }
        catch (ServiceException) { throw; }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Avatar upload failed ({ErrorType}).", error.GetType().Name);
            throw new ServiceException("Dịch vụ ảnh đại diện tạm thời chưa sẵn sàng.", 503);
        }
    }

    public async Task<bool> DeleteAsync(int userId, string publicId, CancellationToken ct)
    {
        if (!publicId.StartsWith(Folder(userId) + "/", StringComparison.Ordinal)) return false;
        try
        {
            var result = await cloudinary.DestroyAsync(new DeletionParams(publicId) { Invalidate = true }).WaitAsync(ct);
            return result.Error is null && result.Result is "ok" or "not found";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { logger.LogWarning("Avatar deletion pending ({ErrorType}).", error.GetType().Name); return false; }
    }
}
