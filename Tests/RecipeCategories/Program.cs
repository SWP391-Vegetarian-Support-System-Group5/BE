using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Text.Json;

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
        await new IngredientCatalogService(liveDb, new RecipeCategoryService(liveDb), new TestEvents()).RefreshCompatibilityAsync(default);
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
RecipeCategoryRequest Request(RecipeCategory c, bool active = true) => new(c.Name, c.Slug, c.Type, c.Description, c.ImageUrl, active);
await service.SeedAsync(default);
await service.SeedAsync(default);
Check(await db.RecipeCategories.CountAsync() == 35, "idempotent 35 category seed");
Check((await service.ListAsync("CUISINE", false, default)).Count == 10, "type filter");
Check(RecipeCategoryService.DietSlugs.Count == 4 && !RecipeCategoryService.DietSlugs.Values.Contains("PESCATARIAN"), "strict vegetarian categories");
var vegan = await db.RecipeCategories.SingleAsync(c => c.Slug == "vegan");
await Rejected(() => service.ValidateSelectionAsync([vegan.Id], ["PLANT", "DAIRY"], default), "reject dairy recipe labeled vegan");
await Rejected(() => service.ValidateSelectionAsync([vegan.Id], [], default), "empty ingredients cannot receive dietary labels");
await Rejected(() => service.ValidateSelectionAsync([999], ["PLANT"], default), "reject unknown category");
await Rejected(async () => { await service.SaveAsync(null, Request(vegan), default); }, "reject duplicate slug/type");
await Rejected(async () => { await service.SaveAsync(null, new("Pescatarian", "pescatarian", "DIETARY_TYPE", null, null), default); }, "reject pescatarian");
var dairyCategory = new IngredientCategory { Code = "DAIRY", Name = "Dairy" };
db.IngredientCategories.Add(dairyCategory);
db.IngredientCategories.Add(new IngredientCategory { Code = "PLANT", Name = "Plant" });
db.Recipes.Add(new Recipe { PostId = 500, RecipeName = "Soup", Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "Milk", Category = dairyCategory } }] });
await db.SaveChangesAsync();
var soup = await db.RecipeCategories.SingleAsync(c => c.Slug == "soup");
await service.SetMappingsAsync(500, [soup.Id, soup.Id], ["DAIRY"], default);
await db.SaveChangesAsync();
Check(await db.RecipeCategoryMappings.CountAsync(m => m.RecipeId == 500) == 3, "one dish has soup + lacto + lacto-ovo, no vegan");
Check(!await db.RecipeCategoryMappings.AnyAsync(m => m.RecipeId == 500 && m.CategoryId == vegan.Id), "no vegan dairy mapping");
await service.SetMappingsAsync(500, [soup.Id], ["PLANT"], default);
await db.SaveChangesAsync();
Check(await db.RecipeCategoryMappings.CountAsync(m => m.RecipeId == 500) == 5, "mapping replacement and all four plant dietary labels");
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
var ingredientCatalog = new IngredientCatalogService(db, service, events);
var recipeService = new RecipeService(new Repository<Post>(db), new Repository<Recipe>(db), new Repository<RecipeIngredient>(db), new Repository<RecipeStep>(db), new Repository<RecipeDietCompatibility>(db), new Repository<Allergen>(db), new Repository<DietType>(db), events, service, ingredientCatalog, db);
var dinner = await db.RecipeCategories.SingleAsync(c => c.Slug == "dinner");
var request = new CreateRecipeRequest("Test post", "Test content", null, 10, 10, 2, null,
    [new("Rice", "100g", "PLANT", null)], [new(1, "Cook rice")], "Rice bowl", [dinner.Id]);
var created = await recipeService.CreateAsync(1, request);
Check(created.Categories.Count == 5, "create recipe persists selected + derived categories");
Check((await recipeService.SearchAsync(null, dinner.Id, "MEAL_TYPE")).Any(r => r.PostId == created.PostId), "recipe search category + type");
await recipeService.UpdateAsync(created.PostId, 1, "USER", request with { Ingredients = [new("Cheese", "50g", "DAIRY", null)], CategoryIds = [] });
var updated = await recipeService.GetAsync(created.PostId);
Check(updated.Categories.Count == 2 && updated.Categories.All(c => c.Slug != "vegan"), "editing dairy recalculates labels and removes old selection");
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
Check(reclassified.Ingredients.Single().IngredientName == "Plant cheese" && reclassified.Categories.Any(x => x.Slug == "vegan"), "shared ingredient edit refreshes recipe name and dietary labels");
var veganDietId = await db.DietTypes.Where(x => x.Name == "VEGAN").Select(x => x.DietTypeId).SingleAsync();
Check(reclassified.CompatibleDietTypeIds.Contains(veganDietId), "shared category edit refreshes planner compatibility");
Check(events.Items.Last() == "postUpdated" && events.Items.Count == 3, "shared ingredient edit publishes recipe refresh event");

sealed class TestEvents : ICommunityEventPublisher
{
    public List<string> Items { get; } = [];
    public Task PublishAsync(CommunityEvent e, CancellationToken ct = default) { Items.Add(e.Type); return Task.CompletedTask; }
}
