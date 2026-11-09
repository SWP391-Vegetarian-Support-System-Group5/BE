using API.Infrastructure;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static class AllergenSelectionMySqlTests
{
    public static async Task RunAsync()
    {
        var builder = new ConfigurationBuilder();
        DotEnvConfiguration.AddFile(builder, "API/.env");
        DotEnvConfiguration.AddFile(builder, "API/.aiven.env");
        builder.AddEnvironmentVariables();
        var connection = builder.Build().GetConnectionString("VegetarianSupportDatabase")
            ?? throw new InvalidOperationException("Database connection is not configured.");
        await using var db = new VegetarianDbContext(new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 0))).Options);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        var userId = await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.UserId).Select(x => x.UserId).FirstOrDefaultAsync(ct);
        var catalog = await db.Allergens.AsNoTracking().OrderBy(x => x.AllergenId).Select(x => x.AllergenId).Take(2).ToListAsync(ct);
        if (userId == 0 || catalog.Count < 2) throw new InvalidOperationException("Verification requires an active user and two existing catalog entries.");
        var original = await db.UserAllergens.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.AllergenId).Select(x => x.AllergenId).ToListAsync(ct);
        var service = new UserAllergenService(new Repository<UserAllergen>(db), new Repository<Allergen>(db), new Repository<User>(db), db, new NoAi());
        var checks = 0;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                async Task SaveAndReloadAsync(List<int> ids)
                {
                    await service.UpdateAsync(userId, new UpdateUserAllergensRequest(ids), ct);
                    db.ChangeTracker.Clear();
                    var reloaded = await service.GetAsync(userId, ct);
                    if (!reloaded.Allergens.Select(x => x.Id).Order().SequenceEqual(ids.Distinct().Order()))
                        throw new InvalidOperationException("Saved allergen IDs do not match after reload.");
                    checks++;
                }
                await SaveAndReloadAsync(original);
                await SaveAndReloadAsync(catalog);
                await SaveAndReloadAsync(catalog);
                await SaveAndReloadAsync([catalog[1]]);
                try { await service.UpdateAsync(userId, new([catalog[1], -1]), ct); throw new InvalidOperationException("Unknown allergen accepted."); }
                catch (ServiceException error) when (error.StatusCode == 404) { checks++; }
                await SaveAndReloadAsync([]);
            }
            finally { await transaction.RollbackAsync(CancellationToken.None); }
        }
        db.ChangeTracker.Clear();
        var restored = await db.UserAllergens.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.AllergenId).Select(x => x.AllergenId).ToListAsync(ct);
        if (!restored.SequenceEqual(original)) throw new InvalidOperationException("Original selection was not restored by rollback.");
        Console.WriteLine($"MYSQL ALLERGEN SELECTION PASS: {checks} real service save/reload checks; transaction rolled back and original selection verified unchanged.");
    }

    private sealed class NoAi : IAllergenNameValidator
    {
        public Task<ValidatedAllergenName> ValidateAsync(string input, IReadOnlyCollection<Allergen> existing, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Selection updates must not invoke AI.");
    }
}
