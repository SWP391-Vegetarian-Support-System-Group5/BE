using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Text.Json;

if (args.Contains("--admin-api"))
{
    await AdminApiTests.RunAsync();
    return;
}

if (args.Contains("--preview-reset-aiven"))
{
    await AivenResetPreview.RunAsync();
    return;
}

if (args.Contains("--remove-dietary-recipe-categories"))
{
    await AivenResetPreview.RemoveDietaryCategoriesAsync();
    return;
}

if (args.Contains("--seed-recipe-catalog-aiven"))
{
    await RecipeCatalogSeed.ApplyAivenAsync(args.FirstOrDefault(value => value.StartsWith("--confirm="))?.Split('=', 2)[1] ?? "");
    return;
}

if (args.Contains("--verify-recipe-catalog-aiven"))
{
    await RecipeCatalogSeed.VerifyAivenAsync();
    return;
}

if (args.Contains("--drop-recipe-calories-aiven"))
{
    await RecipeCaloriesSchemaUpdate.ApplyAivenAsync();
    return;
}

if (args.Contains("--verify-recipe-calories-aiven"))
{
    await RecipeCaloriesSchemaUpdate.VerifyAivenAsync();
    return;
}

if (args.Contains("--reset-aiven-keep-admin"))
{
    await AivenResetPreview.ResetAsync(args.FirstOrDefault(value => value.StartsWith("--confirm="))?.Split('=', 2)[1] ?? "");
    return;
}

if (args.Contains("--video-analysis"))
{
    await VideoAnalysisTests.RunAsync();
    return;
}

if (args.Contains("--apply-bmi-aiven"))
{
    await BmiTests.ApplyAivenAsync();
    return;
}

if (args.Contains("--inspect-aiven") || args.Contains("--apply-aiven"))
{
    var config = File.ReadAllLines("API/.aiven.env").Where(x => x.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
        .Select(x => x.Split('=', 2)[1].Trim().Trim('"', '\'')).Single();
    var connectionOptions = new MySqlConnectionStringBuilder(config) { AllowUserVariables = true, UseAffectedRows = false };
    await using var connection = new MySqlConnection(connectionOptions.ConnectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='RecipeIngredients' AND column_name='IngredientName'";
    var legacy = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    if (args.Contains("--apply-aiven"))
    {
        if (legacy)
        {
            var backup = new Dictionary<string, List<Dictionary<string, object?>>>();
            foreach (var (name, sql) in new Dictionary<string, string> {
                ["RecipeIngredients"] = "SELECT * FROM RecipeIngredients", ["Categories"] = "SELECT * FROM Categories",
                ["RestaurantReviews"] = "SELECT * FROM RestaurantReviews", ["UserProfileHealthGoals"] = "SELECT UserId,HealthGoal FROM UserProfiles",
                ["PostCategories"] = "SELECT PostId,CategoryId FROM Posts", ["DraftCategories"] = "SELECT VideoRecipeDraftId,CategoryId FROM VideoRecipeDrafts" })
            {
                command.CommandText = sql;
                var rows = new List<Dictionary<string, object?>>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>();
                    for (var index = 0; index < reader.FieldCount; index++) row.Add(reader.GetName(index), reader.IsDBNull(index) ? null : reader.GetValue(index));
                    rows.Add(row);
                }
                backup.Add(name, rows);
            }
            Directory.CreateDirectory(".local/schema-backups");
            var path = Path.GetFullPath($".local/schema-backups/ingredients-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(backup));
            Console.WriteLine("Schema backup: " + path);
        }
        using var liveDb = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 0))).Options);
        var recipesBefore = await liveDb.Recipes.CountAsync();
        command.CommandText = "SELECT COUNT(*) FROM RecipeIngredients";
        var linksBefore = Convert.ToInt32(await command.ExecuteScalarAsync());
        await IngredientSchemaUpdate.ApplyAsync(liveDb);
        await IngredientSchemaUpdate.ApplyAsync(liveDb);
        await new IngredientCatalogService(liveDb, new TestEvents()).RefreshCompatibilityAsync(default);
        await new RecipeCategoryService(liveDb).SeedAsync(default);
        if (await liveDb.Recipes.CountAsync() != recipesBefore || await liveDb.RecipeIngredients.CountAsync() != linksBefore)
            throw new Exception("Migration changed recipe or ingredient-link counts.");
        Console.WriteLine($"PASS Aiven migration is idempotent; preserved {recipesBefore} recipes and {linksBefore} ingredient links; catalog has {await liveDb.Ingredients.CountAsync()} ingredients.");
        legacy = false;
    }
    command.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM information_schema.columns WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('RecipeIngredients','Ingredients','IngredientCategories','Categories','RestaurantReviews','Posts','VideoRecipeDrafts','UserProfiles') ORDER BY TABLE_NAME, ORDINAL_POSITION";
    await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) Console.WriteLine(reader.GetString(0) + "." + reader.GetString(1));
    if (legacy)
    {
        command.CommandText = "SELECT TRIM(IngredientName), GROUP_CONCAT(DISTINCT DietaryGroup), COUNT(DISTINCT AllergenId) FROM RecipeIngredients GROUP BY TRIM(IngredientName) HAVING COUNT(DISTINCT DietaryGroup)>1 OR COUNT(DISTINCT AllergenId)>1";
        await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) Console.WriteLine($"CONFLICT {reader.GetString(0)} groups={reader.GetString(1)} allergens={reader.GetInt32(2)}");
    }
    return;
}

