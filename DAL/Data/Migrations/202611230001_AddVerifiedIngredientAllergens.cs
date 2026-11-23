using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202611230001_AddVerifiedIngredientAllergens")]
public sealed class AddVerifiedIngredientAllergens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsAllergenProfileVerified", "Ingredients", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("AllergenEvidence", "Ingredients", type: "varchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.CreateTable("IngredientAllergens", columns: table => new
        {
            IngredientId = table.Column<int>(type: "int", nullable: false),
            AllergenId = table.Column<int>(type: "int", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_IngredientAllergens", x => new { x.IngredientId, x.AllergenId });
            table.ForeignKey("FK_IngredientAllergens_Ingredients", x => x.IngredientId, "Ingredients", "IngredientId", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_IngredientAllergens_Allergens", x => x.AllergenId, "Allergens", "AllergenId", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_IngredientAllergens_AllergenId", "IngredientAllergens", "AllergenId");
        // Preserve every old relation. A single old value is not evidence of a complete composition audit.
        migrationBuilder.Sql("INSERT INTO `IngredientAllergens` (`IngredientId`, `AllergenId`) SELECT `IngredientId`, `AllergenId` FROM `Ingredients` WHERE `AllergenId` IS NOT NULL;");
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Automatic rollback would lose multi-allergen evidence. Supply a reviewed data-preserving rollback or restore a backup.");
}
