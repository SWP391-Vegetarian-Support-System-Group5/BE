using BLL.Common;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public record StoredAvatar(string Url, string PublicId);
public record AvatarResponse(string? ImageUrl, bool IsGenerated, bool PreviousImageDeleted);
public interface IAvatarImageStorage
{
    Task<StoredAvatar> UploadAsync(int userId, byte[] bytes, string contentType, CancellationToken ct);
    Task<bool> DeleteAsync(int userId, string publicId, CancellationToken ct);
}

public sealed class UserAvatarService(IRepository<UserProfile> profiles, IAvatarImageStorage storage, ILogger<UserAvatarService> logger)
{
    public const int MaximumBytes = 5 * 1024 * 1024;

    public async Task<AvatarResponse> UploadAsync(int userId, byte[] bytes, string contentType, CancellationToken ct)
    {
        Validate(bytes, contentType);
        var profile = await RequireProfileAsync(userId, ct);
        var oldUrl = profile.AvatarUrl;
        var oldId = profile.AvatarPublicId;
        var uploaded = await storage.UploadAsync(userId, bytes, contentType, ct);
        try
        {
            profile.AvatarUrl = ImageUrlValidation.Normalize(uploaded.Url)
                ?? throw new ServiceException("Dịch vụ ảnh chưa trả URL hợp lệ.", 503);
            profile.AvatarPublicId = uploaded.PublicId;
            await profiles.SaveChangesAsync(ct);
        }
        catch (Exception error)
        {
            profile.AvatarUrl = oldUrl; profile.AvatarPublicId = oldId;
            await CleanupAsync(userId, uploaded.PublicId);
            if (error is DbUpdateConcurrencyException)
                throw new ServiceException("Ảnh đại diện vừa thay đổi. Hãy tải lại trước khi thử lại.", 409);
            throw;
        }
        return new(profile.AvatarUrl, false, oldId is null && oldUrl is not null ? false : await CleanupAsync(userId, oldId));
    }

    public async Task<AvatarResponse> DeleteAsync(int userId, CancellationToken ct)
    {
        var profile = await RequireProfileAsync(userId, ct);
        var oldId = profile.AvatarPublicId;
        var oldUrl = profile.AvatarUrl;
        profile.AvatarUrl = null; profile.AvatarPublicId = null;
        try { await profiles.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ServiceException("Ảnh đại diện vừa thay đổi. Hãy tải lại trước khi thử lại.", 409); }
        return new(null, false, oldId is null && oldUrl is not null ? false : await CleanupAsync(userId, oldId));
    }

    private async Task<UserProfile> RequireProfileAsync(int userId, CancellationToken ct) =>
        await profiles.Query().Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == userId && x.User.IsActive, ct)
        ?? throw new ServiceException("Không tìm thấy hồ sơ đang hoạt động.", 404);

    private async Task<bool> CleanupAsync(int userId, string? publicId)
    {
        if (string.IsNullOrWhiteSpace(publicId)) return true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var deleted = await storage.DeleteAsync(userId, publicId, timeout.Token);
            if (!deleted) logger.LogWarning("Avatar cleanup pending for user {UserId}, asset {PublicId}.", userId, publicId);
            return deleted;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            logger.LogWarning("Avatar cleanup pending for user {UserId} ({ErrorType}).", userId, error.GetType().Name);
            return false;
        }
    }

    public static void Validate(byte[] bytes, string contentType)
    {
        if (bytes.Length == 0) throw new ServiceException("Tệp ảnh đang trống.", 415);
        if (bytes.Length > MaximumBytes) throw new ServiceException("Ảnh đại diện tối đa 5 MB.", 413);
        var data = bytes.AsSpan();
        var detected = data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? "image/png"
            : data.StartsWith(new byte[] { 255, 216, 255 }) ? "image/jpeg"
            : data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp"
            : throw new ServiceException("Chỉ nhận ảnh JPEG, PNG hoặc WebP.", 415);
        if (!string.Equals(detected, contentType, StringComparison.OrdinalIgnoreCase))
            throw new ServiceException("Nội dung ảnh không khớp định dạng đã khai báo.", 415);
    }
}
