using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class UserAdministrationService(IRepository<User> users) : IUserAdministrationService
{
    public async Task<IReadOnlyCollection<AdminUserResponse>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        (await users.Query().Include(x => x.Profile).OrderBy(x => x.UserId).ToListAsync(cancellationToken)).Select(ToAdminResponse).ToList();
    public async Task<AdminUserResponse> GetUserByIdAsync(int id, CancellationToken cancellationToken = default) =>
        ToAdminResponse(await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, cancellationToken)
            ?? throw new ServiceException("User was not found.", 404));
    public async Task UpdateStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = isActive; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = false; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }

    private static AdminUserResponse ToAdminResponse(User user) => new(
        user.UserId, user.Email, user.Role, user.Profile?.FullName ?? "Unknown user", user.Profile?.Sex,
        user.Profile?.BirthDate, user.Profile?.HeightCm, user.Profile?.WeightKg, user.Profile?.ActivityLevel,
        user.Profile?.HealthGoal, user.Profile?.DietTypeId, user.Profile?.Latitude, user.Profile?.Longitude,
        user.IsActive, user.IsEmailVerified, user.CreatedAt, user.UpdatedAt);
}
