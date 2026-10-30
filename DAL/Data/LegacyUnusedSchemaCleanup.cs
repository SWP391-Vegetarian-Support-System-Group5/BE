using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class LegacyUnusedSchemaCleanup
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;

        await DropColumnsAsync(db, "Posts", ["ImageUrl", "ImagePublicId"], ct);
        await DropColumnsAsync(db, "UserProfiles", ["AvatarUrl", "AvatarPublicId"], ct);
        await DropColumnsAsync(db, "MealPlanMeals", ["ImageUrl", "ImagePublicId", "ImageIsGenerated", "ImageGenerationStatus", "ImageGenerationError", "ImageGenerationToken", "ImageGenerationLeaseUntil"], ct);

        // The old one-to-one diet type is superseded by RestaurantDietTypes.
        var legacyFk = await db.Database.SqlQueryRaw<string>("""
            SELECT CONSTRAINT_NAME AS `Value` FROM information_schema.KEY_COLUMN_USAGE
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='Restaurants' AND COLUMN_NAME='DietTypeId'
              AND REFERENCED_TABLE_NAME IS NOT NULL
            LIMIT 1
            """).FirstOrDefaultAsync(ct);
        if (legacyFk is not null)
        {
            if (!Regex.IsMatch(legacyFk, "^[A-Za-z0-9_]+$")) throw new InvalidOperationException("Unexpected legacy foreign-key name.");
            var sql = $"ALTER TABLE `Restaurants` DROP FOREIGN KEY `{legacyFk}`";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        await DropColumnsAsync(db, "Restaurants", ["DietTypeId"], ct);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS `UserNotifications`", ct);
    }

    private static async Task DropColumnsAsync(VegetarianDbContext db, string table, IReadOnlyCollection<string> columns, CancellationToken ct)
    {
        var present = await db.Database.SqlQueryRaw<string>("""
            SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME={0}
            """, table).ToListAsync(ct);
        var toDrop = columns.Where(present.Contains).ToArray();
        if (toDrop.Length == 0) return;

        // All table and column names come from the fixed allowlist above.
        var sql = $"ALTER TABLE `{table}` " + string.Join(", ", toDrop.Select(column => $"DROP COLUMN `{column}`"));
        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}
