using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610060007_AddVideoRecipeDrafts")]
public partial class AddVideoRecipeDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "VideoRecipeDrafts",
            columns: table => new
            {
                VideoRecipeDraftId = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<int>(type: "int", nullable: false),
                VideoUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                GeminiFileName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                Title = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Transcript = table.Column<string>(type: "nvarchar(max)", nullable: true),
                EstimatedPrepMinutes = table.Column<int>(type: "int", nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "DATEADD(HOUR, 7, SYSUTCDATETIME())"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "DATEADD(HOUR, 7, SYSUTCDATETIME())")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VideoRecipeDrafts", x => x.VideoRecipeDraftId);
                table.ForeignKey("FK_VideoRecipeDrafts_Users_UserId", x => x.UserId, "Users", "UserId");
            });

        migrationBuilder.CreateTable(
            name: "VideoRecipeDraftIngredients",
            columns: table => new
            {
                VideoRecipeDraftIngredientId = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                VideoRecipeDraftId = table.Column<int>(type: "int", nullable: false),
                IngredientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Amount = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                DietaryGroup = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                AllergenId = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VideoRecipeDraftIngredients", x => x.VideoRecipeDraftIngredientId);
                table.ForeignKey("FK_VideoRecipeDraftIngredients_VideoRecipeDrafts_VideoRecipeDraftId", x => x.VideoRecipeDraftId, "VideoRecipeDrafts", "VideoRecipeDraftId");
            });

        migrationBuilder.CreateTable(
            name: "VideoRecipeDraftSteps",
            columns: table => new
            {
                VideoRecipeDraftStepId = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                VideoRecipeDraftId = table.Column<int>(type: "int", nullable: false),
                StepNumber = table.Column<int>(type: "int", nullable: false),
                Instruction = table.Column<string>(type: "nvarchar(max)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VideoRecipeDraftSteps", x => x.VideoRecipeDraftStepId);
                table.ForeignKey("FK_VideoRecipeDraftSteps_VideoRecipeDrafts_VideoRecipeDraftId", x => x.VideoRecipeDraftId, "VideoRecipeDrafts", "VideoRecipeDraftId");
            });

        migrationBuilder.CreateIndex(name: "IX_VideoRecipeDrafts_UserId", table: "VideoRecipeDrafts", column: "UserId");
        migrationBuilder.CreateIndex(name: "IX_VideoRecipeDraftIngredients_VideoRecipeDraftId", table: "VideoRecipeDraftIngredients", column: "VideoRecipeDraftId");
        migrationBuilder.CreateIndex(name: "IX_VideoRecipeDraftSteps_VideoRecipeDraftId_StepNumber", table: "VideoRecipeDraftSteps", columns: new[] { "VideoRecipeDraftId", "StepNumber" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VideoRecipeDraftIngredients");
        migrationBuilder.DropTable(name: "VideoRecipeDraftSteps");
        migrationBuilder.DropTable(name: "VideoRecipeDrafts");
    }
}
