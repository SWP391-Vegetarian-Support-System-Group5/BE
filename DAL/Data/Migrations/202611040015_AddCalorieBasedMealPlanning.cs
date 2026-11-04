using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202611040015_AddCalorieBasedMealPlanning")]
public partial class AddCalorieBasedMealPlanning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The deployed database has had prior nutrition migrations applied inconsistently.
        // MySQL's IF NOT EXISTS keeps this migration safe for both variants without resetting data.
        migrationBuilder.Sql("ALTER TABLE `UserProfiles` ADD COLUMN IF NOT EXISTS `GoalWeightKg` decimal(6,2) NULL");
        migrationBuilder.Sql("ALTER TABLE `Recipes` ADD COLUMN IF NOT EXISTS `CaloriesPerServing` decimal(8,2) NULL");
        migrationBuilder.Sql("ALTER TABLE `VideoRecipeDrafts` ADD COLUMN IF NOT EXISTS `CaloriesPerServing` decimal(8,2) NULL");
        migrationBuilder.Sql("ALTER TABLE `MealPlans` ADD COLUMN IF NOT EXISTS `Tdee` decimal(8,2) NULL");
        migrationBuilder.Sql("ALTER TABLE `MealPlans` ADD COLUMN IF NOT EXISTS `TargetCaloriesPerDay` decimal(8,2) NULL");
        migrationBuilder.Sql("ALTER TABLE `MealPlans` ADD COLUMN IF NOT EXISTS `CalorieAdjustment` int NULL");
        migrationBuilder.Sql("ALTER TABLE `MealPlans` ADD COLUMN IF NOT EXISTS `NutritionGoal` varchar(20) NULL");
        migrationBuilder.Sql("ALTER TABLE `MealPlanMeals` ADD COLUMN IF NOT EXISTS `PlannedCalories` decimal(8,2) NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GoalWeightKg", table: "UserProfiles");
        migrationBuilder.DropColumn(name: "CaloriesPerServing", table: "Recipes");
        migrationBuilder.DropColumn(name: "CaloriesPerServing", table: "VideoRecipeDrafts");
        migrationBuilder.DropColumn(name: "Tdee", table: "MealPlans");
        migrationBuilder.DropColumn(name: "TargetCaloriesPerDay", table: "MealPlans");
        migrationBuilder.DropColumn(name: "CalorieAdjustment", table: "MealPlans");
        migrationBuilder.DropColumn(name: "NutritionGoal", table: "MealPlans");
        migrationBuilder.DropColumn(name: "PlannedCalories", table: "MealPlanMeals");
    }
}
