using System.Data;
using System.Data.Common;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class IngredientSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        var locked = false;
        try
        {
            locked = Convert.ToInt32(await ScalarAsync(connection, "SELECT GET_LOCK('veggiemate_ingredient_schema', 30)", ct)) == 1;
            if (!locked) throw new InvalidOperationException("Another instance is updating the ingredient schema.");
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS `IngredientCategories` (
                  `IngredientCategoryId` int NOT NULL AUTO_INCREMENT,
                  `Code` varchar(30) NOT NULL, `Name` varchar(100) NOT NULL,
                  `DietaryGroup` varchar(30) NOT NULL DEFAULT 'UNVERIFIED',
                  PRIMARY KEY (`IngredientCategoryId`), UNIQUE KEY `UX_IngredientCategories_Code` (`Code`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """, ct);
            if (!await HasColumnAsync(connection, "IngredientCategories", "DietaryGroup", ct))
                await ExecuteAsync(connection, "ALTER TABLE `IngredientCategories` ADD COLUMN `DietaryGroup` varchar(30) NOT NULL DEFAULT 'UNVERIFIED'", ct);
            await ExecuteAsync(connection, """
                INSERT IGNORE INTO `IngredientCategories` (`Code`,`Name`) VALUES
                ('PLANT','Plant'), ('DAIRY','Dairy'), ('EGG','Egg'), ('HONEY','Honey'), ('UNVERIFIED','Unverified');
                """, ct);
            await ExecuteAsync(connection, "UPDATE `IngredientCategories` SET `DietaryGroup`=`Code` WHERE `Code` IN ('PLANT','DAIRY','EGG','HONEY') AND `DietaryGroup`='UNVERIFIED'", ct);
            await ExecuteAsync(connection, """
                CREATE TABLE IF NOT EXISTS `Ingredients` (
                  `IngredientId` int NOT NULL AUTO_INCREMENT,
                  `Name` varchar(200) COLLATE utf8mb4_0900_as_ci NOT NULL,
                  `IngredientCategoryId` int NOT NULL, `AllergenId` int NULL,
                  PRIMARY KEY (`IngredientId`), UNIQUE KEY `UX_Ingredients_Name` (`Name`),
                  CONSTRAINT `FK_Ingredients_IngredientCategories` FOREIGN KEY (`IngredientCategoryId`) REFERENCES `IngredientCategories` (`IngredientCategoryId`),
                  CONSTRAINT `FK_Ingredients_Allergens` FOREIGN KEY (`AllergenId`) REFERENCES `Allergens` (`AllergenId`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """, ct);

            if (await HasColumnAsync(connection, "RecipeIngredients", "IngredientName", ct))
            {
                if (!await HasColumnAsync(connection, "RecipeIngredients", "IngredientId", ct))
                    await ExecuteAsync(connection, "ALTER TABLE `RecipeIngredients` ADD COLUMN `IngredientId` int NULL", ct);
                await BackfillAsync(db, connection, ct);
            }
            if (Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM `RecipeIngredients` WHERE `IngredientId` IS NULL", ct)) > 0)
                throw new InvalidOperationException("Ingredient migration has unmapped recipe rows; legacy columns have been preserved.");
            if (Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='RecipeIngredients' AND column_name='IngredientId' AND is_nullable='YES'", ct)) > 0)
                await ExecuteAsync(connection, "ALTER TABLE `RecipeIngredients` MODIFY COLUMN `IngredientId` int NOT NULL", ct);
            if (Convert.ToInt32(await ScalarAsync(connection, "SELECT COUNT(*) FROM information_schema.key_column_usage WHERE table_schema=DATABASE() AND table_name='RecipeIngredients' AND column_name='IngredientId' AND referenced_table_name='Ingredients'", ct)) == 0)
                await ExecuteAsync(connection, "ALTER TABLE `RecipeIngredients` ADD CONSTRAINT `FK_RecipeIngredients_Ingredients` FOREIGN KEY (`IngredientId`) REFERENCES `Ingredients` (`IngredientId`)", ct);

            await DropColumnAsync(connection, "RecipeIngredients", "IngredientName", ct);
            await DropColumnAsync(connection, "RecipeIngredients", "DietaryGroup", ct);
            await DropColumnAsync(connection, "RecipeIngredients", "AllergenId", ct);
            await DropColumnAsync(connection, "UserProfiles", "HealthGoal", ct);
            await DropColumnAsync(connection, "Posts", "CategoryId", ct);
            await DropColumnAsync(connection, "VideoRecipeDrafts", "CategoryId", ct);
            await ExecuteAsync(connection, "DROP TABLE IF EXISTS `Categories`", ct);
            await ExecuteAsync(connection, "DROP TABLE IF EXISTS `RestaurantReviews`", ct);
        }
        finally
        {
            if (locked) await ScalarAsync(connection, "SELECT RELEASE_LOCK('veggiemate_ingredient_schema')", CancellationToken.None);
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static async Task BackfillAsync(VegetarianDbContext db, DbConnection connection, CancellationToken ct)
    {
        var rows = new List<(string Name, string Code, int? AllergenId)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT `IngredientName`, `DietaryGroup`, `AllergenId` FROM `RecipeIngredients`";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add((reader.GetString(0).Trim(), reader.GetString(1).Trim().ToUpperInvariant(), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }
        var categories = await db.IngredientCategories.ToDictionaryAsync(x => x.Code, ct);
        var catalog = (await db.Ingredients.ToListAsync(ct)).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var codes = group.Select(x => x.Code).Where(x => x != "UNVERIFIED").Distinct().ToArray();
            var allergens = group.Where(x => x.AllergenId.HasValue).Select(x => x.AllergenId!.Value).Distinct().ToArray();
            if (string.IsNullOrWhiteSpace(group.Key) || codes.Length > 1 || allergens.Length > 1)
                throw new InvalidOperationException($"Ingredient '{group.Key}' has conflicting legacy data; resolve it before migration.");
            var code = codes.SingleOrDefault() ?? "UNVERIFIED";
            if (!categories.TryGetValue(code, out var category)) throw new InvalidOperationException($"Unknown ingredient group '{code}'.");
            var allergenId = allergens.Length == 0 ? (int?)null : allergens[0];
            if (!catalog.TryGetValue(group.Key, out var ingredient))
            {
                ingredient = new Ingredient { Name = group.Key, IngredientCategoryId = category.IngredientCategoryId, AllergenId = allergenId };
                db.Ingredients.Add(ingredient);
                catalog.Add(group.Key, ingredient);
            }
            else if (ingredient.IngredientCategoryId != category.IngredientCategoryId || ingredient.AllergenId != allergenId)
                throw new InvalidOperationException($"Ingredient '{group.Key}' conflicts with the existing catalog.");
        }
        await db.SaveChangesAsync(ct);
        await ExecuteAsync(connection, """
            UPDATE `RecipeIngredients` r JOIN `Ingredients` i
              ON TRIM(r.`IngredientName`) COLLATE utf8mb4_0900_as_ci = i.`Name`
            SET r.`IngredientId` = i.`IngredientId`;
            """, ct);
    }

    private static async Task DropColumnAsync(DbConnection connection, string table, string column, CancellationToken ct)
    {
        if (!await HasColumnAsync(connection, table, column, ct)) return;
        var foreignKeys = await NamesAsync(connection, "SELECT DISTINCT constraint_name FROM information_schema.key_column_usage WHERE table_schema=DATABASE() AND table_name=@table AND column_name=@column AND referenced_table_name IS NOT NULL", table, column, ct);
        foreach (var name in foreignKeys) await ExecuteAsync(connection, $"ALTER TABLE {Quote(table)} DROP FOREIGN KEY {Quote(name)}", ct);
        var checks = await NamesAsync(connection, "SELECT t.constraint_name FROM information_schema.table_constraints t JOIN information_schema.check_constraints c ON t.constraint_schema=c.constraint_schema AND t.constraint_name=c.constraint_name WHERE t.table_schema=DATABASE() AND t.table_name=@table AND t.constraint_type='CHECK' AND c.check_clause LIKE CONCAT('%',@column,'%')", table, column, ct);
        foreach (var name in checks) await ExecuteAsync(connection, $"ALTER TABLE {Quote(table)} DROP CHECK {Quote(name)}", ct);
        await ExecuteAsync(connection, $"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(column)}", ct);
    }

    private static async Task<bool> HasColumnAsync(DbConnection connection, string table, string column, CancellationToken ct) =>
        (await NamesAsync(connection, "SELECT column_name FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND column_name=@column", table, column, ct)).Count > 0;

    private static async Task<List<string>> NamesAsync(DbConnection connection, string sql, string table, string column, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in new[] { ("@table", table), ("@column", column) })
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
        }
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names;
    }

    private static string Quote(string identifier) => "`" + identifier.Replace("`", "``") + "`";
    private static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(ct);
    }
    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(ct);
    }
}
