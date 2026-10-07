using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610050002_SecureChatSessions")]
public partial class SecureChatSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "GuestAccessTokenHash", table: "ChatSessions", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "CreatedAt", table: "ChatSessions", type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()");
        migrationBuilder.AddColumn<DateTime>(name: "CreatedAt", table: "ChatMessages", type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GuestAccessTokenHash", table: "ChatSessions");
        migrationBuilder.DropColumn(name: "CreatedAt", table: "ChatSessions");
        migrationBuilder.DropColumn(name: "CreatedAt", table: "ChatMessages");
    }
}
