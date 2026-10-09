using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610080010_AddRecipeMetadataToVideoDrafts")]
public partial class AddRecipeMetadataToVideoDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CategoryId",
            table: "VideoRecipeDrafts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "Servings",
            table: "VideoRecipeDrafts",
            type: "decimal(5,2)",
            precision: 5,
            scale: 2,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_VideoRecipeDrafts_CategoryId",
            table: "VideoRecipeDrafts",
            column: "CategoryId");

        migrationBuilder.AddForeignKey(
            name: "FK_VideoRecipeDrafts_Categories_CategoryId",
            table: "VideoRecipeDrafts",
            column: "CategoryId",
            principalTable: "Categories",
            principalColumn: "CategoryId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_VideoRecipeDrafts_Categories_CategoryId",
            table: "VideoRecipeDrafts");

        migrationBuilder.DropIndex(
            name: "IX_VideoRecipeDrafts_CategoryId",
            table: "VideoRecipeDrafts");

        migrationBuilder.DropColumn(
            name: "CategoryId",
            table: "VideoRecipeDrafts");

        migrationBuilder.DropColumn(
            name: "Servings",
            table: "VideoRecipeDrafts");
    }
}
