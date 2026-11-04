using DAL.Entities;

namespace BLL.Common;

/// <summary>Shared, conservative calculations used only for calorie estimates and meal planning.</summary>
public static class NutritionProfileRules
{
    public const decimal MinimumTargetCalories = 1000m;

    public static void Validate(UserProfile profile)
    {
        if (profile.GoalWeightKg is not > 0) return;
        if (profile.HeightCm is not > 0)
            throw new ServiceException("Enter your height before setting a target weight.", 400);

        var (min, max) = HealthyWeightRange(profile.HeightCm.Value);
        if (profile.GoalWeightKg < min || profile.GoalWeightKg > max)
            throw new ServiceException($"Target weight must stay within the healthy BMI range for your height ({min:0.0}–{max:0.0} kg).", 400);

        var (bmi, category) = MealPlannerBmi.Calculate(profile);
        if (profile.WeightKg is not > 0 || bmi is null) return;
        if (category == "Underweight" && profile.GoalWeightKg <= profile.WeightKg)
            throw new ServiceException("A user in the underweight BMI group cannot set a lower target weight.", 400);
        if (category == "Obese" && profile.GoalWeightKg >= profile.WeightKg)
            throw new ServiceException("A user in the high-BMI group cannot set a higher target weight.", 400);
    }

    public static (decimal MinimumKg, decimal MaximumKg) HealthyWeightRange(decimal heightCm)
    {
        var meters = heightCm / 100m;
        return (Math.Round(18.5m * meters * meters, 1), Math.Round(24.9m * meters * meters, 1));
    }

    public static int? Age(int? birthYear, DateTime today)
    {
        if (birthYear is null) return null;
        var age = today.Year - birthYear.Value;
        return age is >= 18 and <= 120 ? age : null;
    }

    public static decimal? CalculateTdee(UserProfile? profile, DateTime today)
    {
        if (profile?.WeightKg is not > 0 || profile.HeightCm is not > 0 || string.IsNullOrWhiteSpace(profile.Sex)) return null;
        var age = Age(profile.BirthYear, today);
        if (age is null) return null;
        var bmr = 10m * profile.WeightKg.Value + 6.25m * profile.HeightCm.Value - 5m * age.Value + (profile.Sex == "MALE" ? 5m : -161m);
        var factor = profile.ActivityLevel switch
        {
            "SEDENTARY" => 1.2m,
            "LIGHT" => 1.375m,
            "MODERATE" => 1.55m,
            "ACTIVE" => 1.725m,
            "VERY_ACTIVE" => 1.9m,
            _ => (decimal?)null
        };
        return factor is null ? null : Math.Round(bmr * factor.Value, 0);
    }

    public static decimal? EstimateWeeksToGoal(UserProfile? profile, decimal? dailyAdjustment)
    {
        if (profile?.GoalWeightKg is not > 0 || profile.WeightKg is not > 0 || dailyAdjustment is not > 0) return null;
        return Math.Round(Math.Abs(profile.GoalWeightKg.Value - profile.WeightKg.Value) * 7700m / (dailyAdjustment.Value * 7m), 1);
    }

    /// <summary>
    /// Infers the direction from the profile itself. This must not be based on
    /// BMI alone: a user with a normal BMI can still have a valid gain or loss
    /// target inside their healthy range.
    /// </summary>
    public static string InferNutritionGoal(UserProfile? profile)
    {
        if (profile?.GoalWeightKg is not > 0 || profile.WeightKg is not > 0)
            return "MAINTAIN";

        return profile.GoalWeightKg.Value < profile.WeightKg.Value
            ? "LOSE"
            : profile.GoalWeightKg.Value > profile.WeightKg.Value
                ? "GAIN"
                : "MAINTAIN";
    }
}
