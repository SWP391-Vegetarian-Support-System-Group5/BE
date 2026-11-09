using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

static class BirthYearTests
{
    public static async Task RunAsync()
    {
        using var mysql = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql("Server=localhost;Database=test;User=test;Password=test;", new MySqlServerVersion(new Version(8, 0, 0))).Options);
        var sql = mysql.Users.Include(u => u.Profile).Where(u => u.Email == "test@example.invalid").ToQueryString();
        Check(sql.Contains("`BirthYear`") && !sql.Contains("BirthDate"), "login query uses BirthYear without connecting to MySQL");
        Check(mysql.Model.FindEntityType(typeof(UserProfile))!.FindProperty(nameof(UserProfile.BirthYear))!.GetColumnType() == "int", "year maps to MySQL int");

        using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { UserId = 1, Email = "test@example.invalid", PasswordHash = "unused",
            Profile = new UserProfile { FullName = "Test", BirthYear = 2000 } });
        await db.SaveChangesAsync();
        var service = new UserProfileService(new Repository<UserProfile>(db), new Repository<DietType>(db));
        var request = new UpdateUserProfileRequest("Test", null, 2003, 170, 60, "MODERATE", null, null, null);
        var response = await service.UpdateAsync(1, request);
        Check(response.BirthYear == 2003 && (await db.UserProfiles.SingleAsync()).BirthYear == 2003, "profile saves and returns year");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Check(json.RootElement.GetProperty("birthYear").GetInt32() == 2003 && !json.RootElement.TryGetProperty("birthDate", out _), "API returns numeric birthYear");
        foreach (var invalid in new[] { 0, VietnamTime.Now.Year + 1 })
        {
            try
            {
                await service.UpdateAsync(1, request with { BirthYear = invalid });
                throw new Exception("Invalid birth year was accepted.");
            }
            catch (ServiceException ex) when (ex.StatusCode == 400) { }
        }
        Check((await db.UserProfiles.SingleAsync()).BirthYear == 2003, "invalid years leave saved profile unchanged");
        Check((await service.UpdateAsync(1, request with { BirthYear = null })).BirthYear is null, "year remains optional");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
