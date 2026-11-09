using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class AvatarSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='UserProfiles'").ToListAsync(ct);
        if (columns.Count == 0) throw new InvalidOperationException("UserProfiles is missing from the selected database.");
        if (!columns.Contains("AvatarUrl"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `UserProfiles` ADD COLUMN `AvatarUrl` varchar(1000) NULL", ct);
        if (!columns.Contains("AvatarPublicId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `UserProfiles` ADD COLUMN `AvatarPublicId` varchar(255) NULL", ct);
    }
}