using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
var service = new RecipeCategoryService(db);
void Check(bool valid, string name) { if (!valid) throw new Exception(name); Console.WriteLine("PASS " + name); }
async Task Rejected(Func<Task> action, string name) { try { await action(); throw new Exception("Expected rejection: " + name); } catch (ServiceException) { Console.WriteLine("PASS " + name); } }
Check(ImageUrlValidation.Normalize("https://res.cloudinary.com/demo/image/upload/sample.jpg") is not null, "accept Cloudinary image URL");
await Rejected(() => Task.Run(() => ImageUrlValidation.Normalize("https://example.com/photo.jpg")), "reject external image URL");
await Rejected(() => Task.Run(() => ImageUrlValidation.Normalize("https://res.cloudinary.com/demo/video/upload/sample.mp4")), "reject Cloudinary video as image URL");
RecipeCategoryRequest Request(RecipeCategory c, bool active = true) => new(c.Name, c.Slug, c.Type, c.Description, c.ImageUrl, active);
db.RecipeCategories.Add(new RecipeCategory { Name = "Vegan", Slug = "vegan", Type = "DIETARY_TYPE", CreatedAt = DateTime.UtcNow });
await db.SaveChangesAsync();
await service.SeedAsync(default);
await service.SeedAsync(default);
Check(await db.RecipeCategories.CountAsync() == 10, "idempotent 10 menu categories");
Check((await service.ListAsync("DISH_TYPE", false, default)).Count == 10, "dish category type filter");
Check(!await db.RecipeCategories.AnyAsync(c => c.Type == "DIETARY_TYPE"), "dietary compatibility stays outside recipe categories");
await Rejected(() => service.ValidateSelectionAsync([999], ["PLANT"], default), "reject unknown category");
await Rejected(async () => { await service.SaveAsync(null, new("Vegan", "vegan", "DIETARY_TYPE", null, null), default); }, "reject dietary type in recipe categories");
var dairyCategory = new IngredientCategory { Code = "DAIRY", Name = "Dairy", DietaryGroup = "DAIRY" };
db.IngredientCategories.Add(dairyCategory);
db.IngredientCategories.Add(new IngredientCategory { Code = "PLANT", Name = "Plant", DietaryGroup = "PLANT" });
db.Recipes.Add(new Recipe { PostId = 500, RecipeName = "Soup", Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "Milk", Category = dairyCategory } }] });
await db.SaveChangesAsync();
var soup = await db.RecipeCategories.SingleAsync(c => c.Slug == "sup-mon-ham");
await service.SetMappingsAsync(500, [soup.Id, soup.Id], ["DAIRY"], default);
await db.SaveChangesAsync();
Check(await db.RecipeCategoryMappings.CountAsync(m => m.RecipeId == 500) == 1, "one dish has one selected category");
await service.SetMappingsAsync(500, [soup.Id], ["PLANT"], default);
await db.SaveChangesAsync();
Check(await db.RecipeCategoryMappings.CountAsync(m => m.RecipeId == 500) == 1, "category mapping remains independent of ingredient diet group");
await service.SaveAsync(soup.Id, Request(soup, false), default);
Check(!(await service.ListAsync(null, false, default)).Any(c => c.Id == soup.Id), "public excludes inactive");
Check((await service.ListAsync(null, true, default)).Any(c => c.Id == soup.Id), "admin can see inactive");
await Rejected(() => service.ValidateSelectionAsync([soup.Id], ["PLANT"], default), "reject inactive selection");
Check(db.Model.FindEntityType(typeof(MealPlan))!.FindProperty("BMR") is null && db.Model.FindEntityType(typeof(MealPlanMeal))!.FindProperty("PlannedCalories") is null, "obsolete meal properties removed");
Check(db.Model.FindEntityType(typeof(VideoRecipeDraft))!.FindProperty("Servings") is null, "draft servings removed");
var key = db.Model.FindEntityType(typeof(RecipeCategoryMapping))!.FindPrimaryKey()!;
Check(key.Properties.Select(p => p.Name).SequenceEqual(["RecipeId", "CategoryId"]), "composite mapping key");
foreach (var name in new[] { "VEGAN", "VEGETARIAN", "LACTO_VEGETARIAN", "OVO_VEGETARIAN", "LACTO_OVO_VEGETARIAN" }) db.DietTypes.Add(new DietType { Name = name });
db.Users.Add(new User { UserId = 1, Email = "test@example.invalid", PasswordHash = "unused", Role = "USER", Profile = new UserProfile { FullName = "Test" } });
await db.SaveChangesAsync();
var events = new TestEvents();
var ingredientCatalog = new IngredientCatalogService(db, events);
var recipeService = new RecipeService(new Repository<Post>(db), new Repository<Recipe>(db), new Repository<RecipeIngredient>(db), new Repository<RecipeStep>(db), new Repository<RecipeDietCompatibility>(db), new Repository<Allergen>(db), new Repository<DietType>(db), new Repository<ModerationFlag>(db), new MockModerationAiService(), events, service, ingredientCatalog, db, new RecipeBmiService(db, events));
var dinner = await db.RecipeCategories.SingleAsync(c => c.Slug == "mon-an-sang");
var request = new CreateRecipeRequest("Test post", "Test content", null, 10, 10, 2,
    [new("Rice", "100g", "PLANT", null)], [new(1, "Cook rice")], "Rice bowl", [dinner.Id], ImageUrl: "https://res.cloudinary.com/test/image/upload/v1/rice.jpg");
