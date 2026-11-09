using System.Net;
using System.Text;
using System.Text.Json;
using BLL.Common;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

static class CustomAllergenTests
{
    public static async Task RunAsync()
    {
        using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { UserId = 1, Email = "allergy@example.invalid", PasswordHash = "unused", Role = "USER" });
        db.Allergens.AddRange(new Allergen { Name = "Egg" }, new Allergen { Name = "Milk" });
        await db.SaveChangesAsync();

        var calls = 0;
        var response = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(new { allowed = true, name = "Mè", duplicateOf = "", reason = "" }) } } } } } });
        using var client = new HttpClient(new StubHandler(() => { calls++; return response; })) { BaseAddress = new Uri("https://example.invalid/") };
        var validator = new GeminiAllergenNameValidator(client, new GeminiChatbotSettings { ApiKey = "test" });
        var service = new UserAllergenService(new Repository<UserAllergen>(db), new Repository<Allergen>(db), new Repository<User>(db), db, validator);

        await RejectAsync(() => service.AddCustomAsync(1, "trứng"), 409);
        await RejectAsync(() => service.AddCustomAsync(1, "egg"), 409);
        Check(AllergenLexicon.FindExisting("egg", [new Allergen { Name = "Trứng" }]) == "Trứng",
            "Vietnamese catalog names recognize the English equivalent");
        Check(AllergenLexicon.ContainsTerm("Tôi muốn nấu đậu phộng", "dau phong") &&
              !AllergenLexicon.ContainsTerm("Tôi muốn nấu đậu phộng", "dau pho"),
            "Vietnamese accents and đ match aliases with word boundaries");
        await RejectAsync(() => service.AddCustomAsync(1, "thịt bò"), 422);
        Check(calls == 0 && await db.Allergens.CountAsync() == 2, "invalid and synonymous names do not call AI or write data");

        var result = await service.AddCustomAsync(1, "mè");
        Check(result.Allergens.Any(x => x.Name == "Mè") && await db.UserAllergens.CountAsync() == 1,
            "accepted custom allergen is saved and attached to user without admin review");
        Check(AllergenLexicon.FindExisting("sesame", await db.Allergens.ToListAsync()) is null,
            "no synonym is stored in the database");
        response = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(new { allowed = false, name = "", duplicateOf = "Mè", reason = "Tên tiếng Anh của mè." }) } } } } } });
        await RejectAsync(() => service.AddCustomAsync(1, "sesame"), 409);
        Check(calls == 2 && await db.Allergens.CountAsync() == 3, "AI rejects a synonymous resubmission without adding another row");

        var matchCalls = 0;
        var matchResponse = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(new { unsafeIngredientIds = new[] { 10 } }) } } } } } });
        using var matchClient = new HttpClient(new StubHandler(() => { matchCalls++; return matchResponse; })) { BaseAddress = new Uri("https://example.invalid/") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var matcher = new GeminiAllergenIngredientMatcher(matchClient, new GeminiChatbotSettings { ApiKey = "test" }, cache);
        var sesame = new Ingredient { IngredientId = 10, Name = "Sesame", Category = new IngredientCategory { DietaryGroup = "PLANT" } };
        var allergy = await db.Allergens.SingleAsync(x => x.Name == "Mè");
        Check((await matcher.FindUnsafeAsync([allergy], [sesame])).Contains(10), "AI matches a custom allergy with an English ingredient name");
        Check((await matcher.FindUnsafeAsync([allergy], [sesame])).Contains(10) && matchCalls == 1,
            "AI decisions are cached briefly in memory, without a database column");

        var rejected = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = JsonSerializer.Serialize(new { allowed = false, name = "", aliases = Array.Empty<string>(), duplicateOf = "", reason = "Đây không phải nguyên liệu gây dị ứng." }) } } } } } });
        using var rejectingClient = new HttpClient(new StubHandler(() => rejected)) { BaseAddress = new Uri("https://example.invalid/") };
        var rejectingService = new UserAllergenService(new Repository<UserAllergen>(db), new Repository<Allergen>(db), new Repository<User>(db), db,
            new GeminiAllergenNameValidator(rejectingClient, new GeminiChatbotSettings { ApiKey = "test" }));
        try { await rejectingService.AddCustomAsync(1, "bầu trời"); throw new Exception("Expected off-topic rejection"); }
        catch (ServiceException error) when (error.StatusCode == 422)
        {
            Check(error.Message.Contains("không phải nguyên liệu") && await db.Allergens.CountAsync() == 3,
                "off-topic input shows the AI reason and does not write data");
        }
    }

    private static async Task RejectAsync(Func<Task> action, int code)
    {
        try { await action(); throw new Exception("Expected validation failure"); }
        catch (ServiceException error) when (error.StatusCode == code) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS custom allergen " + message);
    }

    private sealed class StubHandler(Func<string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(), Encoding.UTF8, "application/json") });
    }
}
