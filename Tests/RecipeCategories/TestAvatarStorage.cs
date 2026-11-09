using BLL.Common;
using BLL.Services;

sealed class TestAvatarStorage : IAvatarImageStorage
{
    public List<string> Deleted { get; } = [];
    public bool FailUpload { get; set; }
    public bool FailDelete { get; set; }
    public Func<Task>? BeforeReturn { get; set; }
    public string? LastId { get; private set; }
    public async Task<StoredAvatar> UploadAsync(int userId, byte[] bytes, string contentType, CancellationToken ct)
    {
        if (FailUpload) throw new ServiceException("Image provider unavailable.", 503);
        LastId = $"veggiemate/images/avatars/{userId}/{Guid.NewGuid():N}";
        if (BeforeReturn is not null) await BeforeReturn();
        return new($"https://res.cloudinary.com/test/image/upload/{LastId}.png", LastId);
    }
    public Task<bool> DeleteAsync(int userId, string publicId, CancellationToken ct)
    {
        Deleted.Add(publicId);
        return Task.FromResult(!FailDelete);
    }
}
