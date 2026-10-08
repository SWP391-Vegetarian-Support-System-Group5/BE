using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610080011_AddNutritionPlanningFields")]
public partial class AddNutritionPlanningFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ActivityLevel", table: "UserProfiles", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "BirthDate", table: "UserProfiles", type: "date", nullable: true);
        migrationBuilder.AddColumn<string>(name: "HealthGoal", table: "UserProfiles", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);

        AddNutritionColumns(migrationBuilder, "Recipes");
        AddNutritionColumns(migrationBuilder, "VideoRecipeDrafts");

        migrationBuilder.AddColumn<decimal>(name: "BMI", table: "MealPlans", type: "decimal(5,2)", precision: 5, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "BMR", table: "MealPlans", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HealthGoal", table: "MealPlans", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "TargetCaloriesPerDay", table: "MealPlans", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "TDEE", table: "MealPlans", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "PlannedCalories", table: "MealPlanMeals", type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PlannedCalories", table: "MealPlanMeals");
        migrationBuilder.DropColumn(name: "BMI", table: "MealPlans");
        migrationBuilder.DropColumn(name: "BMR", table: "MealPlans");
        migrationBuilder.DropColumn(name: "HealthGoal", table: "MealPlans");
        migrationBuilder.DropColumn(name: "TargetCaloriesPerDay", table: "MealPlans");
        migrationBuilder.DropColumn(name: "TDEE", table: "MealPlans");

        DropNutritionColumns(migrationBuilder, "VideoRecipeDrafts");
        DropNutritionColumns(migrationBuilder, "Recipes");

        migrationBuilder.DropColumn(name: "ActivityLevel", table: "UserProfiles");
        migrationBuilder.DropColumn(name: "BirthDate", table: "UserProfiles");
        migrationBuilder.DropColumn(name: "HealthGoal", table: "UserProfiles");
    }

    private static void AddNutritionColumns(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.AddColumn<decimal>(name: "CaloriesPerServing", table: table, type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "ProteinPerServing", table: table, type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CarbsPerServing", table: table, type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "FatPerServing", table: table, type: "decimal(8,2)", precision: 8, scale: 2, nullable: true);
    }

    private static void DropNutritionColumns(MigrationBuilder migrationBuilder, string table)
    {
        migrationBuilder.DropColumn(name: "CaloriesPerServing", table: table);
        migrationBuilder.DropColumn(name: "ProteinPerServing", table: table);
        migrationBuilder.DropColumn(name: "CarbsPerServing", table: table);
        migrationBuilder.DropColumn(name: "FatPerServing", table: table);
    }
}
