using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610130013_AddPostAndCommentTimestamps")]
public partial class AddPostAndCommentTimestamps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedAt",
            table: "Posts",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "DATEADD(HOUR, 7, SYSUTCDATETIME())");

        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Posts",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "DATEADD(HOUR, 7, SYSUTCDATETIME())");

        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedAt",
            table: "Comments",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "DATEADD(HOUR, 7, SYSUTCDATETIME())");

        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAt",
            table: "Comments",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "DATEADD(HOUR, 7, SYSUTCDATETIME())");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Comments");
        migrationBuilder.DropColumn(name: "CreatedAt", table: "Comments");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Posts");
        migrationBuilder.DropColumn(name: "CreatedAt", table: "Posts");
    }
}
