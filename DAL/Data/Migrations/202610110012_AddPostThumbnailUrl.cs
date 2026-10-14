using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610110012_AddPostThumbnailUrl")]
public partial class AddPostThumbnailUrl : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ThumbnailUrl",
            table: "Posts",
            type: "varchar(1000)",
            unicode: false,
            maxLength: 1000,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE Posts
            SET ThumbnailUrl =
                LEFT(VideoUrl, CHARINDEX('/video/upload/', VideoUrl) + LEN('/video/upload/') - 1)
                + 'c_fill,w_900,h_506,q_auto,so_auto/'
                + LEFT(
                    SUBSTRING(VideoUrl, CHARINDEX('/video/upload/', VideoUrl) + LEN('/video/upload/'), LEN(VideoUrl)),
                    LEN(SUBSTRING(VideoUrl, CHARINDEX('/video/upload/', VideoUrl) + LEN('/video/upload/'), LEN(VideoUrl))) - CHARINDEX('.', REVERSE(SUBSTRING(VideoUrl, CHARINDEX('/video/upload/', VideoUrl) + LEN('/video/upload/'), LEN(VideoUrl))))
                )
                + '.jpg'
            WHERE ThumbnailUrl IS NULL
              AND VideoUrl LIKE 'https://res.cloudinary.com/%/video/upload/%.%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ThumbnailUrl", table: "Posts");
    }
}
