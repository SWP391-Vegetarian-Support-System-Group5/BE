using MySqlConnector;
using BLL.Services;

internal static class AivenResetPreview
{
    private static readonly string[] EmptyTables =
    [
        "Bookmarks", "ChatMessages", "ChatSessions", "Comments", "EmailOtpCodes", "Ingredients",
        "MealPlanMeals", "MealPlans", "ModerationFlags", "PostRatings", "Posts", "RecipeBMITypes",
        "RecipeCategories", "RecipeCategoryMappings", "RecipeDietCompatibility", "RecipeIngredients",
        "RecipeSteps", "Recipes", "RestaurantDietTypes", "RestaurantFoods", "RestaurantIngredients",
        "Restaurants", "UserAllergens", "VideoRecipeDraftIngredients", "VideoRecipeDraftSteps", "VideoRecipeDrafts"
    ];
    private static readonly string[] SystemTables = ["Allergens", "BMITypes", "DietTypes", "IngredientCategories", "UserProfiles", "Users", "__EFMigrationsHistory"];

    private static async Task<MySqlConnection> OpenAsync()
    {
        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('"', '\''))
            .Single();
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<List<string>> TablesAsync(MySqlCommand command)
    {
        command.CommandText = "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables;
    }

    public static async Task RunAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE()";
        Console.WriteLine("Database: " + await command.ExecuteScalarAsync());
        var tables = await TablesAsync(command);
        foreach (var table in tables)
        {
            command.CommandText = $"SELECT COUNT(*) FROM `{table.Replace("`", "``")}`";
            Console.WriteLine($"{table}: {await command.ExecuteScalarAsync()}");
        }
        command.CommandText = "SELECT UserId FROM Users WHERE Role = 'ADMIN' ORDER BY UserId";
        var admins = new List<int>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) admins.Add(reader.GetInt32(0));
        Console.WriteLine("Admin IDs to preserve: " + string.Join(", ", admins));
        Console.WriteLine("Preview only: no data was changed.");
    }

    public static async Task RemoveDietaryCategoriesAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE()";
        if ((string?)await command.ExecuteScalarAsync() != "VegetarianSupportSystemDB") throw new InvalidOperationException("Unexpected database; deletion cancelled.");
        command.CommandText = "SELECT Slug FROM RecipeCategories WHERE Type = 'DIETARY_TYPE'";
        var slugs = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) slugs.Add(reader.GetString(0));
        var expected = new HashSet<string>(["vegan", "lacto-vegetarian", "ovo-vegetarian", "lacto-ovo-vegetarian"], StringComparer.OrdinalIgnoreCase);
        if (slugs.Any(slug => !expected.Contains(slug)) || slugs.Count != slugs.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidOperationException("Unexpected dietary category found; deletion cancelled.");
        command.CommandText = "SELECT COUNT(*) FROM RecipeCategoryMappings WHERE CategoryId IN (SELECT Id FROM RecipeCategories WHERE Type = 'DIETARY_TYPE')";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Dietary categories still have recipe mappings; deletion cancelled.");
        await using var transaction = await connection.BeginTransactionAsync();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM RecipeCategories WHERE Type = 'DIETARY_TYPE'";
        var removed = await command.ExecuteNonQueryAsync();
        command.CommandText = "SELECT COUNT(*) FROM RecipeCategories WHERE Type = 'DIETARY_TYPE'";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0) throw new InvalidOperationException("Dietary categories remain; deletion cancelled.");
        await transaction.CommitAsync();
        Console.WriteLine($"Removed {removed} dietary recipe categories. DietTypes and RecipeDietCompatibility retained.");
    }

    public static async Task ResetAsync(string confirmation)
    {
        if (confirmation != "DELETE_AIVEN_DATA_KEEP_ADMIN_1") throw new InvalidOperationException("Explicit reset confirmation is required.");
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE()";
        if ((string?)await command.ExecuteScalarAsync() != "VegetarianSupportSystemDB") throw new InvalidOperationException("Unexpected database; reset cancelled.");
        var actualTables = await TablesAsync(command);
        var expectedTables = EmptyTables.Concat(SystemTables).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (actualTables.Count != expectedTables.Count || actualTables.Any(table => !expectedTables.Contains(table)))
            throw new InvalidOperationException("Database schema changed; reset cancelled for review.");
        command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' AND ENGINE <> 'InnoDB'";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Non-transactional table found; reset cancelled for review.");
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 'ADMIN' AND UserId = 1";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("Admin UserId=1 not found; reset cancelled.");
        command.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 'ADMIN'";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("Multiple admins found; reset cancelled for review.");

        command.CommandText = "SET FOREIGN_KEY_CHECKS = 0";
        await command.ExecuteNonQueryAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        command.Transaction = transaction;
        try
        {
            foreach (var table in EmptyTables)
            {
                command.CommandText = $"DELETE FROM `{table}`";
                await command.ExecuteNonQueryAsync();
            }
            command.CommandText = "DELETE FROM UserProfiles WHERE UserId <> 1";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "DELETE FROM Users WHERE UserId <> 1";
            await command.ExecuteNonQueryAsync();
            command.CommandText = "DELETE FROM Allergens WHERE Name NOT IN ('Peanut','Milk','Egg','Soy','Gluten','Tree Nut')";
            await command.ExecuteNonQueryAsync();

            command.CommandText = "INSERT INTO RecipeCategories (Name, Slug, Type, Description, ImageUrl, IsActive, CreatedAt) VALUES (@name, @slug, @type, NULL, NULL, TRUE, UTC_TIMESTAMP())";
            var name = command.Parameters.Add("@name", MySqlDbType.VarChar);
            var slug = command.Parameters.Add("@slug", MySqlDbType.VarChar);
            var type = command.Parameters.Add("@type", MySqlDbType.VarChar);
            foreach (var category in RecipeCategoryService.DefaultCategories)
            {
                name.Value = category.Name;
                slug.Value = category.Slug;
                type.Value = category.Type;
                await command.ExecuteNonQueryAsync();
            }
            command.Parameters.Clear();

            foreach (var table in EmptyTables.Where(table => table != "RecipeCategories"))
            {
                command.CommandText = $"SELECT COUNT(*) FROM `{table}`";
                if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0) throw new InvalidOperationException($"{table} was not emptied; reset cancelled.");
            }
            command.CommandText = "SELECT COUNT(*) FROM Users WHERE Role = 'ADMIN' AND UserId = 1";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1) throw new InvalidOperationException("Admin account changed; reset cancelled.");
            command.CommandText = "SELECT COUNT(*) FROM RecipeCategories";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != RecipeCategoryService.DefaultCategories.Count)
                throw new InvalidOperationException("Category seed incomplete; reset cancelled.");
            await transaction.CommitAsync();
            Console.WriteLine("Reset complete: admin UserId=1 and system reference tables retained; 10 menu categories seeded.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            command.Transaction = null;
            command.CommandText = "SET FOREIGN_KEY_CHECKS = 1";
            await command.ExecuteNonQueryAsync();
        }
    }
}
