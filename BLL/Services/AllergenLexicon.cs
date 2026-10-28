using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DAL.Entities;

namespace BLL.Services;

public static class AllergenLexicon
{
    private static readonly Dictionary<string, string[]> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Egg"] = ["egg", "eggs", "trứng", "trung", "lòng đỏ", "lòng trắng"],
        ["Milk"] = ["milk", "dairy", "sữa", "sua", "sữa bò", "cheese", "phô mai", "whey", "casein"],
        ["Peanut"] = ["peanut", "peanuts", "đậu phộng", "lạc"],
        ["Soy"] = ["soy", "soya", "đậu nành", "đậu tương", "tofu", "đậu hũ"],
        ["Gluten"] = ["gluten", "wheat", "lúa mì", "bột mì", "mì căn"],
        ["Tree Nut"] = ["tree nut", "tree nuts", "hạt cây", "hạnh nhân", "almond", "hạt điều", "cashew"]
    };

    public static string Normalize(string value)
    {
        var decomposed = value.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(char.ToLowerInvariant(character));
        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"[^\p{L}\p{N}]+", " ").Trim();
    }

    public static IReadOnlyCollection<string> Terms(Allergen allergen)
    {
        var terms = new List<string> { allergen.Name };
        var known = KnownTerms(allergen.Name);
        if (known is not null) terms.AddRange(known);
        return terms.Select(Normalize).Where(x => x.Length >= 2).Distinct(StringComparer.Ordinal).ToArray();
    }

    public static bool IsKnown(string name) => KnownTerms(name) is not null;

    private static string[]? KnownTerms(string name)
    {
        var normalizedName = Normalize(name);
        return Known.FirstOrDefault(group => Normalize(group.Key) == normalizedName ||
            group.Value.Any(alias => Normalize(alias) == normalizedName)).Value;
    }

    public static bool ContainsTerm(string text, string term)
    {
        var normalized = " " + Normalize(text) + " ";
        return normalized.Contains(" " + Normalize(term) + " ", StringComparison.Ordinal);
    }

    public static string? FindExisting(string input, IEnumerable<Allergen> existing)
    {
        var normalized = Normalize(input);
        return existing.FirstOrDefault(x => Terms(x).Contains(normalized, StringComparer.Ordinal))?.Name;
    }
}
