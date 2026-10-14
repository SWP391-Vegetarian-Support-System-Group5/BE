using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

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
db.Recipes.Add(new Recipe { PostId = 500, RecipeName = "Soup", Ingredients = [new RecipeIngredient { IngredientName = "Milk", DietaryGroup = "DAIRY" }] });
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
var recipeService = new RecipeService(new Repository<Post>(db), new Repository<Recipe>(db), new Repository<RecipeIngredient>(db), new Repository<RecipeStep>(db), new Repository<RecipeDietCompatibility>(db), new Repository<Category>(db), new Repository<Allergen>(db), new Repository<DietType>(db), events, service, db);
var dinner = await db.RecipeCategories.SingleAsync(c => c.Slug == "dinner");
var request = new CreateRecipeRequest("Test post", "Test content", null, null, 10, 10, 2, null,
    [new("Rice", "100g", "PLANT", null)], [new(1, "Cook rice")], "Rice bowl", [dinner.Id]);
var created = await recipeService.CreateAsync(1, request);
Check(created.Categories.Count == 5, "create recipe persists selected + derived categories");
Check((await recipeService.SearchAsync(null, dinner.Id, "MEAL_TYPE")).Any(r => r.PostId == created.PostId), "recipe search category + type");
await recipeService.UpdateAsync(created.PostId, 1, "USER", request with { Ingredients = [new("Cheese", "50g", "DAIRY", null)], CategoryIds = [] });
var updated = await recipeService.GetAsync(created.PostId);
Check(updated.Categories.Count == 2 && updated.Categories.All(c => c.Slug != "vegan"), "editing dairy recalculates labels and removes old selection");
Check(events.Items.SequenceEqual(["postCreated", "postUpdated"]), "recipe mutations publish realtime events");

sealed class TestEvents : ICommunityEventPublisher
{
    public List<string> Items { get; } = [];
    public Task PublishAsync(CommunityEvent e, CancellationToken ct = default) { Items.Add(e.Type); return Task.CompletedTask; }
}
