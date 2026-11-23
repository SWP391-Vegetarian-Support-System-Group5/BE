using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

static class AllergenSourceTests
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS source allergens " + label); }
    static async Task Reject(Func<Task> action, string label) { try { await action(); } catch (ServiceException) { Check(true, label); return; } throw new Exception(label); }
    public static async Task RunAsync()
    {
        await using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
        db.DietTypes.AddRange(new DietType { DietTypeId = 1, Name = "VEGETARIAN" }, new DietType { DietTypeId = 2, Name = "VEGAN" },
            new DietType { DietTypeId = 3, Name = "LACTO_VEGETARIAN" }, new DietType { DietTypeId = 4, Name = "OVO_VEGETARIAN" }, new DietType { DietTypeId = 5, Name = "LACTO_OVO_VEGETARIAN" });
        db.Allergens.AddRange(new Allergen { AllergenId = 1, Name = "Đậu nành" }, new Allergen { AllergenId = 2, Name = "Sữa" }, new Allergen { AllergenId = 3, Name = "Mè" });
        db.IngredientCategories.AddRange(new IngredientCategory { IngredientCategoryId = 1, Code = "PLANT", Name = "Plants", DietaryGroup = "PLANT" }, new IngredientCategory { IngredientCategoryId = 2, Code = "DAIRY", Name = "Dairy", DietaryGroup = "DAIRY" });
        for (int id = 1; id <= 3; id++) db.Users.Add(new User { UserId = id, Email = $"source{id}@example.invalid", PasswordHash = "unused", Role = id == 3 ? "ADMIN" : "USER", IsActive = true,
            Profile = new UserProfile { UserId = id, FullName = "Test", Sex = "MALE", BirthYear = 2000, HeightCm = 170, WeightKg = 60, GoalWeightKg = 60, ActivityLevel = "MODERATE", DietTypeId = 1 } });
        db.RecipeCategories.Add(new RecipeCategory { Id = 1, Name = "Breakfast", IsActive = true });
        await db.SaveChangesAsync();
        Ingredient Ingredient(int id, string name, int category, bool verified, params int[] allergens)
        {
            var item = new Ingredient { IngredientId = id, Name = name, IngredientCategoryId = category, IsAllergenProfileVerified = verified, AllergenEvidence = verified ? "Test product composition" : null };
            foreach (var a in allergens) item.AllergenLinks.Add(new IngredientAllergen { AllergenId = a });
            db.Ingredients.Add(item); return item;
        }
        var soy = Ingredient(1, "Đậu nành", 1, true, 1);
        var tofu = Ingredient(2, "Đậu phụ từ đậu nành - product A", 1, true, 1);
        var soyMilk = Ingredient(3, "Sữa đậu nành - product B", 1, true, 1);
        var yogurt = Ingredient(4, "Sữa chua sữa bò - product C", 2, true, 2);
        var multi = Ingredient(5, "Sốt đậu nành và mè - product D", 1, true, 1, 3);
        var rice = Ingredient(6, "Gạo", 1, true);
        var chickpeaTofu = Ingredient(7, "Đậu hũ đậu gà - product E", 1, true);
        var coconutYogurt = Ingredient(8, "Sữa chua dừa - product F", 1, true);
        var unknown = Ingredient(9, "Đậu hũ chưa rõ nguồn gốc", 1, false);
        void Recipe(int id, string name, Ingredient ingredient, string type = "RECIPE", string status = "PUBLISHED")
        {
            var recipe = new Recipe { PostId = id, RecipeName = name, CaloriesPerServing = 700,
                Post = new Post { PostId = id, UserId = 1, Title = name, Content = name, PostType = type, Status = status, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                Ingredients = [new RecipeIngredient { Ingredient = ingredient, Amount = "100g" }],
                DietCompatibilities = [new RecipeDietCompatibility { DietTypeId = 1, IsCompatible = true }],
                CategoryMappings = [new RecipeCategoryMapping { CategoryId = 1 }] };
            if (ingredient.IngredientCategoryId == 1) recipe.DietCompatibilities.Add(new RecipeDietCompatibility { DietTypeId = 2, IsCompatible = true });
            db.Recipes.Add(recipe);
        }
        Recipe(1, "Món đậu nành", soy); Recipe(2, "Đậu phụ kho", tofu); Recipe(3, "Sinh tố sữa đậu nành", soyMilk);
        Recipe(4, "Sữa chua trái cây", yogurt); Recipe(5, "Sốt nhiều dị ứng", multi); Recipe(6, "Cơm", rice);
        Recipe(7, "Đậu hũ đậu gà", chickpeaTofu); Recipe(8, "Sữa chua thực vật", coconutYogurt);
        Recipe(9, "Chưa xác minh", unknown); Recipe(10, "Video nấu đậu phụ", tofu, "VIDEO"); Recipe(11, "Bản nháp cơm", rice, status: "DRAFT");
        await db.SaveChangesAsync();
        var events = new Events(); var catalog = new IngredientCatalogService(db, events);
        var selection = new UserAllergenService(new Repository<UserAllergen>(db), new Repository<Allergen>(db), new Repository<User>(db));
        var notifications = new UserNotificationService(new NotificationSink(), NullLogger<UserNotificationService>.Instance);
        var posts = new PostService(new Repository<Post>(db), new Repository<Comment>(db), new Repository<PostRating>(db), new Repository<Bookmark>(db), new Repository<ModerationFlag>(db), db, new MockModerationAiService(), notifications, events);
        var recipeService = new RecipeService(new Repository<Post>(db), new Repository<Recipe>(db), new Repository<RecipeIngredient>(db), new Repository<RecipeStep>(db), new Repository<RecipeDietCompatibility>(db), new Repository<Allergen>(db), new Repository<DietType>(db), new Repository<ModerationFlag>(db), new MockModerationAiService(), events, new RecipeCategoryService(db), catalog, db);
        Task<IReadOnlyCollection<RecipeResponse>> Search(int user = 1, string? keyword = null, int? category = null) => recipeService.SearchAsync(keyword, category, null, user);
        await selection.UpdateAsync(1, new([1, 1]));
        Check((await selection.GetAsync(1)).Allergens.Count == 1, "duplicate IDs are removed and persisted response is returned");
        var result = await Search();
        Check(!result.Any(x => new[] { 1, 2, 3, 5, 9, 10 }.Contains(x.PostId)), "soy excludes soy-derived tofu, soy milk, multi-allergen and unverified recipes");
        Check(result.Any(x => x.PostId == 7) && result.Any(x => x.PostId == 8), "chickpea tofu and plant yogurt are not guessed to contain soy/milk");
        Check((await Search(keyword: "Đậu phụ kho")).Count == 0, "exact excluded dish name cannot bypass filtering");
        Check((await Search(category: 1)).Select(x => x.PostId).Order().SequenceEqual(result.Select(x => x.PostId).Order()), "categories use the same rules");
        var feed = await posts.GetAsync(null, null, null, null, null, false, 1);
        Check(feed.Select(x => x.PostId).Order().SequenceEqual(result.Select(x => x.PostId).Order()), "home and recipe search agree including linked VIDEO recipes");
        Check((await posts.GetAsync(null, null, "VIDEO", null, null, false, 1)).Count == 0, "video filtered through linked recipe, not transcript/title");
        await selection.UpdateAsync(2, new([2]));
        var second = await Search(2);
        Check(second.Any(x => x.PostId == 2) && !second.Any(x => x.PostId == 4), "two accounts receive different results; animal yogurt maps to milk");
        Check((await Search()).Any(x => x.PostId == 4), "another account's milk selection does not contaminate soy account");
        await selection.UpdateAsync(1, new([1, 2]));
        Check(!(await Search()).Any(x => x.PostId == 2 || x.PostId == 4 || x.PostId == 5), "ANY matching allergen excludes recipe");
        await selection.UpdateAsync(1, new([2]));
        Check((await Search()).Any(x => x.PostId == 2), "removing soy immediately restores eligible dishes without stale cache");
        await selection.UpdateAsync(1, new([3]));
        Check(!(await Search()).Any(x => x.PostId == 5), "secondary mapping excludes multi-allergen ingredient");
        await Reject(() => selection.UpdateAsync(1, new([-1])), "negative ID rejected");
        await Reject(() => selection.UpdateAsync(1, new([999999])), "nonexistent ID rejected");
        Check((await selection.GetAsync(1)).Allergens.Single().Id == 3, "invalid updates preserve previous selections");
        await selection.UpdateAsync(1, new([]));
        var profile = await db.UserProfiles.SingleAsync(x => x.UserId == 1); profile.DietTypeId = 2; await db.SaveChangesAsync();
        Check(!(await Search()).Any(x => x.PostId == 4 || x.PostId == 11) && (await Search()).Any(x => x.PostId == 9), "empty selection preserves diet/status");
        profile.DietTypeId = 1; await db.SaveChangesAsync(); await selection.UpdateAsync(1, new([1]));
        Check((await posts.GetMineAsync(1)).Any(x => x.PostId == 2) && (await posts.GetAsync(null, null, null, null, null, true, 1)).Any(x => x.PostId == 2), "author management and admin queries stay unfiltered");
        var resolved = await catalog.ResolveAsync([new IngredientRequest(tofu.Name, "1 block", "PLANT", null, IngredientId: tofu.IngredientId)], default);
        Check(resolved.Single().Ingredient.AllergenLinks.Any(x => x.AllergenId == 1), "canonical ID resolution preserves soy origin");
        var aiResolved = await catalog.ResolveAsync([new IngredientRequest(tofu.Name, "1 block", "PLANT", null)], default);
        Check(aiResolved.Single().Ingredient.IngredientId == tofu.IngredientId, "video AI canonical-name resolution uses same mappings");
        await Reject(() => catalog.ReviewAllergensAsync(9, new([1], true, ""), default), "verification requires evidence");
        await catalog.ReviewAllergensAsync(9, new([], true, "Verified chickpea-only product label"), default);
        Check((await Search()).Any(x => x.PostId == 9), "explicit admin verification of absence permits suggestions");
        var request = new CreateRecipeRequest("New ambiguous source", "Recipe", null, 10, 10, 1, [new("Yogurt unknown product", "100g", "PLANT", null)], [new(1, "Mix")], "Unknown yogurt", RequiresAdminReview: false);
        var created = await recipeService.CreateAsync(1, request);
        Check(created.UnverifiedIngredientNames?.Contains("Yogurt unknown product") == true && !(await Search()).Any(x => x.PostId == created.PostId), "unknown source is reported and withheld from allergy suggestions");
        db.PostRatings.AddRange(new PostRating { PostId = 2, UserId = 1, Rating = 5 }, new PostRating { PostId = 6, UserId = 1, Rating = 4 }, new PostRating { PostId = 7, UserId = 1, Rating = 4 }, new PostRating { PostId = 7, UserId = 2, Rating = 4 }); await db.SaveChangesAsync();
        var ranked = await posts.GetAsync(null, null, null, null, null, false, 1, 1, sort: "rating");
        Check(ranked.Single().PostId == 7 && ranked.Single().AverageRating == 4 && ranked.Single().RatingCount == 2, "ratings sorted after filtering before limit; ties use ratingCount");
        var queryFilter = await PersonalRecipeFilter.LoadAsync(db, 1, default);
        using (var mysql = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseMySql("Server=localhost;Database=not_used;User=test;Password=test", new MySqlServerVersion(new Version(8,0,0))).Options))
        {
            var sql = queryFilter.Apply(mysql.Recipes).Take(2).ToQueryString();
            Check(sql.Contains("IngredientAllergens") && sql.Contains("IsAllergenProfileVerified") && sql.Contains("LIMIT"), "filter translates to MySQL before limit without connecting to a database");
        }
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var matcher = new GeminiAllergenIngredientMatcher(new HttpClient(), new GeminiChatbotSettings(), cache);
        var planner = new MealPlanService(new Repository<User>(db), new Repository<Recipe>(db), new Repository<MealPlan>(db), new Repository<MealPlanMeal>(db), cache, db, matcher);
        var plan = await planner.GenerateAsync(1, new(null, null, "MAINTAIN"));
        Check(plan.Meals.Count == 21 && plan.Meals.All(m => !new[] { 1,2,3,5,10,created.PostId }.Contains(m.RecipeId)), "Meal Planner excludes soy-derived/unverified recipes");
        var meal = plan.Meals.First();
        var replacements = await planner.GetReplacementOptionsAsync(plan.MealPlanId, meal.MealPlanMealId, 1, null);
        Check(replacements.All(x => !new[] { 1,2,3,5,10,created.PostId }.Contains(x.RecipeId)), "replacement options obey saved profile");
        await Reject(() => planner.ReplaceMealAsync(plan.MealPlanId, meal.MealPlanMealId, 1, 2), "explicit excluded recipe selection rejected");
        foreach (var ingredient in await db.Ingredients.ToListAsync()) await catalog.ReviewAllergensAsync(ingredient.IngredientId, new([1], true, "Isolated test composition"), default);
        Check((await Search()).Count == 0 && (await posts.GetAsync(null,null,null,null,null,false,1)).Count == 0, "empty home/search results do not fall back");
        var emptyPlan = await planner.GenerateAsync(1, new(null, null, "MAINTAIN"));
        Check(emptyPlan.Meals.Count == 0 && emptyPlan.MealPlanId == 0 && await db.MealPlans.CountAsync() == 1, "empty planner result does not store a dummy plan");
        for (var id=10; id<110; id++) db.Allergens.Add(new Allergen { AllergenId=id, Name="Test allergen "+id }); await db.SaveChangesAsync();
        Check((await selection.UpdateAsync(2,new(Enumerable.Range(10,100).ToList()))).Allergens.Count == 100, "no arbitrary selection limit");
        Console.WriteLine($"SOURCE ALLERGEN TESTS: {checks} passed; isolated database only.");
    }
    sealed class Events : ICommunityEventPublisher { public Task PublishAsync(CommunityEvent e, CancellationToken ct=default) => Task.CompletedTask; }
    sealed class NotificationSink : INotificationEventPublisher { public Task SendAsync(int id, UserNotificationResponse n, CancellationToken ct=default) => Task.CompletedTask; }
}
