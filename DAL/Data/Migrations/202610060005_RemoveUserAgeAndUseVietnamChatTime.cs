using DAL.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Data.Migrations;

[DbContext(typeof(VegetarianDbContext))]
[Migration("202610060005_RemoveUserAgeAndUseVietnamChatTime")]
public partial class RemoveUserAgeAndUseVietnamChatTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        DropColumnIfPresent(migrationBuilder, "Users", "Age");
        SetVietnamTimeDefault(migrationBuilder, "ChatSessions");
        SetVietnamTimeDefault(migrationBuilder, "ChatMessages");

        migrationBuilder.Sql("UPDATE [ChatSessions] SET [CreatedAt] = DATEADD(HOUR, 7, [CreatedAt]);");
        migrationBuilder.Sql("UPDATE [ChatMessages] SET [CreatedAt] = DATEADD(HOUR, 7, [CreatedAt]);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "Age", table: "Users", type: "int", nullable: true);
        SetUtcTimeDefault(migrationBuilder, "ChatSessions");
        SetUtcTimeDefault(migrationBuilder, "ChatMessages");
    }

    private static void DropColumnIfPresent(MigrationBuilder migrationBuilder, string table, string column)
    {
        migrationBuilder.Sql($"""
            IF COL_LENGTH(N'[{table}]', N'{column}') IS NOT NULL
            BEGIN
                DECLARE @sql nvarchar(max) = N'';

                SELECT @sql = @sql + N'ALTER TABLE [{table}] DROP CONSTRAINT ' + QUOTENAME([d].[name]) + N';'
                FROM [sys].[default_constraints] [d]
                INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
                WHERE [d].[parent_object_id] = OBJECT_ID(N'[{table}]') AND [c].[name] = N'{column}';
                IF @sql <> N'' EXEC [sys].[sp_executesql] @sql;

                SET @sql = N'';
                SELECT @sql = @sql + N'ALTER TABLE [{table}] DROP CONSTRAINT ' + QUOTENAME([c].[name]) + N';'
                FROM [sys].[check_constraints] [c]
                WHERE [c].[parent_object_id] = OBJECT_ID(N'[{table}]') AND CHARINDEX(N'{column}', [c].[definition]) > 0;
                IF @sql <> N'' EXEC [sys].[sp_executesql] @sql;

                SET @sql = N'';
                SELECT @sql = @sql + N'DROP INDEX ' + QUOTENAME([i].[name]) + N' ON [{table}];'
                FROM [sys].[indexes] [i]
                INNER JOIN [sys].[index_columns] [ic] ON [ic].[object_id] = [i].[object_id] AND [ic].[index_id] = [i].[index_id]
                INNER JOIN [sys].[columns] [c] ON [c].[object_id] = [ic].[object_id] AND [c].[column_id] = [ic].[column_id]
                WHERE [i].[object_id] = OBJECT_ID(N'[{table}]') AND [c].[name] = N'{column}' AND [i].[is_primary_key] = 0 AND [i].[is_unique_constraint] = 0;
                IF @sql <> N'' EXEC [sys].[sp_executesql] @sql;

                ALTER TABLE [{table}] DROP COLUMN [{column}];
            END
            """);
    }

    private static void SetVietnamTimeDefault(MigrationBuilder migrationBuilder, string table) => SetCreatedAtDefault(migrationBuilder, table, "DATEADD(HOUR, 7, SYSUTCDATETIME())");

    private static void SetUtcTimeDefault(MigrationBuilder migrationBuilder, string table) => SetCreatedAtDefault(migrationBuilder, table, "SYSUTCDATETIME()");

    private static void SetCreatedAtDefault(MigrationBuilder migrationBuilder, string table, string defaultExpression)
    {
        migrationBuilder.Sql($"""
            DECLARE @defaultConstraint sysname;
            SELECT @defaultConstraint = [d].[name]
            FROM [sys].[default_constraints] [d]
            INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
            WHERE [d].[parent_object_id] = OBJECT_ID(N'[{table}]') AND [c].[name] = N'CreatedAt';
            IF @defaultConstraint IS NOT NULL EXEC(N'ALTER TABLE [{table}] DROP CONSTRAINT [' + @defaultConstraint + '];');
            ALTER TABLE [{table}] ADD CONSTRAINT [DF_{table}_CreatedAt] DEFAULT ({defaultExpression}) FOR [CreatedAt];
            """);
    }
}
