using BLL.Common;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

internal static class RecipeCatalogSeed
{
    private sealed record IngredientSeed(string Name, string Amount, string Group, string? AllergenName);
    private sealed record RecipeSeed(string CategorySlug, string Name, int PrepMinutes, int CookMinutes, decimal Servings, string Description, List<IngredientSeed> Ingredients, List<string> Steps);

    public static async Task ApplyAivenAsync(string confirmation)
    {
        if (confirmation != "SEED_100_VIETNAMESE_RECIPES_1")
            throw new InvalidOperationException("Explicit recipe seed confirmation is required.");

        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('"', '\''))
            .Single();
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options;
        await using var db = new VegetarianDbContext(options);

        if (await db.Database.SqlQueryRaw<string>("SELECT DATABASE() AS `Value`").SingleAsync() != "VegetarianSupportSystemDB")
            throw new InvalidOperationException("Unexpected database; seed cancelled.");
        if (await db.Posts.AnyAsync() || await db.Recipes.AnyAsync())
            throw new InvalidOperationException("Posts or recipes already exist; seed cancelled to avoid mixing data.");
        var admin = await db.Users.Include(user => user.Profile).SingleOrDefaultAsync(user => user.UserId == 1 && user.Role == "ADMIN")
            ?? throw new InvalidOperationException("Admin UserId=1 not found; seed cancelled.");
        var records = ReadRecords();
        if (records.Count != 100 || records.GroupBy(record => record.CategorySlug).Any(group => group.Count() != 10))
            throw new InvalidOperationException("Recipe source must contain exactly 10 recipes for each category.");
        var categories = await db.RecipeCategories.Where(category => category.IsActive).ToDictionaryAsync(category => category.Slug);
        if (records.Select(record => record.CategorySlug).Distinct().Any(slug => !categories.ContainsKey(slug)))
            throw new InvalidOperationException("A source category is missing or inactive; seed cancelled.");
        var ingredientCategories = await db.IngredientCategories.ToDictionaryAsync(category => category.Code);
        foreach (var code in new[] { "PLANT", "DAIRY", "EGG", "HONEY" })
            if (!ingredientCategories.ContainsKey(code)) throw new InvalidOperationException($"Ingredient category {code} is missing; seed cancelled.");
        var allergens = await db.Allergens.ToDictionaryAsync(allergen => allergen.Name, StringComparer.OrdinalIgnoreCase);
        var diets = await db.DietTypes.ToDictionaryAsync(diet => diet.Name, StringComparer.OrdinalIgnoreCase);

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var existingIngredients = await db.Ingredients.Include(ingredient => ingredient.Category).ToDictionaryAsync(ingredient => ingredient.Name, StringComparer.OrdinalIgnoreCase);
            var now = VietnamTime.Now;
            foreach (var record in records)
            {
                var post = new Post { UserId = admin.UserId, Title = record.Name, Content = record.Description, PostType = "RECIPE", Status = "PUBLISHED", CreatedAt = now, UpdatedAt = now };
                db.Posts.Add(post);
                await db.SaveChangesAsync();
                var recipe = new Recipe { PostId = post.PostId, RecipeName = record.Name, PrepMinutes = record.PrepMinutes, CookMinutes = record.CookMinutes, Servings = record.Servings };
                db.Recipes.Add(recipe);
                db.RecipeCategoryMappings.Add(new RecipeCategoryMapping { RecipeId = post.PostId, CategoryId = categories[record.CategorySlug].Id });
                var groups = new List<string>();
                foreach (var item in record.Ingredients)
                {
                    if (!ingredientCategories.TryGetValue(item.Group, out var ingredientCategory)) throw new InvalidOperationException($"Unknown group {item.Group}.");
                    if (!existingIngredients.TryGetValue(item.Name, out var ingredient))
                    {
                        ingredient = new Ingredient { Name = item.Name, IngredientCategoryId = ingredientCategory.IngredientCategoryId, AllergenId = item.AllergenName is not null && allergens.TryGetValue(item.AllergenName, out var allergen) ? allergen.AllergenId : null };
                        db.Ingredients.Add(ingredient);
                        existingIngredients.Add(item.Name, ingredient);
                    }
                    else if (ingredient.IngredientCategoryId != ingredientCategory.IngredientCategoryId)
                    {
                        throw new InvalidOperationException($"Ingredient '{item.Name}' has conflicting category data.");
                    }
                    db.RecipeIngredients.Add(new RecipeIngredient { RecipeId = post.PostId, Ingredient = ingredient, Amount = item.Amount });
                    groups.Add(item.Group);
                }
                for (var index = 0; index < record.Steps.Count; index++)
                    db.RecipeSteps.Add(new RecipeStep { RecipeId = post.PostId, StepNumber = index + 1, Instruction = record.Steps[index] });
                foreach (var dietName in VegetarianDietRules.GetCompatibleDietTypes(groups))
                    if (diets.TryGetValue(dietName, out var diet)) db.RecipeDietCompatibilities.Add(new RecipeDietCompatibility { RecipeId = post.PostId, DietTypeId = diet.DietTypeId, IsCompatible = true });
                await db.SaveChangesAsync();
            }
            var created = await db.Recipes.CountAsync();
            if (created != 100) throw new InvalidOperationException($"Expected 100 recipes but created {created}.");
            if (await db.RecipeCategoryMappings.GroupBy(mapping => mapping.CategoryId).AnyAsync(group => group.Count() != 10))
                throw new InvalidOperationException("Category count verification failed.");
            await transaction.CommitAsync();
            Console.WriteLine("Seed complete: 100 Vietnamese recipes across 10 categories.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public static async Task VerifyAivenAsync()
    {
        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('"', '\''))
            .Single();
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options;
        await using var db = new VegetarianDbContext(options);
        var counts = await db.RecipeCategories.AsNoTracking().OrderBy(category => category.Name)
            .Select(category => new { category.Name, Count = category.Mappings.Count })
            .ToListAsync();
        foreach (var item in counts) Console.WriteLine($"{item.Name}: {item.Count}");
        if (counts.Count != 10 || counts.Any(item => item.Count != 10)) throw new InvalidOperationException("Expected exactly 10 recipes in every category.");
        if (await db.Recipes.CountAsync() != 100 || await db.RecipeIngredients.CountAsync() == 0 || await db.RecipeSteps.CountAsync() != 300)
            throw new InvalidOperationException("Recipe catalog record verification failed.");
        var recipes = await db.Recipes
            .Include(recipe => recipe.Ingredients).ThenInclude(link => link.Ingredient).ThenInclude(ingredient => ingredient.Category)
            .Include(recipe => recipe.DietCompatibilities).ThenInclude(mapping => mapping.DietType)
            .ToListAsync();
        foreach (var recipe in recipes)
        {
            var expected = VegetarianDietRules.GetCompatibleDietTypes(recipe.Ingredients.Select(link => link.Ingredient.Category.DietaryGroup))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var actual = recipe.DietCompatibilities
                .Where(mapping => mapping.IsCompatible)
                .Select(mapping => mapping.DietType.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                throw new InvalidOperationException($"Diet compatibility mismatch for recipe {recipe.PostId}.");
        }
        Console.WriteLine("Diet compatibility verification passed.");
        Console.WriteLine("Catalog verification passed.");
    }

    private static List<RecipeSeed> ReadRecords()
    {
        var path = Path.Combine("Database", "Seed", "recipes.vi.txt");
        return File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).Select((line, index) => Parse(line, index + 2)).ToList();
    }

    private static RecipeSeed Parse(string line, int lineNumber)
    {
        var fields = line.Split('|');
        if (fields.Length != 8 || !int.TryParse(fields[2], out var prep) || !int.TryParse(fields[3], out var cook) || !decimal.TryParse(fields[4], System.Globalization.CultureInfo.InvariantCulture, out var servings))
            throw new InvalidOperationException($"Invalid recipe source at line {lineNumber}.");
        var ingredients = fields[6].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(raw =>
        {
            var parts = raw.Split('@');
            if (parts.Length is < 2 or > 4) throw new InvalidOperationException($"Invalid ingredient at line {lineNumber}.");
            return new IngredientSeed(parts[0].Trim(), parts[1].Trim(), parts.Length > 2 ? VegetarianDietRules.NormalizeIngredientGroup(parts[2]) : VegetarianDietRules.Plant, parts.Length > 3 ? parts[3].Trim() : null);
        }).ToList();
        var steps = fields[7].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (ingredients.Count == 0 || steps.Count < 2) throw new InvalidOperationException($"Recipe at line {lineNumber} is incomplete.");
        return new RecipeSeed(fields[0].Trim(), fields[1].Trim(), prep, cook, servings, fields[5].Trim(), ingredients, steps);
    }
}