var created = await recipeService.CreateAsync(1, request);
Check(created.ImageUrl == "https://res.cloudinary.com/test/image/upload/v1/rice.jpg" && created.ThumbnailUrl == created.ImageUrl, "recipe image stored and used as thumbnail");
Check(created.Categories.Count == 1, "create recipe persists selected category");
Check((await recipeService.SearchAsync(null, dinner.Id, "DISH_TYPE")).Any(r => r.PostId == created.PostId), "recipe search category + type");
await recipeService.UpdateAsync(created.PostId, 1, "USER", request with { Ingredients = [new("Cheese", "50g", "DAIRY", null)], CategoryIds = [], ImageUrl = "https://res.cloudinary.com/test/image/upload/v1/cheese.jpg" });
var updated = await recipeService.GetAsync(created.PostId);
Check(updated.ImageUrl == "https://res.cloudinary.com/test/image/upload/v1/cheese.jpg" && updated.ThumbnailUrl == updated.ImageUrl, "recipe image updated");
Check(updated.Categories.Count == 0, "editing recipe removes unselected category");
Check(events.Items.SequenceEqual(["postCreated", "postUpdated"]), "recipe mutations publish realtime events");
await ingredientCatalog.ResolveAsync([new("Rice", "200g", "PLANT", null)], default);
Check(await db.Ingredients.CountAsync(x => x.Name == "Rice") == 1, "shared ingredient reused across recipes");
await Rejected(() => ingredientCatalog.ResolveAsync([new("Rice", "200g", "DAIRY", null)], default), "reject conflicting shared ingredient category");
Check(db.Model.FindEntityType(typeof(RecipeIngredient))!.FindProperty("DietaryGroup") is null, "dietary group moved to ingredient categories");
Check(db.Model.FindEntityType(typeof(UserProfile))!.FindProperty("HealthGoal") is null && db.Model.FindEntityType(typeof(Post))!.FindProperty("CategoryId") is null, "legacy profile/post fields removed");
var cheese = await db.Ingredients.SingleAsync(x => x.Name == "Cheese");
var plant = await db.IngredientCategories.SingleAsync(x => x.Code == "PLANT");
await ingredientCatalog.UpdateAsync(cheese.IngredientId, new("Plant cheese", plant.IngredientCategoryId, null), default);
var reclassified = await recipeService.GetAsync(created.PostId);
Check(reclassified.Ingredients.Single().IngredientName == "Plant cheese" && reclassified.Categories.Count == 0, "shared ingredient edit leaves recipe categories unchanged");
var veganDietId = await db.DietTypes.Where(x => x.Name == "VEGAN").Select(x => x.DietTypeId).SingleAsync();
Check(reclassified.CompatibleDietTypeIds.Contains(veganDietId), "shared category edit refreshes planner compatibility");
Check(events.Items.Last() == "postUpdated" && events.Items.Count == 3, "shared ingredient edit publishes recipe refresh event");

await BmiTests.RunAsync();
await NotificationTests.RunAsync();
await CustomAllergenTests.RunAsync();
await GeminiReviewTests.RunAsync();

sealed class TestEvents : ICommunityEventPublisher
{
    public List<string> Items { get; } = [];
    public Task PublishAsync(CommunityEvent e, CancellationToken ct = default) { Items.Add(e.Type); return Task.CompletedTask; }
}
