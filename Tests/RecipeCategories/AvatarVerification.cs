using API.Infrastructure;
using API.Services;
using BLL.Common;
using BLL.Services;
using CloudinaryDotNet;
using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

static class AvatarVerification
{
    public static async Task RunAsync(bool apply, bool cloudinary)
    {
        var builder = new ConfigurationBuilder();
        DotEnvConfiguration.AddFile(builder, "API/.env");
        DotEnvConfiguration.AddFile(builder, "API/.aiven.env");
        builder.AddEnvironmentVariables();
        var config = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        if (cloudinary)
        {
            var settings = config.GetSection("Cloudinary").Get<CloudinarySettings>() ?? new CloudinarySettings();
            if (!settings.IsConfigured) throw new InvalidOperationException("Cloudinary is not configured.");
            var storage = new CloudinaryAvatarStorage(new Cloudinary(new Account(settings.CloudName, settings.ApiKey, settings.ApiSecret)), settings, NullLogger<CloudinaryAvatarStorage>.Instance);
            var uploaded = await storage.UploadAsync(0, AvatarApiTests.Png, "image/png", timeout.Token);
            try
            {
                if (ImageUrlValidation.Normalize(uploaded.Url) is null) throw new InvalidOperationException("Invalid avatar URL.");
                Console.WriteLine("PASS real Cloudinary avatar upload returns a valid HTTPS image URL.");
            }
            finally
            {
                if (!await storage.DeleteAsync(0, uploaded.PublicId, timeout.Token)) throw new InvalidOperationException("Smoke-test avatar cleanup failed.");
                Console.WriteLine("PASS deleted only the new smoke-test avatar asset; no user profile was changed.");
            }
            return;
        }
        var connection = config.GetConnectionString("VegetarianSupportDatabase") ?? throw new InvalidOperationException("Database connection missing.");
        await using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 0))).Options);
        if (apply) await AvatarSchemaUpdate.ApplyAsync(db, timeout.Token);
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='UserProfiles'").ToListAsync(timeout.Token);
        if (!columns.Contains("AvatarUrl") || !columns.Contains("AvatarPublicId")) throw new InvalidOperationException("Avatar columns are missing.");
        // Materialize the new mapped columns without printing account data.
        await db.UserProfiles.AsNoTracking().Select(x => new { x.AvatarUrl, x.AvatarPublicId }).Take(1).ToListAsync(timeout.Token);
        Console.WriteLine("PASS MySQL UserProfiles has AvatarUrl and AvatarPublicId; mapped fields can be read. No profile records changed.");
    }
}
