using API.Infrastructure;
using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static class AllergenReviewSchema
{
    public static async Task RunAsync(bool apply)
    {
        var config = new ConfigurationBuilder();
        DotEnvConfiguration.AddFile(config, "API/.env");
        DotEnvConfiguration.AddFile(config, "API/.aiven.env");
        config.AddEnvironmentVariables();
        var connection = config.Build().GetConnectionString("VegetarianSupportDatabase")
            ?? throw new InvalidOperationException("Database connection is not configured.");
        await using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 0))).Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (apply) await AllergenSubmissionSchemaUpdate.ApplyAsync(db, timeout.Token);
        var columns = await db.Database.SqlQueryRaw<string>("SELECT COLUMN_NAME AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='AllergenSubmissions' ORDER BY ORDINAL_POSITION").ToListAsync(timeout.Token);
        var indexes = await db.Database.SqlQueryRaw<string>("SELECT DISTINCT INDEX_NAME AS `Value` FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='AllergenSubmissions'").ToListAsync(timeout.Token);
        if (!columns.Contains("Status") || !columns.Contains("RejectionReason") || !columns.Contains("SubmittedPendingKey") ||
            !indexes.Contains("IX_AllergenSubmissions_PendingKey") || !indexes.Contains("IX_AllergenSubmissions_SubmittedPendingKey"))
            throw new InvalidOperationException("Allergen review schema is missing required columns or duplicate guards.");
        Console.WriteLine($"PASS MySQL allergen review schema: {columns.Count} columns, both unique pending indexes; no application records changed.");
        // Test real MySQL uniqueness and state constraints inside a transaction that is always rolled back.
        var userId = await db.Users.AsNoTracking().Where(x => x.IsActive).Select(x => x.UserId).FirstOrDefaultAsync(timeout.Token);
        if (userId == 0) throw new InvalidOperationException("No active user exists for rollback-only constraint verification.");
        var tag = "review-check-" + Guid.NewGuid().ToString("N");
        const string insert = "INSERT INTO `AllergenSubmissions` (`UserId`,`SubmittedName`,`Name`,`Status`,`PendingKey`,`SubmittedPendingKey`,`RejectionReason`,`CreatedAt`) VALUES ({0},{1},{1},{2},{3},{4},{5},{6})";
        await using (var transaction = await db.Database.BeginTransactionAsync(timeout.Token))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(insert, new object[] { userId, tag, "PENDING", tag, tag, null!, DateTime.UtcNow }, timeout.Token);
                async Task ExpectConstraintAsync(string status, object canonical, object submitted, object reason, int expectedError)
                {
                    try
                    {
                        await db.Database.ExecuteSqlRawAsync(insert, new object[] { userId, tag, status, canonical, submitted, reason, DateTime.UtcNow }, timeout.Token);
                        throw new InvalidOperationException("Expected MySQL constraint was not enforced.");
                    }
                    catch (MySqlConnector.MySqlException error) when (error.Number == expectedError) { }
                }
                await ExpectConstraintAsync("PENDING", tag, tag + "-other", null!, 1062);
                await ExpectConstraintAsync("PENDING", tag + "-other", tag, null!, 1062);
                await ExpectConstraintAsync("INVALID", null!, null!, null!, 3819);
                await ExpectConstraintAsync("REJECTED", null!, null!, " ", 3819);
                await db.Database.ExecuteSqlRawAsync("UPDATE `AllergenSubmissions` SET `Status`='REJECTED', `RejectionReason`='Verification only', `PendingKey`=NULL, `SubmittedPendingKey`=NULL WHERE `SubmittedName`={0}", new object[] { tag }, timeout.Token);
                await db.Database.ExecuteSqlRawAsync(insert, new object[] { userId, tag, "PENDING", tag, tag, null!, DateTime.UtcNow }, timeout.Token);
            }
            finally { await transaction.RollbackAsync(CancellationToken.None); }
        }
        if (await db.AllergenSubmissions.AnyAsync(x => x.SubmittedName == tag, timeout.Token))
            throw new InvalidOperationException("Verification records were not rolled back.");
        Console.WriteLine("PASS MySQL real duplicate guards, invalid status/rejection constraints and resubmission; all verification records rolled back.");
    }
}

