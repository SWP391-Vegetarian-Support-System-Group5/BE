using MySqlConnector;
using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

internal static class AivenResetPreview
{
    private static readonly string[] EmptyTables =
    [
        "Bookmarks", "ChatMessages", "ChatSessions", "Comments", "EmailOtpCodes", "Ingredients",
        "MealPlanMeals", "MealPlans", "ModerationFlags", "PostRatings", "Posts",
        "RecipeCategories", "RecipeCategoryMappings", "RecipeDietCompatibility", "RecipeIngredients",
        "RecipeSteps", "Recipes", "RestaurantDietTypes", "RestaurantFoods", "RestaurantIngredients",
        "Restaurants", "UserAllergens", "VideoRecipeDraftIngredients", "VideoRecipeDraftSteps", "VideoRecipeDrafts"
    ];
    private static readonly string[] SystemTables = ["Allergens", "DietTypes", "IngredientCategories", "UserProfiles", "Users", "__EFMigrationsHistory"];
    private static readonly string[] DefaultRecipeCategoryNames = ["Món từ ngũ cốc", "Bánh", "Món ăn sáng", "Bánh mì & bánh kẹp", "Tráng miệng", "Mì", "Salad & món ăn kèm", "Nước sốt", "Ăn nhẹ", "Súp & món hầm"];

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

    public static async Task AuditSchemaAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        var actualTables = await TablesAsync(command);
        var contextConnectionOptions = new MySqlConnectionStringBuilder(connection.ConnectionString)
        {
            AllowUserVariables = true,
            UseAffectedRows = false
        };
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(contextConnectionOptions.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))).Options;
        await using var db = new VegetarianDbContext(options);
        var expected = db.Model.GetEntityTypes()
            .Where(entity => !entity.IsOwned() && entity.GetTableName() is not null)
            .GroupBy(entity => entity.GetTableName()!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.SelectMany(entity => entity.GetProperties()
                .Select(property => property.GetColumnName(StoreObjectIdentifier.Table(group.Key, entity.GetSchema())) ?? property.Name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var systemTables = new HashSet<string>(["__EFMigrationsHistory"], StringComparer.OrdinalIgnoreCase);
        var extraTables = actualTables.Where(table => !systemTables.Contains(table) && !expected.ContainsKey(table)).ToArray();
        var missingTables = expected.Keys.Where(table => !actualTables.Contains(table, StringComparer.OrdinalIgnoreCase)).ToArray();
        Console.WriteLine("Extra tables: " + (extraTables.Length == 0 ? "none" : string.Join(", ", extraTables)));
        Console.WriteLine("Missing mapped tables: " + (missingTables.Length == 0 ? "none" : string.Join(", ", missingTables)));

        foreach (var table in actualTables.Where(expected.ContainsKey).OrderBy(table => table))
        {
            command.CommandText = "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table ORDER BY ORDINAL_POSITION";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@table", table);
            var actualColumns = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) actualColumns.Add(reader.GetString(0));
            var extraColumns = actualColumns.Where(column => !expected[table].Contains(column)).ToArray();
            var missingColumns = expected[table].Where(column => !actualColumns.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (extraColumns.Length > 0 || missingColumns.Length > 0)
                Console.WriteLine($"{table}: extra=[{string.Join(", ", extraColumns)}]; missing=[{string.Join(", ", missingColumns)}]");
        }
        Console.WriteLine("Audit only: no data or schema was changed.");
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

    public static async Task AuditRecipeCategoriesAsync()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.Name, COUNT(DISTINCT m.RecipeId) AS RecipeCount
            FROM RecipeCategories c
            LEFT JOIN RecipeCategoryMappings m ON m.CategoryId = c.Id
            GROUP BY c.Id, c.Name
            ORDER BY c.Name
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var total = 0;
        var empty = 0;
        while (await reader.ReadAsync())
        {
            total++;
            var recipeCount = reader.GetInt32(2);
            if (recipeCount == 0) empty++;
            Console.WriteLine($"{reader.GetInt32(0)} | {reader.GetString(1)} | recipes={recipeCount}");
        }
        Console.WriteLine($"Recipe categories: {total}; empty categories: {empty}. Audit only: no data was changed.");
    }

    public static async Task RemoveEmptyRecipeCategoriesAsync(string confirmation)
    {
        if (confirmation != "REMOVE_EMPTY_RECIPE_CATEGORIES_1")
            throw new InvalidOperationException("Explicit empty-category cleanup confirmation is required.");

        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DATABASE()";
        if ((string?)await command.ExecuteScalarAsync() != "VegetarianSupportSystemDB")
            throw new InvalidOperationException("Unexpected database; deletion cancelled.");

        command.CommandText = """
            SELECT c.Id, c.Name FROM RecipeCategories c
            LEFT JOIN RecipeCategoryMappings m ON m.CategoryId = c.Id
            WHERE m.RecipeId IS NULL
            ORDER BY c.Id
            """;
        var candidates = new List<(int Id, string Name)>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) candidates.Add((reader.GetInt32(0), reader.GetString(1)));
        if (candidates.Count == 0)
        {
            Console.WriteLine("No empty recipe categories found.");
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE c FROM RecipeCategories c
            LEFT JOIN RecipeCategoryMappings m ON m.CategoryId = c.Id
            WHERE m.RecipeId IS NULL
            """;
        var removed = await command.ExecuteNonQueryAsync();
        command.CommandText = "SELECT COUNT(*) FROM RecipeCategories c LEFT JOIN RecipeCategoryMappings m ON m.CategoryId = c.Id WHERE m.RecipeId IS NULL";
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 0)
            throw new InvalidOperationException("Empty category verification failed; transaction cancelled.");
        await transaction.CommitAsync();
        Console.WriteLine($"Removed {removed} empty recipe categories: {string.Join(", ", candidates.Select(candidate => candidate.Name))}.");
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

            command.CommandText = "INSERT INTO RecipeCategories (Name, IsActive, CreatedAt) VALUES (@name, TRUE, UTC_TIMESTAMP())";
            var name = command.Parameters.Add("@name", MySqlDbType.VarChar);
            foreach (var categoryName in DefaultRecipeCategoryNames)
            {
                name.Value = categoryName;
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
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != DefaultRecipeCategoryNames.Length)
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
