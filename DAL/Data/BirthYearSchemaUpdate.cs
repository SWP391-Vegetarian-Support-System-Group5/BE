using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

/// <summary>
/// Keeps the persisted profile data aligned with the application's year-only
/// age input without losing the year stored in older BirthDate values.
/// </summary>
public static class BirthYearSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;

        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='UserProfiles'
            """).ToListAsync(ct);

        if (!columns.Contains("BirthYear"))
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `UserProfiles` ADD COLUMN `BirthYear` int NULL", ct);
            columns.Add("BirthYear");
        }

        if (columns.Contains("BirthDate"))
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE `UserProfiles` SET `BirthYear` = YEAR(`BirthDate`) WHERE `BirthYear` IS NULL AND `BirthDate` IS NOT NULL", ct);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `UserProfiles` DROP COLUMN `BirthDate`", ct);
        }
    }
}
