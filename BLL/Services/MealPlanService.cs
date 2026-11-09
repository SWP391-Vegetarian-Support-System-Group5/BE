using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Data;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;

namespace BLL.Services;

public class MealPlanService(
    IRepository<User> users,
    IRepository<Recipe> recipes,
    IRepository<MealPlan> mealPlans,
    IRepository<MealPlanMeal> mealPlanMeals,
    IMemoryCache cache,
    VegetarianDbContext dbContext,
    IAllergenIngredientMatcher allergenMatcher,
    IImageStorage? imageStorage = null) : IMealPlanService
{
    private sealed record MealCandidate(int Id, decimal Calories, int PreferenceRank);
    private sealed record PlannedMeal(int Day, string MealType, int RecipeId, decimal Calories);
    private sealed record MealReplacementContext(MealPlan Plan, MealPlanMeal Meal, IReadOnlyList<Recipe> Candidates);
    public async Task<MealPlanResponse> GenerateAsync(int userId, GenerateMealPlanRequest request, CancellationToken ct = default)
    {
        return await GeneratePlanCoreAsync(userId, request, isRegenerate: false, ct);
    }
    public async Task<MealPlanResponse> RegenerateAsync(int id, int userId, GenerateMealPlanRequest request, CancellationToken ct = default)
    {
        string cacheKey = $"MealPlan_Regen_{userId}_{DateTime.UtcNow.Date:yyyyMMdd}";
        int regenCount = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
            return 0;
        });

        if (regenCount >= 3)
            throw new ServiceException("You have reached the meal plan regeneration limit (3 times/day). Please try again tomorrow.", 429);

        var oldPlan = await mealPlans.FindAsync(id) ?? throw new ServiceException("Meal plan not found.", 404);
        if (oldPlan.UserId != userId) throw new ServiceException("Forbidden", 403);

        // Keep the previous plan until its complete replacement has been saved.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(ct) : null;
        var replacement = await GeneratePlanCoreAsync(userId, request, isRegenerate: true, ct);
        var oldMeals = await mealPlanMeals.Query().Where(x => x.MealPlanId == id).ToListAsync(ct);
        foreach (var meal in oldMeals) mealPlanMeals.Remove(meal);
        mealPlans.Remove(oldPlan);
        await mealPlans.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        if (transaction is not null) await CleanupImagesAsync(oldMeals.Select(m => m.ImagePublicId));
        cache.Set(cacheKey, regenCount + 1, TimeSpan.FromDays(1));
        return replacement;
    }

    private async Task<MealPlanResponse> GeneratePlanCoreAsync(int userId, GenerateMealPlanRequest request, bool isRegenerate, CancellationToken ct)
    {
        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType).Include(x => x.UserAllergens).ThenInclude(x => x.Allergen).SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new ServiceException("User not found.", 404);

        var (bmi, bmiCategory) = MealPlannerBmi.Calculate(user.Profile);
        if (user.Profile?.DietType is null) throw new ServiceException("Choose your vegetarian type in your profile before creating a meal plan.", 400);
        var tdee = NutritionProfileRules.CalculateTdee(user.Profile, DateTime.Today)
            ?? throw new ServiceException("Complete sex, birth date, height, weight and activity level in your profile before creating a calorie-based meal plan.", 400);
        // A missing goal means "follow the saved profile goal". MAINTAIN is
        // still respected when the user deliberately selects it in the UI.
        var nutritionGoal = string.IsNullOrWhiteSpace(request.NutritionGoal)
            ? NutritionProfileRules.InferNutritionGoal(user.Profile)
            : NormalizeNutritionGoal(request.NutritionGoal);
        int? effectiveAdjustment = nutritionGoal == "MAINTAIN" ? null : request.CalorieAdjustment ?? 500;
        ValidateNutritionGoal(bmiCategory, nutritionGoal, effectiveAdjustment);
        var adjustment = nutritionGoal == "MAINTAIN" ? null : effectiveAdjustment;
        var targetCalories = nutritionGoal switch
        {
            "LOSE" => tdee - adjustment!.Value,
            "GAIN" => tdee + adjustment!.Value,
            _ => tdee
        };
        if (targetCalories < NutritionProfileRules.MinimumTargetCalories)
            throw new ServiceException("The selected calorie deficit would make the daily target too low. Choose a smaller deficit or update your profile.", 400);


        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var blockedGroups = BlockedIngredientGroups(user.UserAllergens);
        var dietTypeId = user.Profile?.DietTypeId;

        var allValidCandidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .Where(x => !dietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == dietTypeId.Value && d.IsCompatible))
            .ToListAsync(ct);

        var unsafeIngredientIds = await allergenMatcher.FindUnsafeAsync(
            user.UserAllergens.Select(x => x.Allergen).ToArray(),
            allValidCandidates.SelectMany(x => x.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);

        var filteredCandidates = allValidCandidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup)))
            .Where(x => !dietTypeId.HasValue || VegetarianDietRules.GetCompatibleDietTypes(x.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup)).Contains(user.Profile?.DietType?.Name ?? ""))
            .Where(x => IsAllergySafe(x, userAllergenIds, blockedGroups, user.UserAllergens, unsafeIngredientIds))
            .ToList();

        if (filteredCandidates.Count == 0) throw new ServiceException("Không có công thức nào khớp kiểu ăn chay và dị ứng của bạn. Vui lòng cập nhật hồ sơ hoặc thử lại sau.", 400);

        var calorieReadyCandidates = filteredCandidates
            .Where(recipe => recipe.CaloriesPerServing is > 0)
            .ToList();
        if (calorieReadyCandidates.Count == 0)
            throw new ServiceException("Các công thức phù hợp chưa có calories theo khẩu phần. Quản trị viên cần bổ sung calories trước khi tạo thực đơn.", 400);

        var preferences = IngredientPreferences.Parse(request.AvailableIngredients);
        var scoredCandidates = calorieReadyCandidates
            .Select(recipe => new { Recipe = recipe, Matches = IngredientPreferences.MatchCount(recipe.Ingredients.Select(i => i.Ingredient.Name), preferences) })
            .ToList();
        // When the user entered pantry ingredients, use matching recipes only.
        // Falling back to the full eligible catalog is intentional only when no
        // recipe contains any entered ingredient.
        var pantryMatches = preferences.Length > 0
            ? scoredCandidates.Where(item => item.Matches > 0).ToList()
            : [];
        var candidatePool = pantryMatches.Count > 0 ? pantryMatches : scoredCandidates;
        var randomFallback = preferences.Length > 0 && pantryMatches.Count == 0;
        var candidates = candidatePool
            .OrderByDescending(item => item.Matches)
            .ThenByDescending(item => item.Matches / (double)Math.Max(1, item.Recipe.Ingredients.Count))
            .ThenBy(item => randomFallback ? Random.Shared.Next() : item.Recipe.PostId)
            .Take(45)
            .Select((item, index) => new MealCandidate(item.Recipe.PostId, item.Recipe.CaloriesPerServing!.Value, index))
            .ToList();
        if (candidates.Count == 0) throw new ServiceException("No recipes are suitable for your diet/allergies.", 400);

        var selectedMeals = BuildLocalMealPlan(candidates, targetCalories);

        var plan = new MealPlan
        {
            UserId = userId,
            StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date,
            AvailableIngredients = request.AvailableIngredients?.Trim(),
            BMI = bmi,
            Tdee = tdee,
            TargetCaloriesPerDay = targetCalories,
            NutritionGoal = nutritionGoal,
            CalorieAdjustment = adjustment
        };
        foreach (var meal in selectedMeals)
        {
            plan.Meals.Add(new MealPlanMeal { DayNumber = meal.Day, MealType = meal.MealType, RecipeId = meal.RecipeId, PlannedCalories = meal.Calories });
        }
        await mealPlans.AddAsync(plan, ct);
        await mealPlans.SaveChangesAsync(ct);

        return await GetByIdAsync(plan.MealPlanId, userId, "USER", ct);
    }

    private static List<PlannedMeal> BuildLocalMealPlan(IReadOnlyList<MealCandidate> candidates, decimal targetCalories)
    {
        if (candidates.Count == 0) throw new ServiceException("The system does not have any suitable recipes to create a meal plan.", 400);
        var result = new List<PlannedMeal>(21);
        var mealTypes = new[] { "BREAKFAST", "LUNCH", "DINNER" };
        // Rotate through eligible recipes before reusing one. This still keeps each
        // day's calories close to target, while preventing one closest-calorie dish
        // from filling all 21 meal slots.
        var usageCounts = candidates.ToDictionary(candidate => candidate.Id, _ => 0);
        var index = 0;
        for (var day = 1; day <= 7; day++)
        {
            var remaining = targetCalories;
            var usedToday = new HashSet<int>();
            for (var mealIndex = 0; mealIndex < mealTypes.Length; mealIndex++)
            {
                var desired = remaining / (mealTypes.Length - mealIndex);
                var availableToday = candidates.Where(candidate => !usedToday.Contains(candidate.Id)).ToList();
                // If fewer than three recipes match the user's strict profile, a
                // repeat is unavoidable; only then allow it within the same day.
                var pool = availableToday.Count > 0 ? availableToday : candidates.ToList();
                var leastUsage = pool.Min(candidate => usageCounts[candidate.Id]);
                var choice = pool
                    .Where(candidate => usageCounts[candidate.Id] == leastUsage)
                    .OrderBy(candidate => Math.Abs(candidate.Calories - desired))
                    .ThenBy(candidate => candidate.PreferenceRank)
                    .ThenBy(candidate => Math.Abs((candidate.Id % candidates.Count) - (index % candidates.Count)))
                    .First();
                result.Add(new PlannedMeal(day, mealTypes[mealIndex], choice.Id, choice.Calories));
                usedToday.Add(choice.Id);
                usageCounts[choice.Id]++;
                remaining -= choice.Calories;
                index++;
            }
        }
        return result;
    }

    public Task<MealPlanResponse> ReplaceMealAsync(int mealPlanId, int mealPlanMealId, int userId, CancellationToken ct = default) =>
        ReplaceMealAsync(mealPlanId, mealPlanMealId, userId, null, ct);

    public async Task<MealPlanResponse> ReplaceMealAsync(int mealPlanId, int mealPlanMealId, int userId, int? recipeId, CancellationToken ct = default)
    {
        var today = VietnamTime.Now.Date;
        var resetAt = new DateTimeOffset(today.AddDays(1), TimeSpan.FromHours(7));
        string cacheKey = $"MealPlan_Replace_v3_{mealPlanId}_{today:yyyyMMdd}";
        int replaceCount = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpiration = resetAt;
            return 0;
        });
        if (replaceCount >= 5)
            throw new ServiceException("Bạn đã dùng hết 5 lượt đổi món hôm nay. Lượt đổi món sẽ được làm mới vào 00:00 ngày mai.", 429);

        var context = await GetReplacementContextAsync(mealPlanId, mealPlanMealId, userId, ct);
        var replacement = recipeId.HasValue
            ? context.Candidates.FirstOrDefault(recipe => recipe.PostId == recipeId.Value)
            : context.Candidates.FirstOrDefault();
        if (replacement is null)
            throw new ServiceException(recipeId.HasValue
                ? "Món bạn chọn không còn phù hợp với kiểu ăn chay, dị ứng hoặc nguyên liệu trong hồ sơ."
                : "Không có món phù hợp để thay thế.", recipeId.HasValue ? 422 : 404);

        context.Meal.RecipeId = replacement.PostId;
        context.Meal.PlannedCalories = replacement.CaloriesPerServing;
        await mealPlanMeals.SaveChangesAsync(ct);
        cache.Set(cacheKey, replaceCount + 1, resetAt);
        return await GetByIdAsync(mealPlanId, userId, "USER", ct);
    }

    public async Task<IReadOnlyCollection<MealPlanRecipeOptionResponse>> GetReplacementOptionsAsync(int mealPlanId, int mealPlanMealId, int userId, string? query, CancellationToken ct = default)
    {
        var context = await GetReplacementContextAsync(mealPlanId, mealPlanMealId, userId, ct);
        var term = query?.Trim();
        return context.Candidates
            .Where(recipe => string.IsNullOrWhiteSpace(term) || recipe.RecipeName.Contains(term, StringComparison.OrdinalIgnoreCase) || recipe.Post.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(60)
            .Select(recipe => new MealPlanRecipeOptionResponse(recipe.PostId, recipe.RecipeName, recipe.ImageUrl ?? recipe.Post.ThumbnailUrl, recipe.CaloriesPerServing!.Value, recipe.PrepMinutes, recipe.CookMinutes))
            .ToList();
    }

    private async Task<MealReplacementContext> GetReplacementContextAsync(int mealPlanId, int mealPlanMealId, int userId, CancellationToken ct)
    {
        var plan = await mealPlans.Query().Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.DietCompatibilities)
            .SingleOrDefaultAsync(x => x.MealPlanId == mealPlanId && x.UserId == userId, ct) ?? throw new ServiceException("Meal plan not found.", 404);
        var mealToReplace = plan.Meals.FirstOrDefault(x => x.MealPlanMealId == mealPlanMealId) ?? throw new ServiceException("Meal not found in plan.", 404);
        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType).Include(x => x.UserAllergens).ThenInclude(x => x.Allergen).SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new ServiceException("User not found.", 404);
        if (user.Profile?.DietType is null) throw new ServiceException("Choose your vegetarian type in your profile before creating a meal plan.", 400);
        var planUnsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(), plan.Meals.SelectMany(x => x.Recipe.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);
        if (!IsPlanSuitable(plan, user, planUnsafeIds)) throw new ServiceException("This meal plan no longer matches your profile. Generate a new plan before replacing meals.", 409);

        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var blockedGroups = BlockedIngredientGroups(user.UserAllergens);
        var preferences = IngredientPreferences.Parse(plan.AvailableIngredients);
        var currentRecipeIdsInPlan = plan.Meals.Select(x => x.RecipeId).ToHashSet();
        var replacementCandidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED" && x.PostId != mealToReplace.RecipeId)
            .Where(x => x.CaloriesPerServing.HasValue && x.CaloriesPerServing > 0)
            .Where(x => x.DietCompatibilities.Any(d => d.DietTypeId == user.Profile.DietTypeId && d.IsCompatible))
            .ToListAsync(ct);
        var replacementUnsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(), replacementCandidates.SelectMany(x => x.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);
        var scored = replacementCandidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup)))
            .Where(x => VegetarianDietRules.GetCompatibleDietTypes(x.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup)).Contains(user.Profile.DietType!.Name))
            .Where(x => IsAllergySafe(x, userAllergenIds, blockedGroups, user.UserAllergens, replacementUnsafeIds))
            .Select(recipe => new { Recipe = recipe, Matches = IngredientPreferences.MatchCount(recipe.Ingredients.Select(i => i.Ingredient.Name), preferences) })
            .ToList();
        var pantryMatches = preferences.Length > 0 ? scored.Where(item => item.Matches > 0).ToList() : [];
        var pool = pantryMatches.Count > 0 ? pantryMatches : scored;
        var randomFallback = preferences.Length > 0 && pantryMatches.Count == 0;
        var caloriesAlreadyPlannedToday = plan.Meals
            .Where(meal => meal.DayNumber == mealToReplace.DayNumber && meal.MealPlanMealId != mealToReplace.MealPlanMealId)
            .Sum(meal => meal.PlannedCalories ?? meal.Recipe.CaloriesPerServing ?? 0m);
        var desiredMealCalories = plan.TargetCaloriesPerDay.HasValue
            ? Math.Max(100m, plan.TargetCaloriesPerDay.Value - caloriesAlreadyPlannedToday)
            : mealToReplace.PlannedCalories ?? mealToReplace.Recipe.CaloriesPerServing ?? 0m;
        var candidates = pool.OrderByDescending(item => item.Matches)
            .ThenBy(item => currentRecipeIdsInPlan.Contains(item.Recipe.PostId))
            .ThenByDescending(item => item.Matches / (double)Math.Max(1, item.Recipe.Ingredients.Count))
            .ThenBy(item => Math.Abs(item.Recipe.CaloriesPerServing!.Value - desiredMealCalories))
            .ThenBy(item => randomFallback ? Random.Shared.Next() : item.Recipe.PostId)
            .Select(item => item.Recipe).ToList();
        return new MealReplacementContext(plan, mealToReplace, candidates);
    }

    private static HashSet<string> BlockedIngredientGroups(IEnumerable<UserAllergen> allergies)
    {
        var names = allergies.Select(x => x.Allergen.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        if (names.Overlaps(["Egg", "Eggs", "Trứng"])) blocked.Add(VegetarianDietRules.Egg);
        if (names.Overlaps(["Milk", "Dairy", "Sữa"])) blocked.Add(VegetarianDietRules.Dairy);
        return blocked;
    }

    private static bool IsAllergySafe(Recipe recipe, HashSet<int> allergenIds, HashSet<string> blockedGroups, IEnumerable<UserAllergen> userAllergens, IReadOnlySet<int> unsafeIngredientIds) =>
        !recipe.Ingredients.Any(item =>
            unsafeIngredientIds.Contains(item.IngredientId) ||
            item.Ingredient.AllergenId is int id && allergenIds.Contains(id) ||
            blockedGroups.Contains(item.Ingredient.Category.DietaryGroup) ||
            userAllergens.Any(allergy =>
                // A plant milk is not animal dairy even though its name contains "milk".
                !(item.Ingredient.Category.DietaryGroup == VegetarianDietRules.Plant &&
                  allergy.Allergen.Name.Equals("Milk", StringComparison.OrdinalIgnoreCase)) &&
                AllergenLexicon.Terms(allergy.Allergen).Any(term => AllergenLexicon.ContainsTerm(item.Ingredient.Name, term))));

    public async Task<IReadOnlyCollection<MealPlanResponse>> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType)
            .Include(x => x.UserAllergens).ThenInclude(x => x.Allergen)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken) ?? throw new ServiceException("User not found.", 404);
        var plans = await mealPlans.Query()
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.DietCompatibilities)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

        var unsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(),
            plans.SelectMany(x => x.Meals).SelectMany(x => x.Recipe.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), cancellationToken);
        return plans.Where(plan => IsPlanSuitable(plan, user, unsafeIds)).Select(plan =>
        {
            string? bmiCat = null;
            if (plan.BMI.HasValue) bmiCat = MealPlannerBmi.Category(plan.BMI.Value);

            return ToResponse(plan, bmiCat, user.Profile);
        }).ToList();
    }

    private static bool IsPlanSuitable(MealPlan plan, User user, IReadOnlySet<int> unsafeIngredientIds)
    {
        if (user.Profile?.DietType is null) return false;
        var allergens = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var blockedGroups = BlockedIngredientGroups(user.UserAllergens);
        return plan.Meals.Count == 21 && plan.Meals.All(meal =>
            meal.Recipe.Post.Status == "PUBLISHED" &&
            meal.Recipe.DietCompatibilities.Any(x => x.DietTypeId == user.Profile.DietTypeId && x.IsCompatible) &&
            VegetarianDietRules.GetCompatibleDietTypes(meal.Recipe.Ingredients.Select(x => x.Ingredient.Category.DietaryGroup)).Contains(user.Profile.DietType.Name) &&
            IsAllergySafe(meal.Recipe, allergens, blockedGroups, user.UserAllergens, unsafeIngredientIds));
    }
    public async Task<MealPlanResponse> GetByIdAsync(int id, int userId, string role, CancellationToken ct = default)
    {
        var plan = await mealPlans.Query().Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.DietCompatibilities)
            .SingleOrDefaultAsync(x => x.MealPlanId == id, ct) ?? throw new ServiceException("Meal plan was not found.", 404);
        if (plan.UserId != userId && role != "ADMIN") throw new ServiceException("Forbidden", 403);
        UserProfile? profile = null;
        if (role != "ADMIN")
        {
            var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType)
                .Include(x => x.UserAllergens).ThenInclude(x => x.Allergen)
                .SingleAsync(x => x.UserId == userId, ct);
            var unsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(),
                plan.Meals.SelectMany(x => x.Recipe.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);
            if (!IsPlanSuitable(plan, user, unsafeIds)) throw new ServiceException("This meal plan no longer matches your profile. Generate a new plan.", 409);
            profile = user.Profile;
        }

        string? bmiCat = null;
        if (plan.BMI.HasValue) bmiCat = MealPlannerBmi.Category(plan.BMI.Value);

        return ToResponse(plan, bmiCat, profile);
    }

    private static MealPlanResponse ToResponse(MealPlan plan, string? bmiCategory, UserProfile? profile)
    {
        var height = profile?.HeightCm;
        var range = height is > 0 ? NutritionProfileRules.HealthyWeightRange(height.Value) : ((decimal MinimumKg, decimal MaximumKg)?)null;
        var weeklyEstimate = NutritionProfileRules.EstimateWeeksToGoal(profile, plan.CalorieAdjustment);
        return new MealPlanResponse(
            plan.MealPlanId,
            plan.StartDate,
            plan.BMI,
            bmiCategory,
            plan.Meals.OrderBy(m => m.DayNumber).Select(m => new MealPlanMealResponse(m.MealPlanMealId, m.DayNumber, m.MealType, m.RecipeId, m.Recipe.RecipeName, m.PlannedCalories ?? m.Recipe.CaloriesPerServing)).ToList(),
            plan.Tdee,
            plan.TargetCaloriesPerDay,
            plan.NutritionGoal,
            plan.CalorieAdjustment,
            profile?.GoalWeightKg,
            range?.MinimumKg,
            range?.MaximumKg,
            weeklyEstimate);
    }

    private static string NormalizeNutritionGoal(string? value)
    {
        var goal = string.IsNullOrWhiteSpace(value) ? "MAINTAIN" : value.Trim().ToUpperInvariant();
        if (goal is not ("MAINTAIN" or "LOSE" or "GAIN"))
            throw new ServiceException("Nutrition goal must be MAINTAIN, LOSE or GAIN.", 400);
        return goal;
    }

    private static void ValidateNutritionGoal(string? bmiCategory, string goal, int? adjustment)
    {
        if (goal == "MAINTAIN" && adjustment is not null)
            throw new ServiceException("A calorie adjustment is only used for weight loss or gain.", 400);
        if (goal != "MAINTAIN" && (adjustment is null || adjustment < 500 || adjustment > 1000))
            throw new ServiceException("Choose a calorie adjustment between 500 and 1000 kcal/day.", 400);
        if (bmiCategory == "Underweight" && goal == "LOSE")
            throw new ServiceException("Weight-loss meal plans are not available for the underweight BMI group.", 400);
        if (bmiCategory == "Obese" && goal == "GAIN")
            throw new ServiceException("Weight-gain meal plans are not available for the high-BMI group.", 400);
    }
    public async Task DeleteAsync(int id, int userId, string role, CancellationToken cancellationToken = default)
    {
        var plan = await mealPlans.FindAsync(id) ?? throw new ServiceException("Meal plan was not found.", 404);

        if (plan.UserId != userId && role != "ADMIN")
            throw new ServiceException("You do not have permission to delete this meal plan.", 403);

        var meals = await mealPlanMeals.Query().Where(x => x.MealPlanId == id).ToListAsync(cancellationToken);
        foreach (var meal in meals)
        {
            mealPlanMeals.Remove(meal);
        }

        await mealPlanMeals.SaveChangesAsync(cancellationToken);
        mealPlans.Remove(plan);
        await mealPlans.SaveChangesAsync(cancellationToken);
        await CleanupImagesAsync(meals.Select(m => m.ImagePublicId));
    }

    private async Task CleanupImagesAsync(IEnumerable<string?> ids)
    {
        if (imageStorage is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
            await imageStorage.DeleteAsync(id!, timeout.Token);
    }
}
