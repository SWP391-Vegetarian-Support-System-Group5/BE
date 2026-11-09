using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class RecipeRestaurantImageSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;

        foreach (var (table, alter) in new[]
        {
            ("Recipes", "ALTER TABLE `Recipes` ADD COLUMN `ImageUrl` varchar(1000) NULL"),
            ("Restaurants", "ALTER TABLE `Restaurants` ADD COLUMN `ImageUrl` varchar(1000) NULL")
        })
        {
            var exists = await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS `Value` FROM information_schema.columns
                WHERE table_schema=DATABASE() AND table_name={table} AND column_name='ImageUrl'
                """).SingleAsync(ct) > 0;
            if (!exists)
                await db.Database.ExecuteSqlRawAsync(alter, ct);
        }
    }
}
