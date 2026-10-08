using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class MealPlanService(
    IRepository<User> users,
    IRepository<Recipe> recipes,
    IRepository<MealPlan> mealPlans,
    IRepository<MealPlanMeal> mealPlanMeals) : IMealPlanService
{
    public async Task<MealPlanResponse> GenerateAsync(int userId, GenerateMealPlanRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.Query().Include(x => x.Profile).Include(x => x.UserAllergens).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken) ?? throw new ServiceException("User was not found.", 404);
        var nutritionTarget = CalculateNutritionTarget(user.Profile);
        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var dietTypeId = user.Profile?.DietTypeId;
        var candidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .ToListAsync(cancellationToken);
        candidates = candidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.DietaryGroup)))
            .Where(x => x.CaloriesPerServing.HasValue)
            .Where(x => !dietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == dietTypeId.Value && d.IsCompatible))
            .Where(x => !x.Ingredients.Any(i => i.AllergenId.HasValue && userAllergenIds.Contains(i.AllergenId.Value)))
            .ToList();
        if (candidates.Count == 0) throw new ServiceException("No recipe with calorie data matches the user's diet type and allergens.", 400);

        var availableIngredients = ParseIngredients(request.AvailableIngredients);
        var plan = new MealPlan
        {
            UserId = userId,
            StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date,
            AvailableIngredients = request.AvailableIngredients?.Trim(),
            BMI = nutritionTarget.Bmi,
            BMR = nutritionTarget.Bmr,
            TDEE = nutritionTarget.Tdee,
            TargetCaloriesPerDay = nutritionTarget.TargetCalories,
            HealthGoal = user.Profile?.HealthGoal
        };
        await mealPlans.AddAsync(plan, cancellationToken); await mealPlans.SaveChangesAsync(cancellationToken);

        var mealTypes = new[] { "BREAKFAST", "LUNCH", "DINNER" };
        for (var day = 1; day <= 7; day++)
        {
            var selectedMeals = SelectDailyRecipes(candidates, availableIngredients, nutritionTarget, day);
            for (var mealIndex = 0; mealIndex < selectedMeals.Count; mealIndex++)
            {
                var selected = selectedMeals[mealIndex];
                await mealPlanMeals.AddAsync(new MealPlanMeal { MealPlanId = plan.MealPlanId, DayNumber = day, MealType = mealTypes[mealIndex], RecipeId = selected.PostId, PlannedCalories = selected.CaloriesPerServing }, cancellationToken);
            }
        }
        await mealPlanMeals.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(plan.MealPlanId, userId, "USER", cancellationToken);
    }

    public async Task<IReadOnlyCollection<MealPlanResponse>> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var plans = await BaseQuery().Where(x => x.UserId == userId).OrderByDescending(x => x.StartDate).ToListAsync(cancellationToken);
        return plans.Select(ToResponse).ToList();
    }

    public async Task<MealPlanResponse> GetByIdAsync(int id, int userId, string role, CancellationToken cancellationToken = default)
    {
        var plan = await BaseQuery().SingleOrDefaultAsync(x => x.MealPlanId == id, cancellationToken) ?? throw new ServiceException("Meal plan was not found.", 404);
        if (plan.UserId != userId && role != "ADMIN") throw new ServiceException("You do not have permission to view this meal plan.", 403);
        return ToResponse(plan);
    }

    public async Task DeleteAsync(int id, int userId, string role, CancellationToken cancellationToken = default)
    {
        var plan = await mealPlans.FindAsync(id) ?? throw new ServiceException("Meal plan was not found.", 404);
        if (plan.UserId != userId && role != "ADMIN") throw new ServiceException("You do not have permission to delete this meal plan.", 403);
        foreach (var meal in await mealPlanMeals.Query().Where(x => x.MealPlanId == id).ToListAsync(cancellationToken)) mealPlanMeals.Remove(meal);
        await mealPlanMeals.SaveChangesAsync(cancellationToken); mealPlans.Remove(plan); await mealPlans.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<MealPlan> BaseQuery() => mealPlans.Query().Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post);
    private static MealPlanResponse ToResponse(MealPlan x) => new(x.MealPlanId, x.StartDate, x.BMI, x.BMR, x.TDEE, x.TargetCaloriesPerDay, x.HealthGoal, x.Meals.OrderBy(m => m.DayNumber).ThenBy(MealTypeOrder).Select(m => new MealPlanMealResponse(m.DayNumber, m.MealType, m.RecipeId, m.Recipe.Post.Title, m.PlannedCalories)).ToList());
    private static HashSet<string> ParseIngredients(string? value) => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).ToHashSet();

    private static IReadOnlyList<Recipe> SelectDailyRecipes(IReadOnlyCollection<Recipe> candidates, IReadOnlySet<string> availableIngredients, NutritionTarget nutritionTarget, int day)
    {
        var pool = candidates
            .OrderByDescending(x => x.Ingredients.Count(i => availableIngredients.Contains(i.IngredientName.ToLowerInvariant())))
            .ThenBy(x => (x.PostId + day) % candidates.Count)
            .Take(30)
            .ToList();

        if (pool.Count < 3)
            throw new ServiceException("At least three recipes with calorie data are required to generate a daily meal plan.", 400);

        List<Recipe>? best = null;
        var bestScore = decimal.MaxValue;
        for (var first = 0; first < pool.Count - 2; first++)
        for (var second = first + 1; second < pool.Count - 1; second++)
        for (var third = second + 1; third < pool.Count; third++)
        {
            var selected = new[] { pool[first], pool[second], pool[third] };
            var totalCalories = selected.Sum(x => x.CaloriesPerServing ?? 0);
            var ingredientMatches = selected.Sum(x => x.Ingredients.Count(i => availableIngredients.Contains(i.IngredientName.ToLowerInvariant())));
            var score = ScoreDailySelection(totalCalories, ingredientMatches, nutritionTarget);
            if (score >= bestScore) continue;

            bestScore = score;
            best = selected.ToList();
        }

        return best!
            .OrderBy(x => (x.PostId + day) % 3)
            .ToList();
    }

    private static NutritionTarget CalculateNutritionTarget(UserProfile? profile)
    {
        if (profile is null)
            throw new ServiceException("Please complete your profile before generating a meal plan.", 400);
        if (!profile.HeightCm.HasValue || !profile.WeightKg.HasValue)
            throw new ServiceException("Please provide height and weight before generating a meal plan.", 400);

        var heightM = profile.HeightCm.Value / 100m;
        var bmi = Math.Round(profile.WeightKg.Value / (heightM * heightM), 2);
        if (string.IsNullOrWhiteSpace(profile.Sex) || !profile.BirthDate.HasValue || string.IsNullOrWhiteSpace(profile.ActivityLevel) || string.IsNullOrWhiteSpace(profile.HealthGoal))
            return new(bmi, null, null, null);

        var age = CalculateAge(profile.BirthDate.Value, DateTime.UtcNow.Date);
        if (age is < 13 or > 120)
            return new(bmi, null, null, null);

        var bmr = profile.Sex == "MALE"
            ? 10m * profile.WeightKg.Value + 6.25m * profile.HeightCm.Value - 5m * age + 5m
            : 10m * profile.WeightKg.Value + 6.25m * profile.HeightCm.Value - 5m * age - 161m;
        var tdee = bmr * ActivityFactor(profile.ActivityLevel);
        var targetCalories = profile.HealthGoal switch
        {
            "WEIGHT_LOSS" => tdee - 500m,
            "MUSCLE_GAIN" => tdee + 300m,
            _ => tdee
        };

        return new(Math.Round(bmi, 2), Math.Round(bmr, 2), Math.Round(tdee, 2), Math.Round(Math.Max(targetCalories, 1200m), 2));
    }

    private static decimal ScoreDailySelection(decimal totalCalories, int ingredientMatches, NutritionTarget nutritionTarget)
    {
        const decimal ingredientBonus = 100m;
        if (nutritionTarget.TargetCalories.HasValue)
            return Math.Abs(totalCalories - nutritionTarget.TargetCalories.Value) - ingredientMatches * ingredientBonus;

        return nutritionTarget.Bmi switch
        {
            < 18.5m => -totalCalories - ingredientMatches * ingredientBonus,
            >= 25m => totalCalories - ingredientMatches * ingredientBonus,
            _ => -ingredientMatches * ingredientBonus
        };
    }

    private static int CalculateAge(DateTime birthDate, DateTime today)
    {
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age)) age--;
        return age;
    }

    private static decimal ActivityFactor(string activityLevel) => activityLevel switch
    {
        "SEDENTARY" => 1.2m,
        "LIGHT" => 1.375m,
        "MODERATE" => 1.55m,
        "ACTIVE" => 1.725m,
        "VERY_ACTIVE" => 1.9m,
        _ => throw new ServiceException("Activity level is invalid.", 400)
    };

    private static int MealTypeOrder(MealPlanMeal meal) => meal.MealType switch
    {
        "BREAKFAST" => 0,
        "LUNCH" => 1,
        "DINNER" => 2,
        _ => 4
    };

    private sealed record NutritionTarget(decimal Bmi, decimal? Bmr, decimal? Tdee, decimal? TargetCalories);
}
