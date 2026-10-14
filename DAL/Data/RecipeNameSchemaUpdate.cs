using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class RecipeNameSchemaUpdate
{
    // The imported MySQL database does not have an EF migration baseline.
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'VideoRecipeDrafts' AND COLUMN_NAME = 'RecipeName'";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 0)
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE `VideoRecipeDrafts` ADD COLUMN `RecipeName` varchar(250) NULL", ct);
                await db.Database.ExecuteSqlRawAsync("UPDATE `VideoRecipeDrafts` SET `RecipeName` = TRIM(`Title`)", ct);
            }
            command.CommandText = "SELECT IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Recipes' AND COLUMN_NAME = 'RecipeName'";
            var nullable = await command.ExecuteScalarAsync(ct);
            if (nullable is null)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Recipes` ADD COLUMN `RecipeName` varchar(250) NULL", ct);
            else if (string.Equals(nullable.ToString(), "NO", StringComparison.OrdinalIgnoreCase))
                return;

            // Legacy recipes used their post title as the dish name. Copy once, never sync.
            await db.Database.ExecuteSqlRawAsync("UPDATE `Recipes` r JOIN `Posts` p ON p.`PostId` = r.`PostId` SET r.`RecipeName` = CASE TRIM(p.`Title`) WHEN 'Sample Recipe - Tofu Bowl' THEN 'Tofu Bowl' WHEN 'Sample Recipe - Mushroom Pasta' THEN 'Mushroom Pasta' WHEN 'Sample Recipe - Vegetable Omelette' THEN 'Vegetable Omelette' WHEN 'QA Recipe - Updated Lentil Bowl' THEN 'Lentil Bowl' ELSE TRIM(p.`Title`) END WHERE r.`RecipeName` IS NULL", ct);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Recipes` MODIFY COLUMN `RecipeName` varchar(250) NOT NULL", ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
