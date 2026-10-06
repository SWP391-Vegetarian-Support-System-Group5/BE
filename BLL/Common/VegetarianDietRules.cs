namespace BLL.Common;

public static class VegetarianDietRules
{
    public const string Plant = "PLANT";
    public const string Dairy = "DAIRY";
    public const string Egg = "EGG";
    public const string Honey = "HONEY";
    public const string Meat = "MEAT";
    public const string Poultry = "POULTRY";
    public const string Fish = "FISH";
    public const string Shellfish = "SHELLFISH";
    public const string AnimalBroth = "ANIMAL_BROTH";
    public const string Gelatin = "GELATIN";

    private static readonly string[] VegetarianIngredientGroups = [Plant, Dairy, Egg, Honey];
    private static readonly string[] NonVegetarianIngredientGroups = [Meat, Poultry, Fish, Shellfish, AnimalBroth, Gelatin];
    private static readonly IReadOnlyDictionary<string, string[]> AllowedGroupsByDietType = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["VEGAN"] = [Plant],
        ["VEGETARIAN"] = [Plant, Dairy, Egg, Honey],
        ["LACTO_VEGETARIAN"] = [Plant, Dairy, Honey],
        ["OVO_VEGETARIAN"] = [Plant, Egg, Honey],
        ["LACTO_OVO_VEGETARIAN"] = [Plant, Dairy, Egg, Honey]
    };

    public static IReadOnlyCollection<string> SupportedIngredientGroups => VegetarianIngredientGroups;
    public static IReadOnlyCollection<string> ProhibitedNonVegetarianGroups => NonVegetarianIngredientGroups;

    public static string NormalizeIngredientGroup(string value)
    {
        var group = value.Trim().ToUpperInvariant();
        if (NonVegetarianIngredientGroups.Contains(group, StringComparer.Ordinal))
            throw new ServiceException($"'{group}' is not allowed. This system only accepts vegetarian recipes.");
        if (!VegetarianIngredientGroups.Contains(group, StringComparer.Ordinal))
            throw new ServiceException($"DietaryGroup must be one of: {string.Join(", ", VegetarianIngredientGroups)}.");
        return group;
    }

    public static IReadOnlyCollection<string> GetCompatibleDietTypes(IEnumerable<string> ingredientGroups)
    {
        var groups = ingredientGroups.ToHashSet(StringComparer.Ordinal);
        return AllowedGroupsByDietType
            .Where(rule => groups.All(group => rule.Value.Contains(group, StringComparer.Ordinal)))
            .Select(rule => rule.Key)
            .ToArray();
    }

    public static IReadOnlyCollection<string> GetAllowedGroups(string dietTypeName) =>
        AllowedGroupsByDietType.TryGetValue(dietTypeName, out var groups) ? groups : [];

    public static bool IsVerifiedVegetarianRecipe(IEnumerable<string> ingredientGroups) =>
        ingredientGroups.Any() && ingredientGroups.All(group => VegetarianIngredientGroups.Contains(group, StringComparer.Ordinal));
}
