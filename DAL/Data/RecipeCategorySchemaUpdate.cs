using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

/// <summary>Applies the intentionally small RecipeCategories schema for legacy MySQL databases.</summary>
public static class RecipeCategorySchemaUpdate
{
    private static readonly string[] RemovedColumns = ["Slug", "Type", "Description", "ImageUrl"];

    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `RecipeCategories` (
              `Id` int NOT NULL AUTO_INCREMENT,
              `Name` varchar(150) NOT NULL,
              `IsActive` tinyint(1) NOT NULL DEFAULT 1,
              `CreatedAt` datetime(6) NOT NULL,
              PRIMARY KEY (`Id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `RecipeCategoryMappings` (
              `RecipeId` int NOT NULL, `CategoryId` int NOT NULL,
              PRIMARY KEY (`RecipeId`,`CategoryId`),
              CONSTRAINT `FK_RecipeCategoryMappings_Recipes` FOREIGN KEY (`RecipeId`) REFERENCES `Recipes` (`PostId`) ON DELETE CASCADE,
              CONSTRAINT `FK_RecipeCategoryMappings_Categories` FOREIGN KEY (`CategoryId`) REFERENCES `RecipeCategories` (`Id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """, ct);

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var columns = await ReadStringsAsync(connection, """
                SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'RecipeCategories';
                """, ct);
            var oldColumns = RemovedColumns.Where(columns.Contains).ToArray();
            if (oldColumns.Length == 0) return;

            var indexes = await ReadStringsAsync(connection, """
                SELECT DISTINCT INDEX_NAME FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'RecipeCategories'
                  AND COLUMN_NAME IN ('Slug', 'Type', 'Description', 'ImageUrl')
                  AND INDEX_NAME <> 'PRIMARY';
                """, ct);
            foreach (var index in indexes.Where(IsSafeIdentifier))
                await ExecuteAsync(connection, $"ALTER TABLE `RecipeCategories` DROP INDEX `{index}`;", ct);
            foreach (var column in oldColumns)
                await ExecuteAsync(connection, $"ALTER TABLE `RecipeCategories` DROP COLUMN `{column}`;", ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<HashSet<string>> ReadStringsAsync(System.Data.Common.DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct)) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static bool IsSafeIdentifier(string value) => Regex.IsMatch(value, "^[A-Za-z0-9_]+$");
}
