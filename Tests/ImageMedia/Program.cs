using System.Net;
using System.Text;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7xkAAAAASUVORK5CYII=");
var file = new ImageFile(png, "image/png");
var pass = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); pass++; }
async Task Error(Func<Task> action, int status)
{
    try { await action(); throw new Exception("Expected error " + status); }
    catch (ServiceException ex) when (ex.StatusCode == status) { }
}

if (args.Contains("--cloudinary") || args.Contains("--live-generation"))
{
    var config = new Dictionary<string, string>();
    foreach (var path in new[] { "API/.env", "API/.aiven.env" })
        if (File.Exists(path)) foreach (var line in File.ReadLines(path))
        {
            var i = line.IndexOf('='); if (i < 1 || line.TrimStart().StartsWith('#')) continue;
            config[line[..i].Trim()] = line[(i + 1)..].Trim().Trim('"', '\'');
        }
    string Value(string key) => Environment.GetEnvironmentVariable(key) ?? config.GetValueOrDefault(key, "");
    if (args.Contains("--cloudinary"))
    {
        var cloud = new CloudinaryImageStorage(new CloudinarySettings
        { CloudName = Value("Cloudinary__CloudName"), ApiKey = Value("Cloudinary__ApiKey"), ApiSecret = Value("Cloudinary__ApiSecret") }, NullLogger<CloudinaryImageStorage>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var stored = await cloud.UploadAsync(file, "veggiemate/images/smoke-tests", timeout.Token);
        try { Check(stored.Url.StartsWith("https://"), "real Cloudinary upload returns HTTPS URL"); }
        finally { Check(await cloud.DeleteAsync(stored.PublicId, timeout.Token), "real Cloudinary deletes only the new smoke-test image"); }
    }
    if (args.Contains("--live-generation"))
    {
        using var client = new HttpClient { BaseAddress = new("https://generativelanguage.googleapis.com/v1beta/"), Timeout = TimeSpan.FromSeconds(120) };
        var settings = new ImageGenerationSettings { ApiKey = Value("GeminiImages__ApiKey") is { Length: > 0 } k ? k : Value("Gemini__ApiKey"), Model = Value("GeminiImages__Model") is { Length: > 0 } m ? m : "gemini-2.5-flash-image" };
        var generated = await new GeminiMealImageGenerator(client, settings).GenerateAsync("Vegetarian tofu bowl", ["tofu", "rice", "carrot"], null, CancellationToken.None);
        Check(generated.Bytes.Length > 0, "real Gemini returns validated image bytes");
    }
    return;
}

var optionsByDb = new Dictionary<VegetarianDbContext, DbContextOptions<VegetarianDbContext>>();
VegetarianDbContext Database()
{
    var options = new DbContextOptionsBuilder<VegetarianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    var db = new VegetarianDbContext(options);
    optionsByDb[db] = options;
    var user = new User { UserId = 1, Email = "owner@example.invalid", PasswordHash = "test", IsActive = true };
    db.Users.Add(user);
    db.UserProfiles.Add(new UserProfile { UserId = 1, User = user, FullName = "Owner", AvatarUrl = "https://old/avatar", AvatarPublicId = "veggiemate/images/avatars/1/old" });
    var post = new Post { PostId = 10, UserId = 1, User = user, Title = "Tofu", Content = "Recipe", PostType = "RECIPE", Status = "PUBLISHED", ImageUrl = "https://shared/recipe", ImagePublicId = "veggiemate/images/posts/10/shared" };
    var recipe = new Recipe { PostId = 10, Post = post, RecipeName = "Tofu bowl" };
    db.Recipes.Add(recipe);
    var plan = new MealPlan { MealPlanId = 20, UserId = 1, StartDate = DateTime.Today };
    db.MealPlans.Add(plan);
    db.MealPlanMeals.Add(new MealPlanMeal { MealPlanMealId = 30, MealPlan = plan, MealPlanId = 20, Recipe = recipe, RecipeId = 10, MealType = "LUNCH", DayNumber = 1, ImageUrl = "https://old/meal", ImagePublicId = "veggiemate/images/meals/1/30/old" });
    db.SaveChanges(); return db;
}
MediaImageService Service(VegetarianDbContext db, FakeStorage storage, FakeGenerator generator, Exception? saveError = null) => new(
    new FailingRepository<UserProfile>(db, saveError), new Repository<Post>(db), new Repository<MealPlanMeal>(db), storage, generator, NullLogger<MediaImageService>.Instance);

Check(ImageValidation.Validate(png, "image/png") == "image/png", "PNG content accepted");
await Error(() => Task.Run(() => ImageValidation.Validate(png, "image/jpeg")), 415); Check(true, "MIME spoof rejected");
await Error(() => Task.Run(() => ImageValidation.Validate("<svg/>"u8.ToArray(), "image/svg+xml")), 415); Check(true, "SVG rejected");
await Error(() => Task.Run(() => ImageValidation.Validate(new byte[ImageValidation.MaximumBytes + 1], "image/png")), 413); Check(true, "oversized image rejected");

using (var db = Database())
{
    var storage = new FakeStorage(); var generator = new FakeGenerator(file);
    storage.OnDelete = id => Check(db.UserProfiles.Single().AvatarUrl == storage.NewUrl, "DB uses new avatar before old cloud image deletion");
    var response = await Service(db, storage, generator).UploadAvatarAsync(1, file, default);
    db.ChangeTracker.Clear();
    Check(db.UserProfiles.Single().AvatarUrl == response.ImageUrl && response.PreviousImageDeleted, "avatar persists and survives reload");
    Check(storage.Deleted.Single().EndsWith("/old"), "only previous avatar deleted");
}
using (var db = Database())
{
    var storage = new FakeStorage { FailUpload = true };
    await Error(() => Service(db, storage, new(file)).UploadAvatarAsync(1, file, default), 503);
    Check(db.UserProfiles.Single().AvatarUrl == "https://old/avatar" && storage.Deleted.Count == 0, "failed upload preserves old image");
}
using (var db = Database())
{
    var storage = new FakeStorage();
    await Error(() => Service(db, storage, new(file), new DbUpdateConcurrencyException()).UploadAvatarAsync(1, file, default), 409);
    Check(storage.Deleted.Single() == storage.NewId && db.UserProfiles.Single().AvatarUrl == "https://old/avatar", "DB conflict cleans new asset, preserves old, returns 409");
}
using (var db = Database())
{
    var storage = new FakeStorage { FailDelete = true };
    var response = await Service(db, storage, new(file)).UploadAvatarAsync(1, file, default);
    Check(response.ImageUrl == storage.NewUrl && !response.PreviousImageDeleted, "cleanup failure reports pending cleanup without losing new image");
}
using (var db = Database())
{
    var storage = new FakeStorage();
    storage.BeforeReturn = async () =>
    {
        using var concurrent = new VegetarianDbContext(optionsByDb[db]);
        var profile = concurrent.UserProfiles.Single();
        profile.AvatarUrl = "https://concurrent/avatar"; profile.AvatarPublicId = "veggiemate/images/avatars/1/concurrent";
        await concurrent.SaveChangesAsync();
    };
    await Error(() => Service(db, storage, new(file)).UploadAvatarAsync(1, file, default), 409);
    db.ChangeTracker.Clear();
    Check(db.UserProfiles.Single().AvatarUrl == "https://concurrent/avatar" && storage.Deleted.Single() == storage.NewId, "real EF concurrency preserves a newer avatar and cleans only the losing upload");
}
using (var db = Database())
{
    var storage = new FakeStorage(); var generator = new FakeGenerator(file); var service = Service(db, storage, generator);
    await Error(() => service.UploadPostAsync(10, 2, "USER", file, default), 403);
    await Error(() => service.GenerateMealAsync(20, 30, 2, new(), default), 404);
    await Error(() => service.GenerateMealAsync(999, 30, 1, new(), default), 404);
    Check(storage.Uploads == 0 && generator.Calls == 0, "unauthorized post and meal requests never reach providers");
    var generated = await service.GenerateMealAsync(20, 30, 1, new(), default);
    db.ChangeTracker.Clear();
    Check(generated.IsGenerated && db.MealPlanMeals.Single().ImageIsGenerated, "generated meal image persists");
    Check(db.Posts.Single().ImageUrl == "https://shared/recipe", "personal meal image leaves shared recipe image unchanged");
    var uploaded = await service.UploadMealAsync(20, 30, 1, file, default);
    Check(!uploaded.IsGenerated && !db.MealPlanMeals.Single().ImageIsGenerated, "manual replacement clears AI label");
    await service.DeleteMealAsync(20, 30, 1, default);
    Check(db.MealPlanMeals.Single().ImageUrl == null, "delete removes meal-specific image");
    var post = await service.UploadPostAsync(10, 1, "USER", file, default);
    db.ChangeTracker.Clear(); Check(db.Posts.Single().ImageUrl == post.ImageUrl, "post cover persists");
}
using (var db = Database())
{
    var meal = db.MealPlanMeals.Single(); meal.ImageUrl = null; meal.ImagePublicId = null; db.SaveChanges();
    var storage = new FakeStorage();
    storage.BeforeReturn = async () =>
    {
        using var concurrent = new VegetarianDbContext(optionsByDb[db]);
        var user = concurrent.Users.Single();
        var post = new Post { PostId = 11, UserId = 1, User = user, Title = "New dish", Content = "New recipe", PostType = "RECIPE", Status = "PUBLISHED" };
        concurrent.Recipes.Add(new Recipe { PostId = 11, Post = post, RecipeName = "New dish" });
        concurrent.MealPlanMeals.Single().RecipeId = 11;
        await concurrent.SaveChangesAsync();
    };
    await Error(() => Service(db, storage, new(file)).GenerateMealAsync(20, 30, 1, new(), default), 409);
    db.ChangeTracker.Clear();
    Check(db.MealPlanMeals.Single().RecipeId == 11 && db.MealPlanMeals.Single().ImageUrl is null
        && storage.Deleted.Single() == storage.NewId, "concurrent recipe replacement cannot receive the previous dish's generated image");
}

using (var client = new HttpClient(new StubHttp(HttpStatusCode.TooManyRequests, "{}")) { BaseAddress = new("https://test.invalid/") })
{
    await Error(() => new GeminiMealImageGenerator(client, new() { ApiKey = "test" }).GenerateAsync("Tofu", [], null, default), 429);
    Check(true, "AI quota error mapped to 429");
}
using (var client = new HttpClient(new StubHttp(HttpStatusCode.OK, "{\"candidates\":[]}")) { BaseAddress = new("https://test.invalid/") })
{
    await Error(() => new GeminiMealImageGenerator(client, new() { ApiKey = "test" }).GenerateAsync("Tofu", [], null, default), 502);
    Check(true, "AI text/no image is not treated as generated image");
}
var body = System.Text.Json.JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { inlineData = new { mimeType = "image/png", data = Convert.ToBase64String(png) } } } } } } });
using (var client = new HttpClient(new StubHttp(HttpStatusCode.OK, body)) { BaseAddress = new("https://test.invalid/") })
{
    var generated = await new GeminiMealImageGenerator(client, new() { ApiKey = "test" }).GenerateAsync("Tofu", [], null, default);
    Check(generated.Bytes.SequenceEqual(png), "Gemini image response decoded and validated");
}
Console.WriteLine($"{pass} checks passed; no live database or external calls.");

