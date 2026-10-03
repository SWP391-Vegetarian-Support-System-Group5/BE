using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class UserAdministrationService(IRepository<User> users) : IUserAdministrationService
{
    public async Task<IReadOnlyCollection<UserResponse>> GetUsersAsync(CancellationToken cancellationToken = default) => (await users.Query().OrderBy(x => x.UserId).ToListAsync(cancellationToken)).Select(AuthService.ToResponse).ToList();
    public async Task<UserResponse> GetUserByIdAsync(int id, CancellationToken cancellationToken = default) => AuthService.ToResponse(await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404));
    public async Task UpdateStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = isActive; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = false; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
}
