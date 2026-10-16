using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Data;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BLL.Services;

public class MealPlanService(
    IRepository<User> users,
    IRepository<Recipe> recipes,
    IRepository<MealPlan> mealPlans,
    IRepository<MealPlanMeal> mealPlanMeals,
    IMemoryCache cache,
    IConfiguration configuration,
    VegetarianDbContext dbContext) : IMealPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private sealed record MealCandidate(int Id, string Title, string Ingredients);
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
        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType).Include(x => x.UserAllergens).SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new ServiceException("User not found.", 404);

        var (bmi, bmiCategory) = CalculateDisplayBmi(user.Profile);


        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var dietTypeId = user.Profile?.DietTypeId;

        var allValidCandidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .Where(x => !dietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == dietTypeId.Value && d.IsCompatible))
            .ToListAsync(ct);

        var filteredCandidates = allValidCandidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.Ingredient.Category.Code)))
            .Where(x => !dietTypeId.HasValue || VegetarianDietRules.GetCompatibleDietTypes(x.Ingredients.Select(i => i.Ingredient.Category.Code)).Contains(user.Profile?.DietType?.Name ?? ""))
            .Where(x => !x.Ingredients.Any(i => i.Ingredient.AllergenId.HasValue && userAllergenIds.Contains(i.Ingredient.AllergenId.Value)))
            .ToList();

        if (filteredCandidates.Count == 0) throw new ServiceException("The system does not have any recipes suitable for your diet/allergies.", 400);

        var preferences = IngredientPreferences.Parse(request.AvailableIngredients);
        var rnd = new Random();
        var aiCandidates = filteredCandidates
            .Select(recipe => new { Recipe = recipe, Matches = IngredientPreferences.MatchCount(recipe.Ingredients.Select(i => i.Ingredient.Name), preferences) })
            .OrderByDescending(item => item.Matches)
            .ThenByDescending(item => item.Matches / (double)Math.Max(1, item.Recipe.Ingredients.Count))
            .ThenBy(_ => rnd.Next())
            .Take(45)
            .Select(item => new MealCandidate(
                item.Recipe.PostId,
                item.Recipe.RecipeName,
                string.Join(", ", item.Recipe.Ingredients.Select(i => i.Ingredient.Name))))
            .ToList();
        if (aiCandidates.Count == 0) throw new ServiceException("No recipes are suitable for your diet/allergies.", 400);

        var aiSelectedMeals = await TryBuildMealPlanAsync(aiCandidates, request.AvailableIngredients, bmi, ct);

        var plan = new MealPlan
        {
            UserId = userId,
            StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date,
            AvailableIngredients = request.AvailableIngredients?.Trim(),
            BMI = bmi
        };
        foreach (var meal in aiSelectedMeals)
        {
            plan.Meals.Add(new MealPlanMeal { DayNumber = meal.Day, MealType = meal.MealType, RecipeId = meal.RecipeId });
        }
        await mealPlans.AddAsync(plan, ct);
        await mealPlans.SaveChangesAsync(ct);

        return await GetByIdAsync(plan.MealPlanId, userId, "USER", ct);
    }

    private async Task<List<AiMealResponse>> TryBuildMealPlanAsync(List<MealCandidate> candidates, string? availableIngredients, decimal? bmi, CancellationToken ct)
    {
        try
        {
            var aiMeals = await CallGeminiForMealPlanAsync(candidates, availableIngredients, bmi, ct);
            var validIds = CandidateIds(candidates);
            var normalized = NormalizeMealPlan(aiMeals, validIds);
            if (normalized.Count == 21) return normalized;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Local fallback keeps meal planning usable when Gemini is unavailable or returns a short/invalid plan.
        }

        return BuildLocalMealPlan(candidates);
    }

    private static HashSet<int> CandidateIds(IEnumerable<MealCandidate> candidates) => candidates.Select(x => x.Id).ToHashSet();

    private static List<AiMealResponse> NormalizeMealPlan(IEnumerable<AiMealResponse> meals, HashSet<int> validIds)
    {
        var result = new List<AiMealResponse>();
        var usedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meal in meals)
        {
            if (!validIds.Contains(meal.RecipeId)) continue;
            if (meal.Day is < 1 or > 7) continue;
            var mealType = NormalizeMealType(meal.MealType);
            var slot = $"{meal.Day}:{mealType}";
            if (!usedSlots.Add(slot)) continue;
            result.Add(new AiMealResponse(meal.Day, mealType, meal.RecipeId));
        }
        return result;
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

    private static string NormalizeMealType(string? mealType)
    {
        var value = (mealType ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "BREAKFAST" or "MORNING" or "SANG" or "SÁNG" => "BREAKFAST",
            "LUNCH" or "MIDDAY" or "TRUA" or "TRƯA" => "LUNCH",
            "DINNER" or "EVENING" or "TOI" or "TỐI" => "DINNER",
            _ => "LUNCH"
        };
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

        var plan = await mealPlans.Query().Include(x => x.Meals).SingleOrDefaultAsync(x => x.MealPlanId == mealPlanId && x.UserId == userId, ct) ?? throw new ServiceException("Meal plan not found.", 404);
        var mealToReplace = plan.Meals.FirstOrDefault(x => x.MealPlanMealId == mealPlanMealId) ?? throw new ServiceException("Meal not found in plan.", 404);

        var user = await users.Query().Include(x => x.Profile).ThenInclude(x => x!.DietType).Include(x => x.UserAllergens).SingleOrDefaultAsync(x => x.UserId == userId, ct) ?? throw new ServiceException("User not found.", 404);
        var userAllergenIds = user.UserAllergens.Select(x => x.AllergenId).ToHashSet();
        var dietTypeId = user.Profile?.DietTypeId;
        var preferences = IngredientPreferences.Parse(plan.AvailableIngredients);

        var currentRecipeIdsInPlan = plan.Meals.Select(x => x.RecipeId).ToHashSet();

        var replacementCandidates = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED" && x.PostId != mealToReplace.RecipeId && !currentRecipeIdsInPlan.Contains(x.PostId))
            .Where(x => !dietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == dietTypeId.Value && d.IsCompatible))
            .Where(x => !x.Ingredients.Any(i => i.Ingredient.AllergenId.HasValue && userAllergenIds.Contains(i.Ingredient.AllergenId.Value)))
            .ToListAsync(ct);
        var random = new Random();
        var replacement = replacementCandidates
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.Ingredient.Category.Code)))
            .Where(x => !dietTypeId.HasValue || VegetarianDietRules.GetCompatibleDietTypes(x.Ingredients.Select(i => i.Ingredient.Category.Code)).Contains(user.Profile?.DietType?.Name ?? ""))
            .Select(recipe => new { Recipe = recipe, Matches = IngredientPreferences.MatchCount(recipe.Ingredients.Select(i => i.Ingredient.Name), preferences) })
            .OrderByDescending(item => item.Matches)
            .ThenByDescending(item => item.Matches / (double)Math.Max(1, item.Recipe.Ingredients.Count))
            .ThenBy(_ => random.Next())
            .Select(item => item.Recipe)
            .FirstOrDefault();

        if (replacement == null) throw new ServiceException("No suitable meal found for replacement.", 404);

        mealToReplace.RecipeId = replacement.PostId;
        await mealPlanMeals.SaveChangesAsync(ct);

        cache.Set(cacheKey, replaceCount + 1, TimeSpan.FromDays(7));
        return await GetByIdAsync(mealPlanId, userId, "USER", ct);
    }
    private (decimal? Bmi, string? Category) CalculateDisplayBmi(UserProfile? profile)
    {
        if (profile?.HeightCm == null || profile?.WeightKg == null) return (null, null);
        var heightM = profile.HeightCm.Value / 100m;
        var bmi = Math.Round(profile.WeightKg.Value / (heightM * heightM), 2);
        string category = bmi switch
        {
            < 18.5m => "Underweight",
            < 25m => "Normal",
            _ => "Overweight"
        };
        return (bmi, category);
    }

    private async Task<List<AiMealResponse>> CallGeminiForMealPlanAsync(object candidates, string? availableIngredients, decimal? bmi, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");

        var apiKey = configuration["Gemini:ApiKey"] ?? configuration["Gemini__ApiKey"];

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ServiceException("Gemini API Key not found in the system.", 500);
        }

        string bmiText = bmi.HasValue ? bmi.Value.ToString("0.##") : "Undetermined";

        // 1. Cải thiện Prompt: Không dùng [ID] để tránh AI tưởng nhầm là mảng
        var prompt = $@"Bạn là một chuyên gia dinh dưỡng thuần chay. Nhiệm vụ của bạn là lập thực đơn 7 ngày (Sáng, Trưa, Tối) dựa TRÊN ĐÚNG danh sách các món ăn ứng viên được cung cấp dưới đây.

            THÔNG TIN NGƯỜI DÙNG:
        - Chỉ số BMI: {bmiText} (Hãy cân nhắc chọn món thanh đạm nếu thừa cân, và giàu dinh dưỡng nếu thiếu cân).
        - Nguyên liệu ưu tiên có sẵn: {availableIngredients ?? "Không có"}.

            DANH SÁCH MÓN ĂN ỨNG VIÊN (Định dạng JSON):
        {JsonSerializer.Serialize(candidates)}

            RÀNG BUỘC BẮT BUỘC (CRITICAL RULES):
        1. TUYỆT ĐỐI CHỈ CHỌN các món ăn có trong ""DANH SÁCH MÓN ĂN ỨNG VIÊN"" ở trên. Không được tự tạo ra món mới.
        2. Ưu tiên tối đa việc chọn các món có chứa ""Nguyên liệu ưu tiên có sẵn"".
        3. Thực đơn phải đủ 7 ngày, mỗi ngày 3 bữa (BREAKFAST, LUNCH, DINNER). Không trùng lặp quá nhiều món trong một tuần.

            ĐỊNH DẠNG ĐẦU RA BẮT BUỘC:
        Trả về duy nhất một mảng JSON (không kèm văn bản giải thích), với cấu trúc chính xác như sau:
        [
            {{ ""Day"": 1, ""MealType"": ""BREAKFAST"", ""RecipeId"": 123 }},
            {{ ""Day"": 1, ""MealType"": ""LUNCH"", ""RecipeId"": 456 }}
        ]";

        var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

        var model = configuration["Gemini:Model"] ?? "gemini-3.5-flash";
        var response = await client.PostAsJsonAsync($"models/{model}:generateContent?key={apiKey}", requestBody, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new ServiceException($"AI Studio connection error: {errorContent}", 500);
        }

        var jsonResult = await response.Content.ReadAsStringAsync(ct);
        string cleanJson = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(jsonResult);
            var textPart = document.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text").GetString();

            if (!string.IsNullOrEmpty(textPart))
            {
                cleanJson = textPart.Replace("```json", "").Replace("```", "").Trim();
            }
        }
        catch
        {
            throw new ServiceException("Unable to read the response from AI Studio.", 500);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        return JsonSerializer.Deserialize<List<AiMealResponse>>(cleanJson, options)
               ?? throw new ServiceException("The AI returned the wrong format.", 500);
    }

    public async Task<IReadOnlyCollection<MealPlanResponse>> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var plans = await mealPlans.Query()
            .Include(x => x.Meals)
            .ThenInclude(x => x.Recipe)
            .ThenInclude(x => x.Post)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

        return plans.Select(plan =>
        {
            string? bmiCat = null;
            if (plan.BMI.HasValue) bmiCat = plan.BMI.Value < 18.5m ? "Underweight" : plan.BMI.Value < 25m ? "Normal" : "Overweight";

            return new MealPlanResponse(
                plan.MealPlanId,
                plan.StartDate,
                plan.BMI,
                bmiCat,
                plan.Meals.OrderBy(m => m.DayNumber).Select(m => new MealPlanMealResponse(m.MealPlanMealId, m.DayNumber, m.MealType, m.RecipeId, m.Recipe.RecipeName)).ToList()
            );
        }).ToList();
    }
    public async Task<MealPlanResponse> GetByIdAsync(int id, int userId, string role, CancellationToken ct = default)
    {
        var plan = await mealPlans.Query().Include(x => x.Meals).ThenInclude(x => x.Recipe).ThenInclude(x => x.Post).SingleOrDefaultAsync(x => x.MealPlanId == id, ct) ?? throw new ServiceException("Meal plan was not found.", 404);
        if (plan.UserId != userId && role != "ADMIN") throw new ServiceException("Forbidden", 403);

        string? bmiCat = null;
        if (plan.BMI.HasValue) bmiCat = plan.BMI.Value < 18.5m ? "Underweight" : plan.BMI.Value < 25m ? "Normal" : "Overweight";

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
