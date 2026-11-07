using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

/// <summary>Keeps the saved human-readable location alongside profile coordinates.</summary>
public static class UserLivingAddressSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;

        var columns = await db.Database.SqlQueryRaw<string>("""
            SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='UserProfiles'
            """).ToListAsync(ct);
        if (!columns.Contains("LivingAddress"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `UserProfiles` ADD COLUMN `LivingAddress` varchar(300) NULL", ct);
    }
}
