using API.Controllers;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

static class BmiTests
{
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS BMI " + name);
    }

    static async Task Reject(Func<Task> action, int status, string name)
    {
        try { await action(); throw new Exception("Expected rejection: " + name); }
        catch (ServiceException error) { Check(error.StatusCode == status, name); }
    }

    public static async Task RunAsync()
    {
        using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        await BMISchemaUpdate.ApplyAsync(db);
        await BMISchemaUpdate.ApplyAsync(db);
        Check(await db.BMITypes.CountAsync() == 3, "fixed seed is idempotent");
        var key = db.Model.FindEntityType(typeof(RecipeBMIType))!.FindPrimaryKey()!;
        Check(key.Properties.Select(x => x.Name).SequenceEqual(["RecipeId", "BMITypeId"]), "unique composite key");
        Check(db.Model.FindEntityType(typeof(RecipeBMIType))!.GetForeignKeys().Any(x => x.PrincipalEntityType.ClrType == typeof(Recipe) && x.DeleteBehavior == DeleteBehavior.Cascade), "recipe delete cascades mappings");
        var normal = await db.BMITypes.SingleAsync(x => x.Code == "NORMAL");
        var under = await db.BMITypes.SingleAsync(x => x.Code == "UNDERWEIGHT");
        var obese = await db.BMITypes.SingleAsync(x => x.Code == "OBESE");
        var vegan = new DietType { Name = "VEGAN" };
        foreach (var name in new[] { "VEGETARIAN", "LACTO_VEGETARIAN", "OVO_VEGETARIAN", "LACTO_OVO_VEGETARIAN" }) db.DietTypes.Add(new DietType { Name = name });
        db.DietTypes.Add(vegan);
        db.IngredientCategories.AddRange(new IngredientCategory { Code = "PLANT", Name = "Plant", DietaryGroup = "PLANT" }, new IngredientCategory { Code = "DAIRY", Name = "Dairy", DietaryGroup = "DAIRY" });
        db.Users.AddRange(new User { UserId = 100, Email = "admin@example.invalid", PasswordHash = "unused", Role = "ADMIN", IsActive = true, Profile = new UserProfile { FullName = "Admin" } },
            new User { UserId = 101, Email = "user@example.invalid", PasswordHash = "unused", Role = "USER", IsActive = true, Profile = new UserProfile { FullName = "User", HeightCm = 170, WeightKg = 60, DietType = vegan } });
        await db.SaveChangesAsync();
        var events = new TestEvents();
        var categories = new RecipeCategoryService(db);
        await categories.SeedAsync(default);
        var bmi = new RecipeBmiService(db, events);
        var catalog = new IngredientCatalogService(db, categories, events);
        var recipes = new RecipeService(new Repository<Post>(db), new Repository<Recipe>(db), new Repository<RecipeIngredient>(db), new Repository<RecipeStep>(db), new Repository<RecipeDietCompatibility>(db), new Repository<Allergen>(db), new Repository<DietType>(db), events, categories, catalog, db, bmi);
        var request = new CreateRecipeRequest("Post title", "Content", null, 10, 10, 2, null,
            [new("Tofu", "100g", "PLANT", null)], [new(1, "Cook")], "Tofu dish", [], [normal.BMITypeId, under.BMITypeId]);
        var recipe = await recipes.CreateAsync(100, request);
        Check(recipe.BmiTypes!.Count == 2, "admin create stores multiple groups and returns them");
        var chinese = await db.RecipeCategories.SingleAsync(x => x.Slug == "chinese");
        await categories.UpdateRecipeMappingsAsync(recipe.PostId, [chinese.Id], default);
        Check((await bmi.RecipesAsync(default)).Single(x => x.PostId == recipe.PostId).CategoryIds.Contains(chinese.Id), "admin recipe list exposes assigned category");
        await categories.UpdateRecipeMappingsAsync(recipe.PostId, [], default);
        Check(!(await bmi.RecipesAsync(default)).Single(x => x.PostId == recipe.PostId).CategoryIds.Contains(chinese.Id), "removing category updates the recipe mapping");
        await bmi.UpdateAsync(recipe.PostId, [normal.BMITypeId, normal.BMITypeId], 100, default);
        Check(await db.RecipeBMITypes.CountAsync(x => x.RecipeId == recipe.PostId) == 1, "deduplicate and remove previous assignments");
        await Reject(() => bmi.UpdateAsync(recipe.PostId, [obese.BMITypeId], 101, default), 403, "user cannot assign BMI groups");
        await Reject(() => recipes.CreateAsync(101, request), 403, "user cannot inject assignments in recipe creation");
        await Reject(() => bmi.UpdateAsync(recipe.PostId, [999999], 100, default), 400, "unknown BMI rejected");
        Check((await recipes.GetAsync(recipe.PostId)).BmiTypes!.Single().Code == "NORMAL", "invalid update preserves existing groups");
        var unassigned = await recipes.CreateAsync(101, request with { RecipeName = "Unassigned tofu", BmiTypeIds = null });
        var wrongBmi = await recipes.CreateAsync(100, request with { RecipeName = "Other BMI tofu", BmiTypeIds = [obese.BMITypeId] });
        var dairy = await recipes.CreateAsync(100, request with { RecipeName = "Dairy dish", Ingredients = [new("Milk", "100ml", "DAIRY", null)], BmiTypeIds = [normal.BMITypeId] });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var planner = new MealPlanService(new Repository<User>(db), new Repository<Recipe>(db), new Repository<MealPlan>(db), new Repository<MealPlanMeal>(db), cache, new ConfigurationBuilder().Build(), db);
        var plan = await planner.GenerateAsync(101, new GenerateMealPlanRequest(null, "đậu hũ"));
        Check(plan.Meals.Count == 21 && plan.Meals.All(x => x.RecipeId == recipe.PostId), "planner excludes unassigned, wrong-BMI and non-vegan recipes in local fallback");
        Check(IngredientPreferences.MatchCount(["Tofu"], IngredientPreferences.Parse("đậu hũ")) == 1, "ingredient ranking reuses bilingual matching");
        var otherIngredients = await recipes.CreateAsync(100, request with { RecipeName = "Spinach dish", Ingredients = [new("Spinach", "100g", "PLANT", null)], BmiTypeIds = [normal.BMITypeId] });
        var ranked = await planner.GenerateAsync(101, new(null, "đậu hũ"));
        Check(ranked.Meals.First().RecipeId == recipe.PostId, "matching ingredients rank above other eligible recipes");
        await bmi.UpdateAsync(otherIngredients.PostId, [], 100, default);
        await recipes.UpdateAsync(recipe.PostId, 100, "ADMIN", request with { BmiTypeIds = [normal.BMITypeId, under.BMITypeId] });
        Check((await recipes.GetAsync(recipe.PostId)).BmiTypes!.Count == 2, "recipe update persists BMI groups");
        await recipes.UpdateAsync(unassigned.PostId, 101, "USER", request with { BmiTypeIds = null });
        Check((await recipes.GetAsync(unassigned.PostId)).BmiTypes!.Count == 0, "user edit does not self-assign groups");
        await bmi.UpdateAsync(recipe.PostId, [], 100, default);
        await Reject(() => planner.RegenerateAsync(plan.MealPlanId, 101, new(null, "Tofu")), 400, "empty BMI result does not fabricate a plan");
        Check(await db.MealPlans.AnyAsync(x => x.MealPlanId == plan.MealPlanId), "failed regeneration preserves prior plan");
        await Reject(() => planner.ReplaceMealAsync(plan.MealPlanId, plan.Meals.First().MealPlanMealId, 101), 404, "replacement cannot bypass BMI filter");
        var profile = await db.UserProfiles.SingleAsync(x => x.UserId == 101);
        profile.HeightCm = null;
        await db.SaveChangesAsync();
        await Reject(() => planner.GenerateAsync(101, new(null, null)), 400, "missing measurements require profile update");
        profile.HeightCm = 170; profile.WeightKg = 80;
        await db.SaveChangesAsync();
        var obesePlan = await planner.GenerateAsync(101, new(null, "Tofu"));
        Check(obesePlan.BmiCategory == "Obese" && obesePlan.Meals.All(x => x.RecipeId == wrongBmi.PostId), "BMI >=25 uses OBESE recipes as approved by project owner");
        Check(MealPlannerBmi.Category(18.49m) == "Underweight" && MealPlannerBmi.Category(18.5m) == "Normal" && MealPlannerBmi.Category(25m) == "Obese", "existing boundaries unchanged with project-approved OBESE name");
        var controller = typeof(RecipeBmiController);
        Check(new[] { "Update", "Recipes", "UpdateCategories" }.All(name => controller.GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Any(x => x.Roles == "ADMIN")), "server endpoints require ADMIN role");
    }

    public static async Task ApplyAivenAsync()
    {
        var config = File.ReadAllLines("API/.aiven.env").Where(x => x.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(x => x.Split('=', 2)[1].Trim().Trim('"', '\'')).Single();
        using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseMySql(config, new MySqlServerVersion(new Version(8, 0, 0))).Options);
        var recipes = await db.Recipes.CountAsync();
        var posts = await db.Posts.CountAsync();
        await BMISchemaUpdate.ApplyAsync(db);
        await BMISchemaUpdate.ApplyAsync(db);
        Check(await db.Recipes.CountAsync() == recipes && await db.Posts.CountAsync() == posts, "Aiven additive schema update preserves posts and recipes");
        Check(await db.BMITypes.CountAsync() == 3, "Aiven contains three fixed groups");
        Console.WriteLine($"BMI schema ready. Preserved {recipes} recipes and {posts} posts. No recipe BMI assignments were fabricated.");
    }
}
