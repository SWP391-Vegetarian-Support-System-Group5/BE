using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Data;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BLL.Services;

public class UserAllergenService(IRepository<UserAllergen> userAllergens, IRepository<Allergen> allergens, IRepository<User> users, VegetarianDbContext db, IAllergenNameValidator validator) : IUserAllergenService
{
    public async Task<UserAllergensResponse> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var items = await userAllergens.Query().Include(x => x.Allergen).Where(x => x.UserId == userId).OrderBy(x => x.Allergen.Name).ToListAsync(cancellationToken);
        return new(userId, items.Select(x => new CatalogItemResponse(x.AllergenId, x.Allergen.Name, null)).ToList());
    }
    public async Task<UserAllergensResponse> UpdateAsync(int userId, UpdateUserAllergensRequest request, CancellationToken cancellationToken = default)
    {
        if (!await users.Query().AnyAsync(x => x.UserId == userId, cancellationToken)) throw new ServiceException("User was not found.", 404);
        var ids = request.AllergenIds.Distinct().ToHashSet();
        if (ids.Count != await allergens.Query().CountAsync(x => ids.Contains(x.AllergenId), cancellationToken)) throw new ServiceException("One or more allergens were not found.", 404);
        foreach (var item in await userAllergens.Query().Where(x => x.UserId == userId).ToListAsync(cancellationToken)) userAllergens.Remove(item);
        foreach (var id in ids) await userAllergens.AddAsync(new UserAllergen { UserId = userId, AllergenId = id }, cancellationToken);
        await userAllergens.SaveChangesAsync(cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    public async Task<UserAllergensResponse> AddCustomAsync(int userId, string name, CancellationToken cancellationToken = default)
    {
        if (!await users.Query().AnyAsync(x => x.UserId == userId, cancellationToken))
            throw new ServiceException("Không tìm thấy tài khoản.", 404);
        if (string.IsNullOrWhiteSpace(name))
            throw new ServiceException("Vui lòng nhập tên nguyên liệu gây dị ứng.", 422);

        var catalog = await allergens.Query().AsNoTracking().ToListAsync(cancellationToken);
        var validated = await validator.ValidateAsync(name, catalog, cancellationToken);

        // Serializing the final catalog check prevents two concurrent submissions from
        // creating different names for the same newly introduced allergen.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var latest = await allergens.Query().AsNoTracking().ToListAsync(cancellationToken);
        foreach (var term in new[] { validated.Name, name })
            if (AllergenLexicon.FindExisting(term, latest) is { } existing)
                throw new ServiceException($"Dị ứng này trùng hoặc đồng nghĩa với '{existing}' đã có. Hãy chọn mục có sẵn.", 409);

        var allergen = new Allergen { Name = validated.Name };
        try
        {
            await allergens.AddAsync(allergen, cancellationToken);
            await allergens.SaveChangesAsync(cancellationToken);
            await userAllergens.AddAsync(new UserAllergen { UserId = userId, AllergenId = allergen.AllergenId }, cancellationToken);
            await userAllergens.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new ServiceException("Dị ứng này vừa được thêm bởi người khác. Hãy tải lại danh sách và chọn mục có sẵn.", 409);
        }
        return await GetAsync(userId, cancellationToken);
    }
}
