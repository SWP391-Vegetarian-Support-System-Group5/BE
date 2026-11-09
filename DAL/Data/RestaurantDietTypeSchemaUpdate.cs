using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class RestaurantDietTypeSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `RestaurantDietTypes` (
              `RestaurantId` int NOT NULL,
              `DietTypeId` int NOT NULL,
              PRIMARY KEY (`RestaurantId`, `DietTypeId`),
              KEY `IX_RestaurantDietTypes_DietTypeId` (`DietTypeId`),
              CONSTRAINT `FK_RestaurantDietTypes_Restaurants` FOREIGN KEY (`RestaurantId`) REFERENCES `Restaurants` (`RestaurantId`) ON DELETE CASCADE,
              CONSTRAINT `FK_RestaurantDietTypes_DietTypes` FOREIGN KEY (`DietTypeId`) REFERENCES `DietTypes` (`DietTypeId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """, ct);

        var hasLegacyColumn = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS `Value` FROM information_schema.columns
            WHERE table_schema=DATABASE() AND table_name='Restaurants' AND column_name='DietTypeId'
            """).SingleAsync(ct) > 0;
        if (!hasLegacyColumn) return;

        await db.Database.ExecuteSqlRawAsync("""
            INSERT IGNORE INTO `RestaurantDietTypes` (`RestaurantId`, `DietTypeId`)
            SELECT `RestaurantId`, `DietTypeId` FROM `Restaurants` WHERE `DietTypeId` IS NOT NULL;
            """, ct);
        var unmapped = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS `Value` FROM `Restaurants` AS r
            WHERE r.`DietTypeId` IS NOT NULL AND NOT EXISTS (
              SELECT 1 FROM `RestaurantDietTypes` AS m
              WHERE m.`RestaurantId`=r.`RestaurantId` AND m.`DietTypeId`=r.`DietTypeId`)
            """).SingleAsync(ct);
        if (unmapped > 0) throw new InvalidOperationException("Legacy restaurant diet types could not be migrated.");

        await db.Database.ExecuteSqlRawAsync("UPDATE `Restaurants` SET `DietTypeId`=NULL WHERE `DietTypeId` IS NOT NULL", ct);
    }
}
