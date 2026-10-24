using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class ImageSchemaUpdate
{
    // Additive MySQL update, consistent with the project's imported-schema startup flow.
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        var columns = new (string Table, string Column, string Definition)[]
        {
            ("UserProfiles", "AvatarUrl", "varchar(1000) NULL"),
            ("UserProfiles", "AvatarPublicId", "varchar(255) NULL"),
            ("Posts", "ImageUrl", "varchar(1000) NULL"),
            ("Posts", "ImagePublicId", "varchar(255) NULL"),
            ("MealPlanMeals", "ImageUrl", "varchar(1000) NULL"),
            ("MealPlanMeals", "ImagePublicId", "varchar(255) NULL"),
            ("MealPlanMeals", "ImageIsGenerated", "tinyint(1) NOT NULL DEFAULT 0")
        };
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            foreach (var column in columns)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME=@column";
                var tableParam = command.CreateParameter(); tableParam.ParameterName = "@table"; tableParam.Value = column.Table; command.Parameters.Add(tableParam);
                var columnParam = command.CreateParameter(); columnParam.ParameterName = "@column"; columnParam.Value = column.Column; command.Parameters.Add(columnParam);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 0)
                {
                    // Identifiers and definitions above are a fixed server-side allowlist, not user input.
                    await using var alter = db.Database.GetDbConnection().CreateCommand();
                    alter.CommandText = $"ALTER TABLE `{column.Table}` ADD COLUMN `{column.Column}` {column.Definition}";
                    await alter.ExecuteNonQueryAsync(ct);
                }
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
