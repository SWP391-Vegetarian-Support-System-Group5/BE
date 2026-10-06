using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class UserAllergenService(IRepository<UserAllergen> userAllergens, IRepository<Allergen> allergens, IRepository<User> users) : IUserAllergenService
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
}
