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
        Console.WriteLine("Calorie-based meal planning schema update complete.");
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
            foreach (var (table, column) in RequiredColumns)
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column";
                command.Parameters.Clear();
                var tableParameter = command.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = table; command.Parameters.Add(tableParameter);
                var columnParameter = command.CreateParameter(); columnParameter.ParameterName = "@column"; columnParameter.Value = column; command.Parameters.Add(columnParameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 1)
                    throw new InvalidOperationException($"{table}.{column} is missing.");
            }
            Console.WriteLine("Verified: calorie-based meal planning columns exist.");
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
            foreach (var definition in ColumnDefinitions)
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column";
                command.Parameters.Clear();
                var tableParameter = command.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = definition.Table; command.Parameters.Add(tableParameter);
                var columnParameter = command.CreateParameter(); columnParameter.ParameterName = "@column"; columnParameter.Value = definition.Column; command.Parameters.Add(columnParameter);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 0) continue;

                // Names are sourced exclusively from the fixed allowlist below.
                command.Parameters.Clear();
                command.CommandText = $"ALTER TABLE `{definition.Table}` ADD COLUMN `{definition.Column}` {definition.SqlType} NULL";
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static readonly (string Table, string Column)[] RequiredColumns =
    [
        ("Recipes", "CaloriesPerServing"),
        ("VideoRecipeDrafts", "CaloriesPerServing"),
        ("UserProfiles", "GoalWeightKg"),
        ("MealPlans", "Tdee"),
        ("MealPlans", "TargetCaloriesPerDay"),
        ("MealPlans", "CalorieAdjustment"),
        ("MealPlans", "NutritionGoal"),
        ("MealPlanMeals", "PlannedCalories")
    ];

    private static readonly (string Table, string Column, string SqlType)[] ColumnDefinitions =
    [
        ("Recipes", "CaloriesPerServing", "decimal(8,2)"),
        ("VideoRecipeDrafts", "CaloriesPerServing", "decimal(8,2)"),
        ("UserProfiles", "GoalWeightKg", "decimal(6,2)"),
        ("MealPlans", "Tdee", "decimal(8,2)"),
        ("MealPlans", "TargetCaloriesPerDay", "decimal(8,2)"),
        ("MealPlans", "CalorieAdjustment", "int"),
        ("MealPlans", "NutritionGoal", "varchar(20)"),
        ("MealPlanMeals", "PlannedCalories", "decimal(8,2)")
    ];
}
