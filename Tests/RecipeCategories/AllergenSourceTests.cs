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
        Check(!result.Any(x => new[] { 1, 2, 3, 5, 10 }.Contains(x.PostId)), "soy excludes soy-derived tofu, soy milk and multi-allergen recipes");
        Check(result.Any(x => x.PostId == 9 && x.UnverifiedIngredientNames!.Contains(unknown.Name)), "unverified unmatched recipe remains visible with verification metadata");
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
        Check(created.UnverifiedIngredientNames?.Contains("Yogurt unknown product") == true, "unknown source remains explicitly reported");
        db.PostRatings.AddRange(new PostRating { PostId = 2, UserId = 1, Rating = 5 }, new PostRating { PostId = 6, UserId = 1, Rating = 4 }, new PostRating { PostId = 7, UserId = 1, Rating = 4 }, new PostRating { PostId = 7, UserId = 2, Rating = 4 }); await db.SaveChangesAsync();
        var ranked = await posts.GetAsync(null, null, null, null, null, false, 1, 1, sort: "rating");
        Check(ranked.Single().PostId == 7 && ranked.Single().AverageRating == 4 && ranked.Single().RatingCount == 2, "ratings sorted after filtering before limit; ties use ratingCount");
        var queryFilter = await PersonalRecipeFilter.LoadAsync(db, 1, default);
        using (var mysql = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>().UseMySql("Server=localhost;Database=not_used;User=test;Password=test", new MySqlServerVersion(new Version(8,0,0))).Options))
        {
            var sql = queryFilter.Apply(mysql.Recipes).Take(2).ToQueryString();
            Check(sql.Contains("IngredientAllergens") && !sql.Contains("IsAllergenProfileVerified") && sql.Contains("LIMIT"), "filter translates to MySQL before limit without connecting to a database");
        }
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var matcher = new GeminiAllergenIngredientMatcher(new HttpClient(), new GeminiChatbotSettings(), cache);
        var planner = new MealPlanService(new Repository<User>(db), new Repository<Recipe>(db), new Repository<MealPlan>(db), new Repository<MealPlanMeal>(db), cache, db, matcher);
        var plan = await planner.GenerateAsync(1, new(null, null, "MAINTAIN"));
        Check(plan.Meals.Count == 21 && plan.Meals.All(m => !new[] { 1,2,3,5,10 }.Contains(m.RecipeId)), "Meal Planner excludes matching soy-derived recipes");
        var meal = plan.Meals.First();
        var replacements = await planner.GetReplacementOptionsAsync(plan.MealPlanId, meal.MealPlanMealId, 1, null);
        Check(replacements.All(x => !new[] { 1,2,3,5,10 }.Contains(x.RecipeId)), "replacement options obey saved profile");
        await Reject(() => planner.ReplaceMealAsync(plan.MealPlanId, meal.MealPlanMealId, 1, 2), "explicit excluded recipe selection rejected");
        await selection.UpdateAsync(2, new([2]));
        var chat = new NutritionChatbotService(new Repository<ChatSession>(db), new Repository<ChatMessage>(db), new Repository<User>(db),
            new Repository<Recipe>(db), new Ai(), new Knowledge(), matcher, new BillingService(db, new SePaySettings(), notifications));
        var session = await chat.CreateSessionAsync(2);
        var related = await chat.SendAsync(session.ChatSessionId, 2, "ADMIN", null, new("Sữa chua thực vật"));
        Check(related.RelatedRecipes.All(x => !new[] { 3, 4, 8 }.Contains(x.RecipeId)),
            "chat respects the broad saved milk keyword, including plant milk names");
        await selection.UpdateAsync(2, new([1]));
        related = await chat.SendAsync(session.ChatSessionId, 2, "ADMIN", null, new("Đậu phụ kho"));
        Check(related.RelatedRecipes.All(x => !new[] {1,2,3,5,10}.Contains(x.RecipeId)),
            "chat exact excluded dish name cannot bypass saved allergy profile");

        // Reproduce an upgraded catalog: every profile defaults to unverified.
        // Saved allergy IDs must progressively remove matches, not empty the feed.
        var allIngredients = await db.Ingredients.ToListAsync();
        foreach (var ingredient in allIngredients) { ingredient.IsAllergenProfileVerified = false; ingredient.AllergenEvidence = null; }
        yogurt.AllergenId = 2; // Exercise the retained legacy-only relation too.
        db.IngredientAllergens.RemoveRange(yogurt.AllergenLinks.ToArray());
        await db.SaveChangesAsync();
        await selection.UpdateAsync(1, new([]));
        var baseline = (await Search()).Select(x => x.PostId).ToHashSet();
        foreach (var (ids, excluded) in new[] {
            (new[] { 1 }, new[] { 1,2,3,5,10 }),
            (new[] { 1,2 }, new[] { 1,2,3,4,5,8,10 }),
            (new[] { 2 }, new[] { 3,4,8 }),
            (Array.Empty<int>(), Array.Empty<int>()) })
        {
            await selection.UpdateAsync(1, new(ids.ToList()));
            var expected = baseline.Except(excluded).Order().ToArray();
            var searchIds = (await Search()).Select(x => x.PostId).Order().ToArray();
            var feedIds = (await posts.GetAsync(null,null,null,null,null,false,1)).Select(x => x.PostId).Order().ToArray();
            Check(searchIds.SequenceEqual(expected) && feedIds.SequenceEqual(expected),
                "unverified catalog progressively filters home/search for [" + string.Join(",", ids) + "]");
            var blocked = await matcher.FindUnsafeAsync(await db.Allergens.Where(a => ids.Contains(a.AllergenId)).ToListAsync(), allIngredients);
            Check(blocked.SetEquals(allIngredients.Where(i => (i.AllergenId.HasValue && ids.Contains(i.AllergenId.Value)) ||
                i.AllergenLinks.Any(a => ids.Contains(a.AllergenId)) || db.Allergens.AsEnumerable().Any(a => ids.Contains(a.AllergenId) && IngredientAllergyMatch.NameMatches(i.Name, a.Name))).Select(i => i.IngredientId)),
                "matcher uses recorded relations even when all profiles are unverified");
        }
        await selection.UpdateAsync(1, new([1]));
        var unverifiedOptions = await planner.GetReplacementOptionsAsync(plan.MealPlanId, meal.MealPlanMealId, 1, null);
        Check(unverifiedOptions.Any(x => x.RecipeId == 6) && unverifiedOptions.All(x => !new[] {1,2,3,5,10}.Contains(x.RecipeId)),
            "planner replacement keeps unmatched unverified ingredients and rejects matches");
        var unverifiedPlan = await planner.RegenerateAsync(plan.MealPlanId, 1, new(null, null, "MAINTAIN"));
        Check(unverifiedPlan.Meals.Count == 21 && unverifiedPlan.Meals.All(x => !new[] {1,2,3,5,10}.Contains(x.RecipeId)),
            "planner still generates a complete plan with an unverified catalog using recorded allergies");
        plan = unverifiedPlan; // Regeneration replaces the persisted plan and returns its new ID.
        await Reject(() => planner.ReplaceMealAsync(plan.MealPlanId, unverifiedPlan.Meals.First().MealPlanMealId, 1, 2),
            "unverified matching tofu cannot bypass explicit replacement");
        Check(await db.Ingredients.AllAsync(i => !i.IsAllergenProfileVerified && i.AllergenEvidence == null),
            "filtering never relabels unverified ingredients as verified");

        db.Allergens.AddRange(new Allergen { AllergenId = 4, Name = "Nấm hương" }, new Allergen { AllergenId = 5, Name = "nấm" });
        Recipe(112, "Bò kho chay", Ingredient(112, "NẤM HƯƠNG khô", 1, false));
        Recipe(113, "Món nấm đùi gà", Ingredient(113, "nấm đùi gà", 1, false));
        await db.SaveChangesAsync();
        await selection.UpdateAsync(1, new([4]));
        Check(!(await Search()).Any(r => r.PostId == 112) && (await Search()).Any(r => r.PostId == 113), "saved shiitake name excludes unmapped recipe");
        await selection.UpdateAsync(1, new([5]));
        Check(!(await posts.GetAsync(null,null,null,null,null,false,1)).Any(r => r.PostId == 112 || r.PostId == 113)
            && (await Search()).Any(r => r.PostId == 6), "saved general mushroom keyword filters home and retains rice");
        Check(!(await Search(keyword: "Bò kho chay")).Any(), "exact dish search cannot bypass keyword exclusion");
        await selection.UpdateAsync(1, new([]));
        Check((await Search()).Any(r => r.PostId == 112), "removing mushroom selection restores unmapped recipe immediately");
        await selection.UpdateAsync(1, new([1]));

        foreach (var ingredient in await db.Ingredients.ToListAsync()) await catalog.ReviewAllergensAsync(ingredient.IngredientId, new([1], true, "Isolated test composition"), default);
        Check((await Search()).Count == 0 && (await posts.GetAsync(null,null,null,null,null,false,1)).Count == 0, "empty home/search results do not fall back");
        var emptyPlan = await planner.GenerateAsync(1, new(null, null, "MAINTAIN"));
        Check(emptyPlan.Meals.Count == 0 && emptyPlan.MealPlanId == 0 && await db.MealPlans.CountAsync() == 1, "empty planner result does not store a dummy plan");
        var emptyRegeneration = await planner.RegenerateAsync(plan.MealPlanId, 1, new(null, null, "MAINTAIN"));
        Check(emptyRegeneration.Meals.Count == 0 && await db.MealPlans.CountAsync() == 1 && await db.MealPlanMeals.CountAsync() == 21,
            "empty regeneration preserves the existing plan and its meal rows");
        for (var id=10; id<110; id++) db.Allergens.Add(new Allergen { AllergenId=id, Name="Test allergen "+id }); await db.SaveChangesAsync();
        Check((await selection.UpdateAsync(2,new(Enumerable.Range(10,100).ToList()))).Allergens.Count == 100, "no arbitrary selection limit");

        db.Allergens.AddRange(new Allergen { AllergenId = 200, Name = "Nấm" }, new Allergen { AllergenId = 201, Name = "Đậu phộng" });
        var mushroom = Ingredient(200, "NẤM HƯƠNG tươi", 1, false);
        var kingOyster = Ingredient(201, "nam-dui-ga", 1, true);
        var mushroomSeasoning = Ingredient(202, "Hạt nêm nấm", 1, false);
        var potato = Ingredient(203, "Khoai tây", 1, false);
        var peanuts = Ingredient(204, "Đậu phộng rang", 1, false);
        var notMushroom = Ingredient(205, "Rau Namur", 1, false);
        Recipe(200, "Bò Kho Chay", mushroom);
        Recipe(201, "Món áp chảo", kingOyster, "VIDEO");
        Recipe(202, "Canh rau", mushroomSeasoning);
        Recipe(203, "Khoai hấp", potato);
        Recipe(204, "Món đậu", peanuts);
        Recipe(205, "Rau luộc", notMushroom);
        Recipe(206, "Nấm chỉ nằm trong tiêu đề", potato);
        await db.SaveChangesAsync();
        await selection.UpdateAsync(1, new([200]));
        var mushroomResults = await Search();
        Check(!mushroomResults.Any(x => new[] {200,201,202}.Contains(x.PostId)) &&
            mushroomResults.Any(x => x.PostId == 203) && mushroomResults.Any(x => x.PostId == 205),
            "mushroom preference excludes exact/broad/accentless ingredient names without emptying unrelated recipes");
        Check(mushroomResults.Any(x => x.PostId == 206), "keyword filtering uses ingredients, not dish titles");
        Check((await Search(keyword: "Bò Kho Chay")).Count == 0, "exact title cannot bypass mushroom ingredient keywords");
        Check((await posts.GetAsync(null,null,null,null,null,false,1)).Select(x => x.PostId).Order()
            .SequenceEqual(mushroomResults.Select(x => x.PostId).Order()), "keyword home/search consistency includes recipe videos");
        await selection.UpdateAsync(2, new([]));
        Check((await Search(2)).Any(x => x.PostId == 200), "keyword exclusions stay isolated per account");
        await selection.UpdateAsync(1, new([200,201]));
        Check(!(await Search()).Any(x => new[] {200,201,202,204}.Contains(x.PostId)) &&
            (await Search()).Any(x => x.PostId == 203), "multiple keyword preferences use ANY match");
        await selection.UpdateAsync(1, new([201]));
        Check((await Search()).Any(x => x.PostId == 200) && !(await Search()).Any(x => x.PostId == 204),
            "removing mushroom preference immediately restores mushroom dishes");
        await selection.UpdateAsync(1, new([200]));
        db.PostRatings.Add(new PostRating { PostId = 200, UserId = 1, Rating = 5 });
        await db.SaveChangesAsync();
        Check((await posts.GetAsync(null,null,null,null,null,false,1,1,sort: "rating")).Single().PostId != 200,
            "keyword exclusion happens before rating limit");
        var mushroomFilter = await PersonalRecipeFilter.LoadAsync(db, 1, default);
        using (var mysql = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql("Server=localhost;Database=not_used;User=test;Password=test", new MySqlServerVersion(new Version(8,0,0))).Options))
        {
            var sql = mushroomFilter.Apply(mysql.Recipes).Take(1).ToQueryString();
            Check(sql.Contains("IngredientId") && sql.Contains("LIMIT") && mushroomFilter.NameMatchedIngredientIds.Contains(200),
                "keyword ingredient IDs translate into MySQL filtering before limit");
        }
        var keywordPlan = await planner.GenerateAsync(1, new(null,null,"MAINTAIN"));
        Check(keywordPlan.Meals.Count == 21 && keywordPlan.Meals.All(x => !new[] {200,201,202}.Contains(x.RecipeId)),
            "planner generation excludes mushroom keywords and keeps other recipes");
        var keywordMeal = keywordPlan.Meals.First();
        var keywordOptions = await planner.GetReplacementOptionsAsync(keywordPlan.MealPlanId, keywordMeal.MealPlanMealId, 1, null);
        Check(keywordOptions.Count > 0 && keywordOptions.All(x => !new[] {200,201,202}.Contains(x.RecipeId)),
            "planner replacement options use keyword exclusions");
        try
        {
            await planner.ReplaceMealAsync(keywordPlan.MealPlanId, keywordMeal.MealPlanMealId, 1, 200);
            throw new Exception("Keyword-blocked recipe replacement accepted");
        }
        catch (ServiceException error) when (error.StatusCode == 422) { Check(true, "explicit mushroom replacement rejected with 422"); }
        related = await chat.SendAsync(session.ChatSessionId, 2, "ADMIN", null, new("Bò Kho Chay"));
        Check(related.RelatedRecipes.Any(x => x.RecipeId == 200), "account without mushroom selection can discover mushroom recipe");
        await selection.UpdateAsync(2, new([200]));
        related = await chat.SendAsync(session.ChatSessionId, 2, "ADMIN", null, new("Bò Kho Chay"));
        Check(related.RelatedRecipes.All(x => !new[] {200,201,202}.Contains(x.RecipeId)), "chat suggestions obey mushroom keywords");
        var keywordBlocked = await matcher.FindUnsafeAsync(await db.Allergens.Where(x => x.AllergenId == 200).ToListAsync(),
            await db.Ingredients.ToListAsync());
        Check(keywordBlocked.Contains(200) && keywordBlocked.Contains(201) && keywordBlocked.Contains(202) && !keywordBlocked.Contains(203),
            "compatibility matcher agrees with query keyword matching");
        Check(AllergenKeywordRules.Matches(["Nấm"], "Nấm hương", false) &&
            AllergenKeywordRules.Matches(["Nấm"], "  NẤM, đùi gà ", false) &&
            !AllergenKeywordRules.Matches(["Nấm hương"], "Namur", false) &&
            !AllergenKeywordRules.Matches(["Đậu phộng"], "Đậu gà", false),
            "normalization handles decomposed accents and word boundaries without arbitrary token widening");
        await selection.UpdateAsync(1, new([]));
        Check((await Search()).Any(x => x.PostId == 200) && (await Search()).Any(x => x.PostId == 204),
            "clearing allergies restores keyword-excluded recipes");

        Console.WriteLine($"SOURCE ALLERGEN TESTS: {checks} passed; isolated database only.");
    }
    sealed class Ai : IAiChatService { public Task<ChatbotReply> ReplyAsync(ChatbotRequest request, CancellationToken ct=default) => Task.FromResult(new ChatbotReply("Use the verified suggestions.", [])); }
    sealed class Knowledge : IKnowledgeBaseService { public KnowledgeBaseMatch FindRelevant(string question) => new("", []); }
    sealed class Events : ICommunityEventPublisher { public Task PublishAsync(CommunityEvent e, CancellationToken ct=default) => Task.CompletedTask; }
    sealed class NotificationSink : INotificationEventPublisher { public Task SendAsync(int id, UserNotificationResponse n, CancellationToken ct=default) => Task.CompletedTask; }
}
