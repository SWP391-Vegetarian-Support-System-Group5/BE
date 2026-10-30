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
    IAllergenIngredientMatcher allergenMatcher) : IMealPlanService
{
    private sealed record MealCandidate(int Id);
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
        cache.Set(cacheKey, regenCount + 1, TimeSpan.FromDays(1));
        return replacement;
    }

    private async Task<MealPlanResponse> GeneratePlanCoreAsync(int userId, GenerateMealPlanRequest request, bool isRegenerate, CancellationToken ct)
    {
        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType).Include(x => x.UserAllergens).ThenInclude(x => x.Allergen).SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new ServiceException("User not found.", 404);

        var (bmi, bmiCategory) = MealPlannerBmi.Calculate(user.Profile);
        var bmiCode = MealPlannerBmi.RequiredCode(bmiCategory);
        if (user.Profile?.DietType is null) throw new ServiceException("Choose your vegetarian type in your profile before creating a meal plan.", 400);


        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var blockedGroups = BlockedIngredientGroups(user.UserAllergens);
        var dietTypeId = user.Profile?.DietTypeId;

        var allValidCandidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .Where(x => x.BMITypes.Any(m => m.BMIType.Code == bmiCode))
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

        if (filteredCandidates.Count == 0) throw new ServiceException("No recipes have been assigned to your BMI group that also match your vegetarian type and allergies. Please try again after an administrator reviews the recipes.", 400);

        var preferences = IngredientPreferences.Parse(request.AvailableIngredients);
        var scoredCandidates = filteredCandidates
            .Select(recipe => new { Recipe = recipe, Matches = IngredientPreferences.MatchCount(recipe.Ingredients.Select(i => i.Ingredient.Name), preferences) })
            .ToList();
        var randomFallback = preferences.Length > 0 && scoredCandidates.All(item => item.Matches == 0);
        var candidates = scoredCandidates
            .OrderByDescending(item => item.Matches)
            .ThenByDescending(item => item.Matches / (double)Math.Max(1, item.Recipe.Ingredients.Count))
            .ThenBy(item => randomFallback ? Random.Shared.Next() : item.Recipe.PostId)
            .Take(45)
            .Select(item => new MealCandidate(item.Recipe.PostId))
            .ToList();
        if (candidates.Count == 0) throw new ServiceException("No recipes are suitable for your diet/allergies.", 400);

        var selectedMeals = BuildLocalMealPlan(candidates);

        var plan = new MealPlan
        {
            UserId = userId,
            StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date,
            AvailableIngredients = request.AvailableIngredients?.Trim(),
            BMI = bmi
        };
        foreach (var meal in selectedMeals)
        {
            plan.Meals.Add(new MealPlanMeal { DayNumber = meal.Day, MealType = meal.MealType, RecipeId = meal.RecipeId });
        }
        await mealPlans.AddAsync(plan, ct);
        await mealPlans.SaveChangesAsync(ct);

        return await GetByIdAsync(plan.MealPlanId, userId, "USER", ct);
    }

    private static List<AiMealResponse> BuildLocalMealPlan(IReadOnlyList<MealCandidate> candidates)
    {
        if (candidates.Count == 0) throw new ServiceException("The system does not have any suitable recipes to create a meal plan.", 400);
        var result = new List<AiMealResponse>(21);
        var mealTypes = new[] { "BREAKFAST", "LUNCH", "DINNER" };
        var index = 0;
        for (var day = 1; day <= 7; day++)
        {
            foreach (var mealType in mealTypes)
            {
                result.Add(new AiMealResponse(day, mealType, candidates[index % candidates.Count].Id));
                index++;
            }
        }
        return result;
    }

    public async Task<MealPlanResponse> ReplaceMealAsync(int mealPlanId, int mealPlanMealId, int userId, CancellationToken ct = default)
    {
        string cacheKey = $"MealPlan_Replace_{mealPlanId}";
        int replaceCount = cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            return 0;
        });

        if (replaceCount >= 5)
            throw new ServiceException("You have reached the meal replacement limit (5 times/week).", 429);

        var plan = await mealPlans.Query().Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.BMITypes).ThenInclude(x => x.BMIType)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.DietCompatibilities)
            .SingleOrDefaultAsync(x => x.MealPlanId == mealPlanId && x.UserId == userId, ct) ?? throw new ServiceException("Meal plan not found.", 404);
        var mealToReplace = plan.Meals.FirstOrDefault(x => x.MealPlanMealId == mealPlanMealId) ?? throw new ServiceException("Meal not found in plan.", 404);

        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType).Include(x => x.UserAllergens).ThenInclude(x => x.Allergen).SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new ServiceException("User not found.", 404);
        var planUnsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(),
            plan.Meals.SelectMany(x => x.Recipe.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);
        if (!IsPlanSuitable(plan, user, planUnsafeIds)) throw new ServiceException("This meal plan no longer matches your profile. Generate a new plan before replacing meals.", 409);
        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var blockedGroups = BlockedIngredientGroups(user.UserAllergens);
        var dietTypeId = user.Profile?.DietTypeId;
        var preferences = IngredientPreferences.Parse(plan.AvailableIngredients);
        var (_, bmiCategory) = MealPlannerBmi.Calculate(user.Profile);
        var bmiCode = MealPlannerBmi.RequiredCode(bmiCategory);
        if (user.Profile?.DietType is null) throw new ServiceException("Choose your vegetarian type in your profile before creating a meal plan.", 400);

        var currentRecipeIdsInPlan = plan.Meals.Select(x => x.RecipeId).ToHashSet();

        var replacementCandidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED" && x.PostId != mealToReplace.RecipeId)
            .Where(x => x.BMITypes.Any(m => m.BMIType.Code == bmiCode))
            .Where(x => !dietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == dietTypeId.Value && d.IsCompatible))
            .ToListAsync(ct);
        var replacementUnsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(),
            replacementCandidates.SelectMany(x => x.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);
        var scoredReplacements = replacementCandidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup)))
            .Where(x => !dietTypeId.HasValue || VegetarianDietRules.GetCompatibleDietTypes(x.Ingredients.Select(i => i.Ingredient.Category.DietaryGroup)).Contains(user.Profile?.DietType?.Name ?? ""))
            .Where(x => IsAllergySafe(x, userAllergenIds, blockedGroups, user.UserAllergens, replacementUnsafeIds))
            .Select(recipe => new { Recipe = recipe, Matches = IngredientPreferences.MatchCount(recipe.Ingredients.Select(i => i.Ingredient.Name), preferences) })
            .ToList();
        var randomReplacementFallback = preferences.Length > 0 && scoredReplacements.All(item => item.Matches == 0);
        var replacement = scoredReplacements
            .OrderByDescending(item => item.Matches)
            .ThenBy(item => currentRecipeIdsInPlan.Contains(item.Recipe.PostId))
            .ThenByDescending(item => item.Matches / (double)Math.Max(1, item.Recipe.Ingredients.Count))
            .ThenBy(item => randomReplacementFallback ? Random.Shared.Next() : item.Recipe.PostId)
            .Select(item => item.Recipe)
            .FirstOrDefault();

        if (replacement == null) throw new ServiceException("No suitable meal found for replacement.", 404);

        mealToReplace.RecipeId = replacement.PostId;
        await mealPlanMeals.SaveChangesAsync(ct);

        cache.Set(cacheKey, replaceCount + 1, TimeSpan.FromDays(7));
        return await GetByIdAsync(mealPlanId, userId, "USER", ct);
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
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.BMITypes).ThenInclude(x => x.BMIType)
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

            return new MealPlanResponse(
                plan.MealPlanId,
                plan.StartDate,
                plan.BMI,
                bmiCat,
                plan.Meals.OrderBy(m => m.DayNumber).Select(m => new MealPlanMealResponse(m.MealPlanMealId, m.DayNumber, m.MealType, m.RecipeId, m.Recipe.RecipeName)).ToList()
            );
        }).ToList();
    }

    private static bool IsPlanSuitable(MealPlan plan, User user, IReadOnlySet<int> unsafeIngredientIds)
    {
        if (user.Profile?.DietType is null) return false;
        string bmiCode;
        try { bmiCode = MealPlannerBmi.RequiredCode(MealPlannerBmi.Calculate(user.Profile).Category); }
        catch (ServiceException) { return false; }
        var allergens = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var blockedGroups = BlockedIngredientGroups(user.UserAllergens);
        return plan.Meals.Count == 21 && plan.Meals.All(meal =>
            meal.Recipe.Post.Status == "PUBLISHED" &&
            meal.Recipe.BMITypes.Any(x => x.BMIType.Code == bmiCode) &&
            meal.Recipe.DietCompatibilities.Any(x => x.DietTypeId == user.Profile.DietTypeId && x.IsCompatible) &&
            VegetarianDietRules.GetCompatibleDietTypes(meal.Recipe.Ingredients.Select(x => x.Ingredient.Category.DietaryGroup)).Contains(user.Profile.DietType.Name) &&
            IsAllergySafe(meal.Recipe, allergens, blockedGroups, user.UserAllergens, unsafeIngredientIds));
    }
    public async Task<MealPlanResponse> GetByIdAsync(int id, int userId, string role, CancellationToken ct = default)
    {
        var plan = await mealPlans.Query().Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.BMITypes).ThenInclude(x => x.BMIType)
            .Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.DietCompatibilities)
            .SingleOrDefaultAsync(x => x.MealPlanId == id, ct) ?? throw new ServiceException("Meal plan was not found.", 404);
        if (plan.UserId != userId && role != "ADMIN") throw new ServiceException("Forbidden", 403);
        if (role != "ADMIN")
        {
            var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType)
                .Include(x => x.UserAllergens).ThenInclude(x => x.Allergen)
                .SingleAsync(x => x.UserId == userId, ct);
            var unsafeIds = await allergenMatcher.FindUnsafeAsync(user.UserAllergens.Select(x => x.Allergen).ToArray(),
                plan.Meals.SelectMany(x => x.Recipe.Ingredients).Select(x => x.Ingredient).DistinctBy(x => x.IngredientId).ToArray(), ct);
            if (!IsPlanSuitable(plan, user, unsafeIds)) throw new ServiceException("This meal plan no longer matches your profile. Generate a new plan.", 409);
        }

        string? bmiCat = null;
        if (plan.BMI.HasValue) bmiCat = MealPlannerBmi.Category(plan.BMI.Value);

        return new MealPlanResponse(plan.MealPlanId, plan.StartDate, plan.BMI, bmiCat, plan.Meals.OrderBy(m => m.DayNumber).Select(m => new MealPlanMealResponse(m.MealPlanMealId, m.DayNumber, m.MealType, m.RecipeId, m.Recipe.RecipeName)).ToList());
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
    }
}
