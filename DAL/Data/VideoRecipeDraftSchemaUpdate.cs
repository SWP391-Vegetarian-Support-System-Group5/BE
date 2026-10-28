using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class VideoRecipeDraftSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        var hasRecipePostId = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS `Value` FROM information_schema.columns
            WHERE table_schema=DATABASE() AND table_name='VideoRecipeDrafts' AND column_name='RecipePostId'
            """).SingleAsync(ct) > 0;
        if (!hasRecipePostId)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `VideoRecipeDrafts` ADD COLUMN `RecipePostId` int NULL, ADD UNIQUE KEY `IX_VideoRecipeDrafts_RecipePostId` (`RecipePostId`);", ct);
    }
}
