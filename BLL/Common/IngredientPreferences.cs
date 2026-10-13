using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BLL.Common;

public static class IngredientPreferences
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["nam"] = "mushroom", ["dau hu"] = "tofu", ["tau hu"] = "tofu", ["dau phu"] = "tofu",
        ["gao"] = "rice", ["com"] = "rice", ["gao lut"] = "brown rice", ["gung"] = "ginger",
        ["cai thia"] = "bok choy", ["bun"] = "rice vermicelli", ["sa"] = "lemongrass",
        ["xa lach"] = "lettuce", ["ca chua"] = "tomato", ["ca tim"] = "eggplant",
        ["hanh la"] = "spring onion", ["bi do"] = "pumpkin", ["nuoc cot dua"] = "coconut milk",
        ["hung que"] = "basil", ["ca rot"] = "carrot", ["cu sen"] = "lotus root",
        ["me"] = "tamarind", ["hat me"] = "sesame seed", ["vung"] = "sesame seed",
        ["dau ga"] = "chickpea", ["cai bo xoi"] = "spinach", ["dau lang do"] = "red lentil",
        ["khoai lang"] = "sweet potato", ["nghe"] = "turmeric", ["ngo ri"] = "cilantro",
        ["bap"] = "corn", ["ngo"] = "corn", ["dau ha lan"] = "green pea",
        ["kho qua"] = "bitter melon", ["muop dang"] = "bitter melon", ["mien"] = "glass noodle",
        ["bap chuoi"] = "banana blossom", ["hoa chuoi"] = "banana blossom", ["tia to"] = "perilla",
        ["bong cai trang"] = "cauliflower", ["chanh"] = "lime", ["dua leo"] = "cucumber",
        ["dua chuot"] = "cucumber", ["bi do nhat"] = "kabocha", ["banh trang"] = "rice paper",
        ["bo"] = "avocado", ["bac ha"] = "mint", ["dau xanh"] = "mung bean",
        ["la dua"] = "pandan", ["duong mia"] = "cane sugar", ["rau muong"] = "water spinach",
        ["morning glory"] = "water spinach", ["toi"] = "garlic", ["ot"] = "chili",
        ["banh pho"] = "rice noodle", ["hoa hoi"] = "star anise", ["que"] = "cinnamon",
        ["cai thao"] = "napa cabbage", ["cu cai trang"] = "daikon", ["dau bap"] = "okra",
        ["hanh tay"] = "onion", ["mit non"] = "young jackfruit", ["rong bien"] = "seaweed",
        ["sua dau nanh"] = "soy milk", ["yen mach"] = "oat", ["chuoi"] = "banana",
        ["hat chia"] = "chia seed", ["dau phong"] = "peanut", ["lac"] = "peanut",
        ["hat dieu"] = "cashew", ["khoai tay"] = "potato", ["mi kieu mach"] = "soba noodle",
        ["trung"] = "egg", ["pho mai"] = "cheese", ["sua chua"] = "yogurt",
        ["mat ong"] = "honey", ["co xa huong"] = "thyme", ["tuong len men"] = "miso",
        ["hanh nhan"] = "almond", ["cam"] = "orange", ["mi lua mi"] = "wheat noodle",
        ["dau nanh nhat"] = "edamame", ["mi y"] = "pasta", ["hat oc cho"] = "walnut",
        ["xoai"] = "mango", ["ot chuong"] = "bell pepper", ["ngo tay"] = "parsley",
        ["bap cai"] = "cabbage", ["dau que"] = "green bean", ["dau co ve"] = "green bean",
        ["cu den"] = "beetroot", ["thom"] = "pineapple", ["dua"] = "pineapple",
        ["dau den"] = "black bean", ["dau lang"] = "lentil", ["huong thao"] = "rosemary"
    };

    public static string[] Parse(string? value) => (value ?? "")
        .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Canonical).Where(term => term.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    private static string Canonical(string value)
    {
        var text = new StringBuilder();
        foreach (var ch in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                text.Append(ch == 'đ' ? 'd' : ch);
        var normalized = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
        return Aliases.GetValueOrDefault(normalized, normalized);
    }

    public static bool Matches(string ingredient, string term)
    {
        var actual = Canonical(ingredient);
        var desired = Canonical(term);
        return actual == desired || (" " + actual + " ").Contains(" " + desired + " ", StringComparison.Ordinal);
    }

    public static int MatchCount(IEnumerable<string> ingredients, IReadOnlyCollection<string> preferences)
    {
        var names = ingredients.ToArray();
        return preferences.Count(term => names.Any(name => Matches(name, term)));
    }
}
