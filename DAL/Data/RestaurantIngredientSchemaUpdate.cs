using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class RestaurantIngredientSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `RestaurantIngredients` (
              `RestaurantId` int NOT NULL,
              `IngredientId` int NOT NULL,
              PRIMARY KEY (`RestaurantId`, `IngredientId`),
              KEY `IX_RestaurantIngredients_IngredientId` (`IngredientId`),
              CONSTRAINT `FK_RestaurantIngredients_Restaurants` FOREIGN KEY (`RestaurantId`) REFERENCES `Restaurants` (`RestaurantId`) ON DELETE CASCADE,
              CONSTRAINT `FK_RestaurantIngredients_Ingredients` FOREIGN KEY (`IngredientId`) REFERENCES `Ingredients` (`IngredientId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """, ct);
    }
}
