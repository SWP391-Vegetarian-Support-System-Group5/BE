using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610140014_AdminRestaurantCatalogEnhancements")]
public partial class AdminRestaurantCatalogEnhancements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "IsActive", table: "Allergens", type: "tinyint(1)", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>(name: "IsActive", table: "DietTypes", type: "tinyint(1)", nullable: false, defaultValue: true);

        migrationBuilder.AddColumn<string>(name: "Description", table: "Restaurants", type: "varchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "PhoneNumber", table: "Restaurants", type: "varchar(30)", unicode: false, maxLength: 30, nullable: true);
        migrationBuilder.AddColumn<string>(name: "OpeningHours", table: "Restaurants", type: "varchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ImageUrl", table: "Restaurants", type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<bool>(name: "IsActive", table: "Restaurants", type: "tinyint(1)", nullable: false, defaultValue: true);

        // Existing rows receive stable auto-increment identifiers before the legacy composite key is replaced.
        migrationBuilder.Sql("ALTER TABLE RestaurantFoods ADD COLUMN RestaurantFoodId INT NOT NULL AUTO_INCREMENT UNIQUE FIRST;");
        migrationBuilder.DropPrimaryKey(name: "PK_RestaurantFoods", table: "RestaurantFoods");
        migrationBuilder.AddPrimaryKey(name: "PK_RestaurantFoods", table: "RestaurantFoods", column: "RestaurantFoodId");
        migrationBuilder.CreateIndex(name: "IX_RestaurantFoods_RestaurantId_FoodName", table: "RestaurantFoods", columns: new[] { "RestaurantId", "FoodName" }, unique: true);

        migrationBuilder.AddColumn<string>(name: "Description", table: "RestaurantFoods", type: "varchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ImageUrl", table: "RestaurantFoods", type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "Price", table: "RestaurantFoods", type: "decimal(12,2)", precision: 12, scale: 2, nullable: true);
        migrationBuilder.AddColumn<int>(name: "DietTypeId", table: "RestaurantFoods", type: "int", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "IsAvailable", table: "RestaurantFoods", type: "tinyint(1)", nullable: false, defaultValue: true);
        migrationBuilder.CreateIndex(name: "IX_RestaurantFoods_DietTypeId", table: "RestaurantFoods", column: "DietTypeId");
        migrationBuilder.AddForeignKey(name: "FK_RestaurantFoods_DietTypes_DietTypeId", table: "RestaurantFoods", column: "DietTypeId", principalTable: "DietTypes", principalColumn: "DietTypeId", onDelete: ReferentialAction.NoAction);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_RestaurantFoods_DietTypes_DietTypeId", table: "RestaurantFoods");
        migrationBuilder.DropIndex(name: "IX_RestaurantFoods_DietTypeId", table: "RestaurantFoods");
        migrationBuilder.DropIndex(name: "IX_RestaurantFoods_RestaurantId_FoodName", table: "RestaurantFoods");
        migrationBuilder.DropPrimaryKey(name: "PK_RestaurantFoods", table: "RestaurantFoods");
        migrationBuilder.DropColumn(name: "Description", table: "RestaurantFoods");
        migrationBuilder.DropColumn(name: "ImageUrl", table: "RestaurantFoods");
        migrationBuilder.DropColumn(name: "Price", table: "RestaurantFoods");
        migrationBuilder.DropColumn(name: "DietTypeId", table: "RestaurantFoods");
        migrationBuilder.DropColumn(name: "IsAvailable", table: "RestaurantFoods");
        migrationBuilder.DropColumn(name: "RestaurantFoodId", table: "RestaurantFoods");
        migrationBuilder.AddPrimaryKey(name: "PK_RestaurantFoods", table: "RestaurantFoods", columns: new[] { "RestaurantId", "FoodName" });

        migrationBuilder.DropColumn(name: "Description", table: "Restaurants");
        migrationBuilder.DropColumn(name: "PhoneNumber", table: "Restaurants");
        migrationBuilder.DropColumn(name: "OpeningHours", table: "Restaurants");
        migrationBuilder.DropColumn(name: "ImageUrl", table: "Restaurants");
        migrationBuilder.DropColumn(name: "IsActive", table: "Restaurants");
        migrationBuilder.DropColumn(name: "IsActive", table: "DietTypes");
        migrationBuilder.DropColumn(name: "IsActive", table: "Allergens");
    }
}
