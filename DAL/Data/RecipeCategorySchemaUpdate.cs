using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class RecipeCategorySchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `RecipeCategories` (
              `Id` int NOT NULL AUTO_INCREMENT, `Name` varchar(150) NOT NULL,
              `Slug` varchar(150) NOT NULL, `Type` varchar(30) NOT NULL,
              `Description` varchar(1000) NULL, `ImageUrl` varchar(1000) NULL,
              `IsActive` tinyint(1) NOT NULL DEFAULT 1, `CreatedAt` datetime(6) NOT NULL,
              PRIMARY KEY (`Id`), UNIQUE KEY `UX_RecipeCategories_Type_Slug` (`Type`,`Slug`),
              CONSTRAINT `CK_RecipeCategories_Type` CHECK (`Type` IN ('MEAL_TYPE','CUISINE','DISH_TYPE','LIFESTYLE','DIETARY_TYPE'))
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
    }
}
