using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class BMISchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (db.Database.IsMySql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS `BMITypes` (
                  `BMITypeId` int NOT NULL AUTO_INCREMENT,
                  `Code` varchar(20) CHARACTER SET ascii NOT NULL,
                  `Name` varchar(100) NOT NULL, `Description` varchar(500) NULL,
                  PRIMARY KEY (`BMITypeId`), UNIQUE KEY `IX_BMITypes_Code` (`Code`),
                  CONSTRAINT `CK_BMITypes_Code` CHECK (`Code` IN ('UNDERWEIGHT','NORMAL','OBESE'))
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """, ct);
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS `RecipeBMITypes` (
                  `RecipeId` int NOT NULL, `BMITypeId` int NOT NULL,
                  PRIMARY KEY (`RecipeId`,`BMITypeId`),
                  KEY `IX_RecipeBMITypes_BMITypeId_RecipeId` (`BMITypeId`,`RecipeId`),
                  CONSTRAINT `FK_RecipeBMITypes_Recipes` FOREIGN KEY (`RecipeId`) REFERENCES `Recipes` (`PostId`) ON DELETE CASCADE,
                  CONSTRAINT `FK_RecipeBMITypes_BMITypes` FOREIGN KEY (`BMITypeId`) REFERENCES `BMITypes` (`BMITypeId`) ON DELETE RESTRICT
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """, ct);
            // A unique code makes concurrent startup and repeated deployment safe.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO `BMITypes` (`Code`,`Name`,`Description`) VALUES
                ('UNDERWEIGHT','Gầy',NULL),('NORMAL','Bình thường',NULL),('OBESE','Béo phì',NULL)
                ON DUPLICATE KEY UPDATE `Code` = VALUES(`Code`);
                """, ct);
            return;
        }
        foreach (var (code, name) in new[] { ("UNDERWEIGHT", "Gầy"), ("NORMAL", "Bình thường"), ("OBESE", "Béo phì") })
            if (!await db.BMITypes.AnyAsync(x => x.Code == code, ct)) db.BMITypes.Add(new BMIType { Code = code, Name = name });
        await db.SaveChangesAsync(ct);
    }
}
