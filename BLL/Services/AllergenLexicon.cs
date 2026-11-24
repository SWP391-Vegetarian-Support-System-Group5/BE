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
        ["Peanut"] = ["peanut", "peanuts", "đậu phộng", "lạc", "đậu lạc"],
        ["Soy"] = ["soy", "soya", "soybean", "soybeans", "đậu nành", "đậu tương", "tofu", "đậu hũ"],
        ["Gluten"] = ["gluten", "wheat", "lúa mì", "bột mì", "mì căn"],
        ["Tree Nut"] = ["tree nut", "tree nuts", "hạt cây", "các loại hạt cây", "hạnh nhân", "almond", "hạt điều", "cashew"],
        ["Sesame"] = ["sesame", "mè", "vừng"]
    };

    // Ingredient names used for discovery. Keep product names separate from profile aliases.
    private static readonly Dictionary<string, string[]> IngredientNames = new()
    {
        ["egg"] = ["trứng gà", "trứng vịt", "trứng cút", "bột trứng", "lòng đỏ", "lòng trắng", "egg white", "egg yolk", "ovalbumin", "albumen", "mayonnaise", "mayo", "meringue"],
        ["milk"] = ["sữa bò", "sữa dê", "sữa cừu", "sữa bột", "sữa đặc", "bơ sữa", "bơ lạt", "bơ mặn", "butter", "buttermilk", "ghee", "kem sữa", "kem tươi", "whipping cream", "sữa chua", "yogurt", "yoghurt", "phô mai", "phô mát", "cheese", "whey", "casein", "caseinate", "lactalbumin"],
        ["peanut"] = ["peanuts", "đậu phộng", "đậu lạc", "lạc rang", "lạc luộc", "bơ lạc", "dầu lạc", "bột lạc", "groundnut", "groundnuts"],
        ["soy"] = ["soya", "soybean", "soybeans", "đậu nành", "đậu tương", "đậu hũ", "tàu hũ", "đậu phụ", "tofu", "bean curd", "phù trúc", "váng đậu", "yuba", "chao", "tempeh", "edamame", "miso", "nước tương", "xì dầu", "tàu vị yểu", "tamari", "shoyu", "lecithin đậu nành"],
        ["gluten"] = ["wheat", "lúa mì", "bột mì", "mì căn", "seitan", "durum", "semolina", "spelt", "bulgur", "couscous", "barley", "đại mạch", "rye", "lúa mạch đen", "triticale"],
        ["tree nut"] = ["hạt cây", "các loại hạt cây", "hạnh nhân", "almond", "almonds", "hạt điều", "cashew", "cashews", "óc chó", "walnut", "walnuts", "hạt dẻ cười", "pistachio", "pistachios", "hạt phỉ", "hazelnut", "hazelnuts", "mắc ca", "macadamia", "pecan", "pecans", "hạt hồ đào", "hạt Brazil", "Brazil nut", "Brazil nuts", "hạt thông", "pine nut", "pine nuts"],
        ["sesame"] = ["mè", "vừng", "tahini", "tahina", "benne", "gingelly", "sesamum"]
    };

    private static readonly Dictionary<string, string[]> NormalizedIngredientNames = IngredientNames
        .ToDictionary(group => group.Key, group => group.Value.Select(Normalize).Distinct().ToArray());

    private static readonly Dictionary<string, string[]> KnownByAlias = Known
        .SelectMany(group => group.Value.Append(group.Key).Select(alias => (Alias: Normalize(alias), Terms: group.Value)))
        .GroupBy(entry => entry.Alias)
        .ToDictionary(group => group.Key, group => group.First().Terms);

    public static IReadOnlyCollection<string> IngredientTerms(string allergenName)
    {
        var name = Normalize(allergenName);
        var key = DuplicateKey(name);
        if (key == "tree nut" && name is not ("tree nut" or "tree nuts" or "hat cay" or "cac loai hat cay")) return [];
        return NormalizedIngredientNames.GetValueOrDefault(key) ?? [];
    }

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

    public static string DuplicateKey(string name) => Normalize(KnownTerms(name)?.FirstOrDefault() ?? name);

    private static string[]? KnownTerms(string name) => KnownByAlias.GetValueOrDefault(Normalize(name));

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
