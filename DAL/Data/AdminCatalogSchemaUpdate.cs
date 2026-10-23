using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

/// <summary>Additive, idempotent schema update for the admin catalogue fields.</summary>
public static class AdminCatalogSchemaUpdate
{
    public static async Task ApplyAsync(VegetarianDbContext db, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = db.Database.GetDbConnection();
            await AddColumnAsync(connection, "Allergens", "IsActive", "TINYINT(1) NOT NULL DEFAULT 1", ct);
            await AddColumnAsync(connection, "DietTypes", "IsActive", "TINYINT(1) NOT NULL DEFAULT 1", ct);
            await AddColumnAsync(connection, "Restaurants", "Description", "VARCHAR(1000) NULL", ct);
            await AddColumnAsync(connection, "Restaurants", "PhoneNumber", "VARCHAR(30) NULL", ct);
            await AddColumnAsync(connection, "Restaurants", "OpeningHours", "VARCHAR(200) NULL", ct);
            await AddColumnAsync(connection, "Restaurants", "ImageUrl", "VARCHAR(1000) NULL", ct);
            await AddColumnAsync(connection, "Restaurants", "IsActive", "TINYINT(1) NOT NULL DEFAULT 1", ct);
            await AddColumnAsync(connection, "RestaurantFoods", "Description", "VARCHAR(1000) NULL", ct);
            await AddColumnAsync(connection, "RestaurantFoods", "ImageUrl", "VARCHAR(1000) NULL", ct);
            await AddColumnAsync(connection, "RestaurantFoods", "Price", "DECIMAL(12,2) NULL", ct);
            await AddColumnAsync(connection, "RestaurantFoods", "DietTypeId", "INT NULL", ct);
            await AddColumnAsync(connection, "RestaurantFoods", "IsAvailable", "TINYINT(1) NOT NULL DEFAULT 1", ct);
            await CreateAdminAuditLogTableAsync(connection, ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task CreateAdminAuditLogTableAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS `AdminAuditLogs` (
                `AdminAuditLogId` INT NOT NULL AUTO_INCREMENT,
                `AdminUserId` INT NOT NULL,
                `Method` VARCHAR(10) NOT NULL,
                `Path` VARCHAR(500) NOT NULL,
                `StatusCode` INT NOT NULL,
                `CreatedAt` DATETIME(6) NOT NULL,
                PRIMARY KEY (`AdminAuditLogId`),
                INDEX `IX_AdminAuditLogs_AdminUserId` (`AdminUserId`),
                INDEX `IX_AdminAuditLogs_CreatedAt` (`CreatedAt`),
                CONSTRAINT `FK_AdminAuditLogs_Users_AdminUserId` FOREIGN KEY (`AdminUserId`) REFERENCES `Users` (`UserId`) ON DELETE RESTRICT
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task AddColumnAsync(DbConnection connection, string table, string column, string definition, CancellationToken ct)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @table AND column_name = @column";
        var tableParameter = exists.CreateParameter(); tableParameter.ParameterName = "@table"; tableParameter.Value = table; exists.Parameters.Add(tableParameter);
        var columnParameter = exists.CreateParameter(); columnParameter.ParameterName = "@column"; columnParameter.Value = column; exists.Parameters.Add(columnParameter);
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) > 0) return;
        await using var add = connection.CreateCommand();
        add.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {definition}";
        await add.ExecuteNonQueryAsync(ct);
    }
}
