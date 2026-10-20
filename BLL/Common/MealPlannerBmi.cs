using DAL.Entities;

namespace BLL.Common;

public static class MealPlannerBmi
{
    // Existing thresholds are unchanged; the project calls the >=25 group OBESE.
    public static (decimal? Bmi, string? Category) Calculate(UserProfile? profile)
    {
        if (profile?.HeightCm is not > 0 || profile.WeightKg is not > 0) return (null, null);
        var heightM = profile.HeightCm.Value / 100m;
        var bmi = Math.Round(profile.WeightKg.Value / (heightM * heightM), 2);
        return (bmi, Category(bmi));
    }

    public static string Category(decimal bmi) => bmi < 18.5m ? "Underweight" : bmi < 25m ? "Normal" : "Obese";

    public static string RequiredCode(string? category) => category switch
    {
        "Underweight" => "UNDERWEIGHT",
        "Normal" => "NORMAL",
        "Obese" => "OBESE",
        null => throw new ServiceException("Update your height and weight in your profile before creating a meal plan.", 400),
        _ => throw new ServiceException("Your BMI group could not be determined. Update your profile and try again.", 400)
    };
}
