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
        var user = await users.Query().Include(x => x.UserAllergens).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken) ?? throw new ServiceException("User was not found.", 404);
        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var candidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .ToListAsync(cancellationToken);
        candidates = candidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.DietaryGroup)))
            .Where(x => !user.DietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == user.DietTypeId && d.IsCompatible))
            .Where(x => !x.Ingredients.Any(i => i.AllergenId.HasValue && userAllergenIds.Contains(i.AllergenId.Value)))
            .ToList();
        if (candidates.Count == 0) throw new ServiceException("No recipe matches the user's diet type and allergens.", 400);

        var availableIngredients = ParseIngredients(request.AvailableIngredients);
        var plan = new MealPlan { UserId = userId, StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date, AvailableIngredients = request.AvailableIngredients?.Trim() };
        await mealPlans.AddAsync(plan, cancellationToken); await mealPlans.SaveChangesAsync(cancellationToken);

        var mealTypes = new[] { "BREAKFAST", "LUNCH", "DINNER", "SNACK" };
        for (var day = 1; day <= 7; day++)
        {
            for (var mealIndex = 0; mealIndex < mealTypes.Length; mealIndex++)
            {
                var selected = SelectRecipe(candidates, availableIngredients, day, mealIndex);
                await mealPlanMeals.AddAsync(new MealPlanMeal { MealPlanId = plan.MealPlanId, DayNumber = day, MealType = mealTypes[mealIndex], RecipeId = selected.PostId }, cancellationToken);
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
    private static MealPlanResponse ToResponse(MealPlan x) => new(x.MealPlanId, x.StartDate, x.Meals.OrderBy(m => m.DayNumber).ThenBy(m => m.MealType).Select(m => new MealPlanMealResponse(m.DayNumber, m.MealType, m.RecipeId, m.Recipe.Post.Title)).ToList());
    private static HashSet<string> ParseIngredients(string? value) => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToLowerInvariant()).ToHashSet();
    private static Recipe SelectRecipe(IReadOnlyCollection<Recipe> candidates, IReadOnlySet<string> availableIngredients, int day, int mealIndex) => candidates.OrderByDescending(x => x.Ingredients.Count(i => availableIngredients.Contains(i.IngredientName.ToLowerInvariant()))).ThenBy(x => (x.PostId + day + mealIndex) % candidates.Count).First();
}
