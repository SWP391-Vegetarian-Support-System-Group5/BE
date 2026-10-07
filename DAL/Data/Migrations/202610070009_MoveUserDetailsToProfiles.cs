using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610070009_MoveUserDetailsToProfiles")]
public partial class MoveUserDetailsToProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserProfiles",
            columns: table => new
            {
                UserId = table.Column<int>(type: "int", nullable: false),
                FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Sex = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                HeightCm = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                WeightKg = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                DietTypeId = table.Column<int>(type: "int", nullable: true),
                Latitude = table.Column<decimal>(type: "decimal(9,6)", nullable: true),
                Longitude = table.Column<decimal>(type: "decimal(9,6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                table.ForeignKey("FK_UserProfiles_Users_UserId", x => x.UserId, "Users", "UserId");
                table.ForeignKey("FK_UserProfiles_DietTypes_DietTypeId", x => x.DietTypeId, "DietTypes", "DietTypeId");
            });

        migrationBuilder.CreateIndex(name: "IX_UserProfiles_DietTypeId", table: "UserProfiles", column: "DietTypeId");
        migrationBuilder.Sql("""
            INSERT INTO [UserProfiles] ([UserId], [FullName], [Sex], [HeightCm], [WeightKg], [DietTypeId], [Latitude], [Longitude])
            SELECT [UserId], [FullName], [Sex], [HeightCm], [WeightKg], [DietTypeId], [Latitude], [Longitude]
            FROM [Users];
            """);

        migrationBuilder.Sql("""
            DECLARE @foreignKeyName sysname;
            SELECT @foreignKeyName = [fk].[name]
            FROM [sys].[foreign_keys] [fk]
            INNER JOIN [sys].[foreign_key_columns] [fkc] ON [fk].[object_id] = [fkc].[constraint_object_id]
            INNER JOIN [sys].[columns] [c] ON [fkc].[parent_object_id] = [c].[object_id] AND [fkc].[parent_column_id] = [c].[column_id]
            WHERE [fk].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'DietTypeId';
            IF @foreignKeyName IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @foreignKeyName + '];');

            DECLARE @indexName sysname;
            SELECT @indexName = [i].[name]
            FROM [sys].[indexes] [i]
            INNER JOIN [sys].[index_columns] [ic] ON [i].[object_id] = [ic].[object_id] AND [i].[index_id] = [ic].[index_id]
            INNER JOIN [sys].[columns] [c] ON [ic].[object_id] = [c].[object_id] AND [ic].[column_id] = [c].[column_id]
            WHERE [i].[object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'DietTypeId' AND [i].[is_primary_key] = 0 AND [i].[is_unique_constraint] = 0;
            IF @indexName IS NOT NULL EXEC(N'DROP INDEX [' + @indexName + '] ON [Users];');
            """);

        DropColumnIfExists(migrationBuilder, "FullName");
        DropColumnIfExists(migrationBuilder, "Sex");
        DropColumnIfExists(migrationBuilder, "HeightCm");
        DropColumnIfExists(migrationBuilder, "WeightKg");
        DropColumnIfExists(migrationBuilder, "DietTypeId");
        DropColumnIfExists(migrationBuilder, "Latitude");
        DropColumnIfExists(migrationBuilder, "Longitude");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FullName", table: "Users", type: "nvarchar(150)", maxLength: 150, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "Sex", table: "Users", type: "varchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "HeightCm", table: "Users", type: "decimal(5,2)", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "WeightKg", table: "Users", type: "decimal(6,2)", nullable: true);
        migrationBuilder.AddColumn<int>(name: "DietTypeId", table: "Users", type: "int", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "Latitude", table: "Users", type: "decimal(9,6)", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "Longitude", table: "Users", type: "decimal(9,6)", nullable: true);
        migrationBuilder.Sql("""
            UPDATE [u] SET [FullName] = [p].[FullName], [Sex] = [p].[Sex], [HeightCm] = [p].[HeightCm], [WeightKg] = [p].[WeightKg],
                [DietTypeId] = [p].[DietTypeId], [Latitude] = [p].[Latitude], [Longitude] = [p].[Longitude]
            FROM [Users] [u] INNER JOIN [UserProfiles] [p] ON [u].[UserId] = [p].[UserId];
            """);
        migrationBuilder.CreateIndex(name: "IX_Users_DietTypeId", table: "Users", column: "DietTypeId");
        migrationBuilder.AddForeignKey(name: "FK_Users_DietTypes_DietTypeId", table: "Users", column: "DietTypeId", principalTable: "DietTypes", principalColumn: "DietTypeId");
        migrationBuilder.DropTable(name: "UserProfiles");
    }

    private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string column)
    {
        migrationBuilder.Sql($"""
            IF COL_LENGTH(N'[Users]', N'{column}') IS NOT NULL
            BEGIN
                DECLARE @dropConstraints nvarchar(max) = N'';
                SELECT @dropConstraints += N'ALTER TABLE [Users] DROP CONSTRAINT [' + [name] + N'];'
                FROM
                (
                    SELECT [d].[name]
                    FROM [sys].[default_constraints] [d]
                    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
                    WHERE [d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'{column}'

                    UNION

                    SELECT [fk].[name]
                    FROM [sys].[foreign_keys] [fk]
                    INNER JOIN [sys].[foreign_key_columns] [fkc] ON [fk].[object_id] = [fkc].[constraint_object_id]
                    INNER JOIN [sys].[columns] [c] ON [fkc].[parent_object_id] = [c].[object_id] AND [fkc].[parent_column_id] = [c].[column_id]
                    WHERE [fk].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'{column}'

                    UNION

                    SELECT [ck].[name]
                    FROM [sys].[check_constraints] [ck]
                    WHERE [ck].[parent_object_id] = OBJECT_ID(N'[Users]') AND CHARINDEX(N'{column}', [ck].[definition]) > 0

                    UNION

                    SELECT [kc].[name]
                    FROM [sys].[key_constraints] [kc]
                    INNER JOIN [sys].[index_columns] [ic] ON [kc].[parent_object_id] = [ic].[object_id] AND [kc].[unique_index_id] = [ic].[index_id]
                    INNER JOIN [sys].[columns] [c] ON [ic].[object_id] = [c].[object_id] AND [ic].[column_id] = [c].[column_id]
                    WHERE [kc].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'{column}' AND [kc].[type] <> 'PK'
                ) [constraints];
                IF LEN(@dropConstraints) > 0 EXEC(@dropConstraints);

                DECLARE @dropIndexes nvarchar(max) = N'';
                SELECT @dropIndexes += N'DROP INDEX [' + [i].[name] + N'] ON [Users];'
                FROM [sys].[indexes] [i]
                INNER JOIN [sys].[index_columns] [ic] ON [i].[object_id] = [ic].[object_id] AND [i].[index_id] = [ic].[index_id]
                INNER JOIN [sys].[columns] [c] ON [ic].[object_id] = [c].[object_id] AND [ic].[column_id] = [c].[column_id]
                WHERE [i].[object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'{column}' AND [i].[is_primary_key] = 0 AND [i].[is_unique_constraint] = 0;
                IF LEN(@dropIndexes) > 0 EXEC(@dropIndexes);

                ALTER TABLE [Users] DROP COLUMN [{column}];
            END
            """);
    }
}
