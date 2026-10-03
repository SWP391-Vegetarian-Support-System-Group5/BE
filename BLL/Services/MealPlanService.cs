using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public static class NutritionCalculationSettings
{
    public const decimal WeightLossCalorieDeficit = 500m;
    public const decimal MuscleGainCalorieSurplus = 300m;
}

public class MealPlanService(
    IRepository<User> users,
    IRepository<Recipe> recipes,
    IRepository<MealPlan> mealPlans,
    IRepository<MealPlanMeal> mealPlanMeals) : IMealPlanService
{
    public async Task<MealPlanResponse> GenerateAsync(int userId, GenerateMealPlanRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.Query().Include(x => x.UserAllergens).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken) ?? throw new ServiceException("User was not found.", 404);
        ValidateNutritionProfile(user);
        var bmi = Math.Round(user.WeightKg!.Value / ((user.HeightCm!.Value / 100m) * (user.HeightCm.Value / 100m)), 2);
        var bmr = CalculateBmr(user);
        var tdee = Math.Round(bmr * ActivityFactor(user.ActivityLevel!), 2);
        var targetCalories = CalculateTargetCalories(tdee, user.HealthGoal!);
        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var candidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .ToListAsync(cancellationToken);
        candidates = candidates
            .Where(x => !user.DietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == user.DietTypeId && d.IsCompatible))
            .Where(x => !x.Ingredients.Any(i => i.AllergenId.HasValue && userAllergenIds.Contains(i.AllergenId.Value)))
            .ToList();
        if (candidates.Count == 0) throw new ServiceException("No recipe matches the user's diet type and allergens.", 400);

        var availableIngredients = ParseIngredients(request.AvailableIngredients);
        var plan = new MealPlan { UserId = userId, StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date, HealthGoal = user.HealthGoal, AvailableIngredients = request.AvailableIngredients?.Trim(), BMI = bmi, BMR = bmr, TDEE = tdee, TargetCaloriesPerDay = targetCalories };
        await mealPlans.AddAsync(plan, cancellationToken); await mealPlans.SaveChangesAsync(cancellationToken);

        var allocations = new[] { ("BREAKFAST", 0.25m), ("LUNCH", 0.35m), ("DINNER", 0.30m), ("SNACK", 0.10m) };
        for (var day = 1; day <= 7; day++)
        {
            foreach (var (mealType, share) in allocations)
            {
                var selected = SelectRecipe(candidates, targetCalories * share, availableIngredients, day);
                await mealPlanMeals.AddAsync(new MealPlanMeal { MealPlanId = plan.MealPlanId, DayNumber = day, MealType = mealType, RecipeId = selected.PostId, PlannedCalories = selected.CaloriesPerServing ?? 0m }, cancellationToken);
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
    private static MealPlanResponse ToResponse(MealPlan x) => new(x.MealPlanId, x.StartDate, x.HealthGoal, x.BMI, x.BMR, x.TDEE, x.TargetCaloriesPerDay, x.Meals.OrderBy(m => m.DayNumber).ThenBy(m => m.MealType).Select(m => new MealPlanMealResponse(m.DayNumber, m.MealType, m.RecipeId, m.Recipe.Post.Title, m.PlannedCalories)).ToList());
    private static void ValidateNutritionProfile(User user)
    {
        if (user.Age is null or <= 0 || user.HeightCm is null or <= 0 || user.WeightKg is null or <= 0 || string.IsNullOrWhiteSpace(user.Sex) || string.IsNullOrWhiteSpace(user.ActivityLevel) || string.IsNullOrWhiteSpace(user.HealthGoal)) throw new ServiceException("Age, sex, height, weight, activity level, and health goal are required to generate a meal plan.");
    }
    private static decimal CalculateBmr(User user)
    {
        var baseValue = 10m * user.WeightKg!.Value + 6.25m * user.HeightCm!.Value - 5m * user.Age!.Value;
        return Math.Round(baseValue + (user.Sex!.Equals("MALE", StringComparison.OrdinalIgnoreCase) || user.Sex.Equals("M", StringComparison.OrdinalIgnoreCase) ? 5m : -161m), 2);
    }
    private static decimal ActivityFactor(string activityLevel) => FixedValues.ActivityFactor(activityLevel);
    private static decimal CalculateTargetCalories(decimal tdee, string goal) => Math.Round(FixedValues.HealthGoal(goal) switch { "WEIGHT_LOSS" => tdee - NutritionCalculationSettings.WeightLossCalorieDeficit, "MAINTENANCE" => tdee, "MUSCLE_GAIN" => tdee + NutritionCalculationSettings.MuscleGainCalorieSurplus, _ => throw new InvalidOperationException("Health goal validation failed.") }, 2);
    private static HashSet<string> ParseIngredients(string? value) => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
    private static Recipe SelectRecipe(IReadOnlyCollection<Recipe> candidates, decimal desiredCalories, IReadOnlySet<string> availableIngredients, int day) => candidates.OrderByDescending(x => x.Ingredients.Count(i => availableIngredients.Contains(i.IngredientName.ToLowerInvariant()))).ThenBy(x => Math.Abs((x.CaloriesPerServing ?? 0m) - desiredCalories)).ThenBy(x => Math.Abs((x.PostId % candidates.Count) - (day % candidates.Count))).First();
}
