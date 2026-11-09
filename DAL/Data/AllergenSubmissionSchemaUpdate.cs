using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class AllergenSubmissionSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS `AllergenSubmissions` (
              `Id` int NOT NULL AUTO_INCREMENT,
              `UserId` int NOT NULL,
              `SubmittedName` varchar(80) NOT NULL,
              `Name` varchar(80) NOT NULL,
              `Status` varchar(20) NOT NULL,
              `PendingKey` varchar(200) NULL,
              `SubmittedPendingKey` varchar(200) NULL,
              `RejectionReason` varchar(1000) NULL,
              `AllergenId` int NULL,
              `ReviewedByUserId` int NULL,
              `CreatedAt` datetime(6) NOT NULL,
              `ReviewedAt` datetime(6) NULL,
              PRIMARY KEY (`Id`),
              UNIQUE KEY `IX_AllergenSubmissions_PendingKey` (`PendingKey`),
              UNIQUE KEY `IX_AllergenSubmissions_SubmittedPendingKey` (`SubmittedPendingKey`),
              KEY `IX_AllergenSubmissions_Status_CreatedAt` (`Status`, `CreatedAt`),
              KEY `IX_AllergenSubmissions_UserId` (`UserId`),
              KEY `IX_AllergenSubmissions_ReviewedByUserId` (`ReviewedByUserId`),
              KEY `IX_AllergenSubmissions_AllergenId` (`AllergenId`),
              CONSTRAINT `FK_AllergenSubmissions_Users` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE CASCADE,
              CONSTRAINT `FK_AllergenSubmissions_Reviewer` FOREIGN KEY (`ReviewedByUserId`) REFERENCES `Users` (`UserId`) ON DELETE SET NULL,
              CONSTRAINT `FK_AllergenSubmissions_Allergens` FOREIGN KEY (`AllergenId`) REFERENCES `Allergens` (`AllergenId`) ON DELETE SET NULL,
              CONSTRAINT `CK_AllergenSubmissions_Status` CHECK (`Status` IN ('PENDING','APPROVED','REJECTED')),
              CONSTRAINT `CK_AllergenSubmissions_RejectionReason` CHECK (`Status` <> 'REJECTED' OR CHAR_LENGTH(TRIM(COALESCE(`RejectionReason`, ''))) > 0)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """, ct);
        // Upgrade the earlier review table additively, without changing existing decisions.
        var present = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='AllergenSubmissions'").ToListAsync(ct);
        if (!present.Contains("SubmittedPendingKey"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `AllergenSubmissions` ADD COLUMN `SubmittedPendingKey` varchar(200) NULL, ADD UNIQUE KEY `IX_AllergenSubmissions_SubmittedPendingKey` (`SubmittedPendingKey`)", ct);
        var checks = await db.Database.SqlQueryRaw<string>("SELECT CONSTRAINT_NAME AS `Value` FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='AllergenSubmissions' AND CONSTRAINT_TYPE='CHECK'").ToListAsync(ct);
        if (!checks.Contains("CK_AllergenSubmissions_RejectionReason"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `AllergenSubmissions` ADD CONSTRAINT `CK_AllergenSubmissions_RejectionReason` CHECK (`Status` <> 'REJECTED' OR CHAR_LENGTH(TRIM(COALESCE(`RejectionReason`, ''))) > 0)", ct);
    }
}