sealed class FakeGenerator(ImageFile image) : IMealImageGenerator
{
    public int Calls;
    public Task<ImageFile> GenerateAsync(string name, IReadOnlyCollection<string> ingredients, string? style, CancellationToken ct) { Calls++; return Task.FromResult(image); }
}
sealed class FakeStorage : IImageStorage
{
    public string NewUrl = "https://new/image";
    public string NewId = "veggiemate/images/test/new";
    public int Uploads;
    public bool FailUpload, FailDelete;
    public List<string> Deleted = [];
    public Action<string>? OnDelete;
    public Func<Task>? BeforeReturn;
    public async Task<StoredImage> UploadAsync(ImageFile file, string folder, CancellationToken ct)
    { Uploads++; if (FailUpload) throw new ServiceException("Unavailable", 503); if (BeforeReturn is not null) await BeforeReturn(); return new StoredImage(NewUrl, NewId); }
    public Task<bool> DeleteAsync(string id, CancellationToken ct) { OnDelete?.Invoke(id); Deleted.Add(id); return Task.FromResult(!FailDelete); }
}
sealed class FailingRepository<T>(VegetarianDbContext db, Exception? error) : Repository<T>(db), IRepository<T> where T : class
{
    Task<int> IRepository<T>.SaveChangesAsync(CancellationToken ct) => error is null ? base.SaveChangesAsync(ct) : Task.FromException<int>(error);
}
sealed class StubHttp(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}
