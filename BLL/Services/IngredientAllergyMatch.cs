using DAL.Entities;

namespace BLL.Services;

public static class IngredientAllergyMatch
{
    // Whole phrases: a broad selection matches ingredient varieties without matching inside words.
    public static bool NameMatches(string ingredientName, string allergenName)
    {
        var ingredient = AllergenLexicon.Normalize(ingredientName);
        var allergen = AllergenLexicon.Normalize(allergenName);
        if (allergen.Length == 0 || ingredient.Length == 0) return false;
        return AllergenLexicon.ContainsTerm(ingredient, allergen) ||
            AllergenKeywordRules.Matches([allergen], ingredient, false);
    }

    public static bool Matches(Ingredient ingredient, IReadOnlyCollection<Allergen> allergens) =>
        allergens.Any(a => ingredient.AllergenId == a.AllergenId ||
            ingredient.AllergenLinks.Any(link => link.AllergenId == a.AllergenId) ||
            NameMatches(ingredient.Name, a.Name));
}
