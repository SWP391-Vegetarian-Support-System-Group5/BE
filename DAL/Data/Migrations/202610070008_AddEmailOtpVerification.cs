using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610070008_AddEmailOtpVerification")]
public partial class AddEmailOtpVerification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsEmailVerified",
            table: "Users",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.CreateTable(
            name: "EmailOtpCodes",
            columns: table => new
            {
                EmailOtpCodeId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                Purpose = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                CodeHash = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                FailedAttempts = table.Column<int>(type: "int", nullable: false),
                UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EmailOtpCodes", x => x.EmailOtpCodeId));

        migrationBuilder.CreateIndex(
            name: "IX_EmailOtpCodes_Email_Purpose_UsedAt",
            table: "EmailOtpCodes",
            columns: new[] { "Email", "Purpose", "UsedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EmailOtpCodes");
        migrationBuilder.DropColumn(name: "IsEmailVerified", table: "Users");
    }
}
