using System.Data;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public static class RecipeCaloriesSchemaUpdate
{
    public static async Task ApplyAivenAsync(CancellationToken ct = default)
    {
        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('"', '\''))
            .Single();
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options;
        await using var db = new VegetarianDbContext(options);
        await ApplyAsync(db, ct);
        Console.WriteLine("Recipes.CaloriesPerServing schema update complete.");
    }

    public static async Task VerifyAivenAsync(CancellationToken ct = default)
    {
        var connectionString = File.ReadAllLines("API/.aiven.env")
            .Where(line => line.StartsWith("ConnectionStrings__VegetarianSupportDatabase="))
            .Select(line => line.Split('=', 2)[1].Trim().Trim('"', '\''))
            .Single();
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options;
        await using var db = new VegetarianDbContext(options);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Recipes' AND COLUMN_NAME = 'CaloriesPerServing'";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0) throw new InvalidOperationException("Recipes.CaloriesPerServing still exists.");
            Console.WriteLine("Verified: Recipes.CaloriesPerServing does not exist.");
        }
        finally { await connection.CloseAsync(); }
    }

    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsMySql()) return;
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Recipes' AND COLUMN_NAME = 'CaloriesPerServing'";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) > 0)
            {
                command.CommandText = "ALTER TABLE `Recipes` DROP COLUMN `CaloriesPerServing`";
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
