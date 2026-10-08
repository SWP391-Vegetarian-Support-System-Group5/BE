using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610060004_RemoveOpenAiKnowledgeBaseSettings")]
public partial class RemoveOpenAiKnowledgeBaseSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[AiKnowledgeBaseSettings]', N'U') IS NOT NULL
                DROP TABLE [AiKnowledgeBaseSettings];
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AiKnowledgeBaseSettings",
            columns: table => new
            {
                AiKnowledgeBaseSettingsId = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                VectorStoreId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AiKnowledgeBaseSettings", x => x.AiKnowledgeBaseSettingsId));
    }
}
