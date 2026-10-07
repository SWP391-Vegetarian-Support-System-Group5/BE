using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610060006_ClassifyRecipeIngredients")]
public partial class ClassifyRecipeIngredients : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DietaryGroup",
            table: "RecipeIngredients",
            type: "varchar(30)",
            maxLength: 30,
            nullable: false,
            defaultValue: "UNVERIFIED");

        migrationBuilder.AddCheckConstraint(
            name: "CK_RecipeIngredients_DietaryGroup",
            table: "RecipeIngredients",
            sql: "[DietaryGroup] IN ('PLANT', 'DAIRY', 'EGG', 'HONEY', 'UNVERIFIED')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_RecipeIngredients_DietaryGroup", table: "RecipeIngredients");
        migrationBuilder.DropColumn(name: "DietaryGroup", table: "RecipeIngredients");
    }
}
