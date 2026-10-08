namespace BLL.Common;

public static class FixedValues
{
    public static string Sex(string? value) => Require(value, ["MALE", "FEMALE"], "Sex must be MALE or FEMALE.");

    public static string ActivityLevel(string? value) => Require(value, ["SEDENTARY", "LIGHT", "MODERATE", "ACTIVE", "VERY_ACTIVE"], "Activity level must be SEDENTARY, LIGHT, MODERATE, ACTIVE, or VERY_ACTIVE.");

    public static string HealthGoal(string? value) => Require(value, ["WEIGHT_LOSS", "MAINTENANCE", "MUSCLE_GAIN"], "Health goal must be WEIGHT_LOSS, MAINTENANCE, or MUSCLE_GAIN.");

    public static string PostType(string? value) => Require(value, ["BLOG", "QUESTION", "RECIPE", "VIDEO"], "Post type must be BLOG, QUESTION, RECIPE, or VIDEO.");

    public static string PostStatus(string? value) => Require(value, ["DRAFT", "PUBLISHED", "REMOVED"], "Post status must be DRAFT, PUBLISHED, or REMOVED.");

    public static string CategoryType(string? value) => Require(value, ["FOOD", "RECIPE", "BLOG", "VIDEO"], "Category type must be FOOD, RECIPE, BLOG, or VIDEO.");

    private static string Require(string? value, IReadOnlyCollection<string> allowedValues, string message)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !allowedValues.Contains(normalized)) throw new ServiceException(message);
        return normalized;
    }
}
