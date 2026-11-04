using DAL.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// One-off, explicitly-confirmed data repair for the seeded catalog. Values are
/// calculated per serving from the recipe's recorded amounts and a curated kcal
/// per 100 g table derived from USDA FoodData Central reference foods. They are
/// intentionally stored as estimates: brands and cooking absorption vary.
/// </summary>
internal static class RecipeCaloriesBackfill
{
    private static readonly (string Term, decimal KcalPer100g)[] EnergyDensity =
    [
        ("dầu ô liu", 884), ("nước cốt dừa", 230), ("bơ đậu phộng", 588), ("hạt điều", 553), ("hạt óc chó", 654),
        ("hạt bí", 559), ("mè", 573), ("đậu phộng", 567), ("dừa nạo", 354), ("phô mai", 280), ("sữa chua", 61),
        ("trứng", 143), ("mật ong", 304), ("đường", 387), ("bột cacao", 228), ("bột năng", 358),
        ("bột nếp", 370), ("bột gạo", 366), ("bột mì", 364), ("bột yến mạch", 389), ("yến mạch", 389),
        ("quinoa", 368), ("hạt kê", 378), ("hạt kiều mạch", 343), ("gạo lứt", 370), ("gạo đỏ", 360),
        ("gạo basmati", 365), ("gạo tẻ", 365), ("nếp than", 360), ("hạt bo bo", 352), ("miến dong", 351),
        ("bún tươi", 110), ("mì", 138), ("pasta", 371), ("bánh mì", 265), ("baguette", 270),
        ("tortilla", 312), ("bánh pita", 275), ("bánh burger", 270), ("đậu hũ", 76), ("đậu gà", 164),
        ("đậu lăng", 353), ("đậu đen", 132), ("đậu đỏ", 127), ("đậu trắng", 139), ("đậu xanh", 347),
        ("đậu Hà Lan", 81), ("đậu nành", 54), ("bơ trái", 160), ("chuối", 89), ("xoài", 60),
        ("dâu", 32), ("táo", 52), ("đu đủ", 43), ("dứa", 50), ("bắp", 86), ("khoai lang", 86),
        ("khoai tây", 77), ("bí đỏ", 26), ("cà rốt", 41), ("cà chua", 18), ("nấm", 22),
        ("bông cải", 34), ("cải bó xôi", 23), ("cải", 20), ("bí ngòi", 17), ("hành", 40),
        ("rong biển", 45), ("miso", 199), ("tamari", 60), ("bột bắp", 381), ("agar", 306)
    ];

    private static readonly Dictionary<string, decimal> PieceWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["quả"] = 120, ["ổ"] = 75, ["lát"] = 30, ["cái"] = 80, ["lá"] = 2,
        ["muỗng"] = 15, ["thìa"] = 5, ["chén"] = 180, ["bát"] = 250, ["ly"] = 200
    };

    public static async Task ApplyAivenAsync(string confirmation)
    {
        if (confirmation != "FILL_RECIPE_CALORIES_FROM_INGREDIENTS_1")
            throw new InvalidOperationException("Explicit calorie backfill confirmation is required.");

        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('\"', '\'')).Single();
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options;
        await using var db = new VegetarianDbContext(options);
        if (await db.Database.SqlQueryRaw<string>("SELECT DATABASE() AS `Value`").SingleAsync() != "VegetarianSupportSystemDB")
            throw new InvalidOperationException("Unexpected database; calories were not changed.");

        var recipes = await db.Recipes.Include(recipe => recipe.Ingredients).ThenInclude(link => link.Ingredient).ToListAsync();
        if (recipes.Count == 0) throw new InvalidOperationException("No recipes found; calories were not changed.");
        foreach (var recipe in recipes)
            recipe.CaloriesPerServing = EstimatePerServing(recipe.Ingredients.Select(link => (link.Ingredient.Name, link.Amount)), recipe.Servings);
        await db.SaveChangesAsync();
        Console.WriteLine($"Updated calories for {recipes.Count} recipes. Values are ingredient-based estimates per serving.");
    }

    public static async Task VerifyAivenAsync()
    {
        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('\"', '\'')).Single();
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options;
        await using var db = new VegetarianDbContext(options);
        var stats = await db.Recipes.GroupBy(_ => 1).Select(group => new
        {
            Total = group.Count(),
            Missing = group.Count(recipe => !recipe.CaloriesPerServing.HasValue || recipe.CaloriesPerServing <= 0),
            Minimum = group.Min(recipe => recipe.CaloriesPerServing),
            Maximum = group.Max(recipe => recipe.CaloriesPerServing)
        }).SingleAsync();
        if (stats.Missing != 0) throw new InvalidOperationException($"{stats.Missing} recipes still do not have calories.");
        Console.WriteLine($"Calories verified: {stats.Total} recipes; range {stats.Minimum:0}–{stats.Maximum:0} kcal/serving; missing {stats.Missing}.");
    }

    public static decimal EstimatePerServing(IEnumerable<(string Name, string? Amount)> ingredients, decimal? servings)
    {
        decimal total = 0;
        foreach (var (name, amount) in ingredients)
        {
            var density = EnergyDensity.FirstOrDefault(item => name.Contains(item.Term, StringComparison.OrdinalIgnoreCase)).KcalPer100g;
            if (density <= 0) density = 25m; // low-energy fallback for herbs/vegetables absent from the reference table
            total += density * EstimateGrams(amount) / 100m;
        }
        var portions = servings is > 0 ? servings.Value : 1m;
        return Math.Clamp(Math.Round(total / portions, 0), 40m, 1500m);
    }

    private static decimal EstimateGrams(string? amount)
    {
        if (string.IsNullOrWhiteSpace(amount)) return 20m;
        var match = System.Text.RegularExpressions.Regex.Match(amount.Replace(',', '.'), @"\d+(?:\.\d+)?");
        if (!match.Success || !decimal.TryParse(match.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)) return 20m;
        if (amount.Contains("kg", StringComparison.OrdinalIgnoreCase)) return value * 1000m;
        if (amount.Contains("ml", StringComparison.OrdinalIgnoreCase)) return value; // practical 1 ml ≈ 1 g for the listed liquids
        if (amount.Contains("g", StringComparison.OrdinalIgnoreCase)) return value;
        var unit = PieceWeights.FirstOrDefault(pair => amount.Contains(pair.Key, StringComparison.OrdinalIgnoreCase));
        return unit.Value > 0 ? value * unit.Value : value * 20m;
    }
}
