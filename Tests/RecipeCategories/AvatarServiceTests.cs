using BLL.Services;
using BLL.Common;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

static class AvatarServiceTests
{
    private const string OldId = "veggiemate/images/avatars/1/old";
    private const string OldUrl = "https://res.cloudinary.com/test/image/upload/old.png";
    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        (VegetarianDbContext Db, DbContextOptions<VegetarianDbContext> Options) Database()
        {
            var options = new DbContextOptionsBuilder<VegetarianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var db = new VegetarianDbContext(options);
            db.Users.Add(new User { UserId = 1, Email = "avatar@example.invalid", PasswordHash = "unused", IsActive = true, Role = "USER" });
            db.UserProfiles.Add(new UserProfile { UserId = 1, FullName = "Avatar", AvatarUrl = OldUrl, AvatarPublicId = OldId });
            db.SaveChanges(); return (db, options);
        }
        UserAvatarService Service(VegetarianDbContext db, TestAvatarStorage storage) => new(new Repository<UserProfile>(db), storage, NullLogger<UserAvatarService>.Instance);
        async Task Reject(Func<Task> action, int status) { try { await action(); throw new Exception("Expected failure"); } catch (ServiceException error) when (error.StatusCode == status) { checks++; } }
        var first = Database();
        await using (var db = first.Db)
        {
            var storage = new TestAvatarStorage { FailUpload = true };
            await Reject(() => Service(db, storage).UploadAsync(1, AvatarApiTests.Png, "image/png", default), 503);
            Check(db.UserProfiles.Single().AvatarUrl == OldUrl && storage.Deleted.Count == 0, "Provider failure keeps the previous image");
        }
        var second = Database();
        await using (var db = second.Db)
        {
            var storage = new TestAvatarStorage { FailDelete = true };
            var result = await Service(db, storage).UploadAsync(1, AvatarApiTests.Png, "image/png", default);
            db.ChangeTracker.Clear();
            Check(!result.PreviousImageDeleted && db.UserProfiles.Single().AvatarUrl == result.ImageUrl, "Cleanup failure does not roll back a saved avatar");
        }
        var concurrent = Database();
        await using (var db = concurrent.Db)
        {
            const string latestUrl = "https://res.cloudinary.com/test/image/upload/newer.png";
            var storage = new TestAvatarStorage
            {
                BeforeReturn = async () =>
                {
                    await using var other = new VegetarianDbContext(concurrent.Options);
                    var profile = await other.UserProfiles.SingleAsync();
                    profile.AvatarUrl = latestUrl; profile.AvatarPublicId = "veggiemate/images/avatars/1/newer";
                    await other.SaveChangesAsync();
                }
            };
            await Reject(() => Service(db, storage).UploadAsync(1, AvatarApiTests.Png, "image/png", default), 409);
            db.ChangeTracker.Clear();
            Check(db.UserProfiles.Single().AvatarUrl == latestUrl, "Concurrent avatar upload cannot overwrite a newer image");
            Check(storage.Deleted.Count == 1 && storage.Deleted[0] == storage.LastId && !storage.Deleted.Contains(OldId), "Losing upload deletes only its new cloud asset");
        }
        var legacy = Database();
        await using (var db = legacy.Db)
        {
            db.UserProfiles.Single().AvatarPublicId = null;
            await db.SaveChangesAsync();
            var storage = new TestAvatarStorage();
            var result = await Service(db, storage).UploadAsync(1, AvatarApiTests.Png, "image/png", default);
            Check(!result.PreviousImageDeleted && storage.Deleted.Count == 0, "A legacy URL without a managed public ID is never falsely reported as deleted");
        }
        Console.WriteLine($"AVATAR SERVICE PASS: {checks} failure and concurrency checks; no live calls.");
    }
}
