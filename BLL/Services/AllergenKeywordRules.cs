using System.Globalization;
using System.Text;

namespace BLL.Services;

// Conservative discovery exclusions, not a claim about medical cross-reactivity.
public static class AllergenKeywordRules
{
    public static string Normalize(string? value)
    {
        var result = new StringBuilder();
        foreach (var ch in (value ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            result.Append(char.IsLetterOrDigit(c) ? (c == 'đ' ? 'd' : c) : ' ');
        }
        return string.Join(" ", result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        phrase.Length > 0 && (" " + text + " ").Contains(" " + phrase + " ", StringComparison.Ordinal);

    public static bool Matches(IEnumerable<string> allergenNames, string ingredientName, bool verified)
    {
        var ingredient = Normalize(ingredientName);
        foreach (var label in allergenNames)
        {
            var allergen = Normalize(label);
            if (allergen.Length == 0) continue;
            // Explicit product choice: any mushroom selection excludes the whole mushroom keyword group.
            if (ContainsPhrase(allergen, "nam") || ContainsPhrase(allergen, "mushroom") || allergen == "shiitake")
            {
                if (new[] { "nam", "mushroom", "mushrooms", "shiitake" }.Any(k => ContainsPhrase(ingredient, k))) return true;
                continue;
            }
            // Reviewed composition distinguishes e.g. coconut yogurt from animal-milk yogurt.
            // Unreviewed catalog entries use complete labels/curated aliases, never arbitrary first words.
            if (verified) continue;
            string[] keywords = allergen switch
            {
                "dau phong" or "lac" or "peanut" or "peanuts" => ["dau phong", "lac", "peanut", "peanuts"],
                "dau nanh" or "dau tuong" or "soy" or "soya" => ["dau nanh", "dau tuong", "soy", "soya"],
                "pho mai" or "pho mat" or "cheese" => ["pho mai", "pho mat", "cheese"],
                _ => [allergen]
            };
            if (keywords.Any(k => ContainsPhrase(ingredient, k) && !(k == "lac" && ContainsPhrase(ingredient, "lac tien")))) return true;
        }
        return false;
    }
}
