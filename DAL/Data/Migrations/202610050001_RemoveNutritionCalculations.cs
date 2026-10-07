using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610050001_RemoveNutritionCalculations")]
public partial class RemoveNutritionCalculations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        DropColumnIfPresent(migrationBuilder, "Users", "ActivityLevel");
        DropColumnIfPresent(migrationBuilder, "Users", "HealthGoal");
        DropColumnIfPresent(migrationBuilder, "Recipes", "CaloriesPerServing");
        DropColumnIfPresent(migrationBuilder, "Recipes", "ProteinPerServing");
        DropColumnIfPresent(migrationBuilder, "Recipes", "CarbsPerServing");
        DropColumnIfPresent(migrationBuilder, "Recipes", "FatPerServing");
        DropColumnIfPresent(migrationBuilder, "MealPlans", "HealthGoal");
        DropColumnIfPresent(migrationBuilder, "MealPlans", "BMI");
        DropColumnIfPresent(migrationBuilder, "MealPlans", "BMR");
        DropColumnIfPresent(migrationBuilder, "MealPlans", "TDEE");
        DropColumnIfPresent(migrationBuilder, "MealPlans", "TargetCaloriesPerDay");
        DropColumnIfPresent(migrationBuilder, "MealPlanMeals", "PlannedCalories");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ActivityLevel", table: "Users", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HealthGoal", table: "Users", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CaloriesPerServing", table: "Recipes", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "ProteinPerServing", table: "Recipes", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CarbsPerServing", table: "Recipes", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "FatPerServing", table: "Recipes", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HealthGoal", table: "MealPlans", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "BMI", table: "MealPlans", type: "decimal(5,2)", precision: 5, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "BMR", table: "MealPlans", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "TDEE", table: "MealPlans", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "TargetCaloriesPerDay", table: "MealPlans", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "PlannedCalories", table: "MealPlanMeals", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
    }

    private static void DropColumnIfPresent(MigrationBuilder migrationBuilder, string table, string column)
    {
        migrationBuilder.Sql($"""
            IF COL_LENGTH(N'[{table}]', N'{column}') IS NOT NULL
            BEGIN
                DECLARE @sql nvarchar(max) = N'';

                SELECT @sql = @sql + N'ALTER TABLE [{table}] DROP CONSTRAINT ' + QUOTENAME([d].[name]) + N';'
                FROM [sys].[default_constraints] [d]
                INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
                WHERE [d].[parent_object_id] = OBJECT_ID(N'[{table}]') AND [c].[name] = N'{column}';
                IF @sql <> N'' EXEC [sys].[sp_executesql] @sql;

                SET @sql = N'';
                SELECT @sql = @sql + N'ALTER TABLE [{table}] DROP CONSTRAINT ' + QUOTENAME([c].[name]) + N';'
                FROM [sys].[check_constraints] [c]
                WHERE [c].[parent_object_id] = OBJECT_ID(N'[{table}]') AND CHARINDEX(N'{column}', [c].[definition]) > 0;
                IF @sql <> N'' EXEC [sys].[sp_executesql] @sql;

                SET @sql = N'';
                SELECT @sql = @sql + N'DROP INDEX ' + QUOTENAME([i].[name]) + N' ON [{table}];'
                FROM [sys].[indexes] [i]
                INNER JOIN [sys].[index_columns] [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id]
                INNER JOIN [sys].[columns] [c] ON [c].[object_id] = [ic].[object_id] AND [c].[column_id] = [ic].[column_id]
                WHERE [i].[object_id] = OBJECT_ID(N'[{table}]') AND [c].[name] = N'{column}' AND [i].[is_primary_key] = 0 AND [i].[is_unique_constraint] = 0;
                IF @sql <> N'' EXEC [sys].[sp_executesql] @sql;

                ALTER TABLE [{table}] DROP COLUMN [{column}];
            END
            """);
    }
}
