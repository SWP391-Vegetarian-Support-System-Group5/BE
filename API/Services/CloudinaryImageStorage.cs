using BLL.Common;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace API.Services;

public interface IImageStorage
{
    Task<string> UploadAsync(Stream image, string extension, string purpose, CancellationToken ct);
}

public sealed class CloudinaryImageStorage(Cloudinary cloudinary, CloudinarySettings settings, ILogger<CloudinaryImageStorage> logger) : IImageStorage
{
    public async Task<string> UploadAsync(Stream image, string extension, string purpose, CancellationToken ct)
    {
        if (!settings.IsConfigured)
            throw new ServiceException("Cloudinary chưa được cấu hình để tải ảnh.", 503);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            image.Position = 0;
            var result = await cloudinary.UploadAsync(new ImageUploadParams
            {
                File = new FileDescription($"image{extension}", image),
                Folder = $"{settings.ImageFolder.TrimEnd('/')}/{purpose}",
                PublicId = Guid.NewGuid().ToString("N"),
                Overwrite = false,
                UseFilename = false,
                UniqueFilename = false
            }, timeout.Token);
            if (result.Error is not null || result.SecureUrl is null)
            {
                logger.LogWarning("Cloudinary image upload failed: {Reason}", result.Error?.Message ?? "SecureUrl missing");
                throw new ServiceException("Không thể tải ảnh lên Cloudinary. Vui lòng thử lại.", 503);
            }
            return ImageUrlValidation.Normalize(result.SecureUrl.ToString())!;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ServiceException("Tải ảnh lên Cloudinary quá thời gian chờ. Vui lòng thử lại.", 503);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Cloudinary image connection failed.");
            throw new ServiceException("Không thể kết nối Cloudinary để tải ảnh. Vui lòng thử lại.", 503);
        }
        catch (ServiceException)
        {
            throw;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Cloudinary image upload failed unexpectedly.");
            throw new ServiceException("Không thể tải ảnh lên Cloudinary. Vui lòng thử lại.", 503);
        }
    }
}
