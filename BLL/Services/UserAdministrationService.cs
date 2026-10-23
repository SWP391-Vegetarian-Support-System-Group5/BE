using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class UserAdministrationService(IRepository<User> users, IRepository<UserProfile> profiles, IRepository<DietType> dietTypes) : IUserAdministrationService
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase) { "USER", "ADMIN" };

    public async Task<IReadOnlyCollection<AdminUserResponse>> GetUsersAsync(string? keyword = null, string? role = null, bool? isActive = null, CancellationToken cancellationToken = default)
    {
        var query = users.Query().Include(x => x.Profile).AsQueryable();
        if (!string.IsNullOrWhiteSpace(keyword)) { var term = keyword.Trim().ToLower(); query = query.Where(x => x.Email.ToLower().Contains(term) || (x.Profile != null && x.Profile.FullName.ToLower().Contains(term))); }
        if (!string.IsNullOrWhiteSpace(role)) { var value = NormalizeRole(role); query = query.Where(x => x.Role == value); }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        return (await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<AdminUserDashboardResponse> GetDashboardAsync(CancellationToken ct = default)
    {
        var query = users.Query();
        return new(await query.CountAsync(ct), await query.CountAsync(x => x.IsActive, ct), await query.CountAsync(x => !x.IsActive, ct), await query.CountAsync(x => x.IsEmailVerified, ct), await query.CountAsync(x => x.Role == "ADMIN", ct), await query.CountAsync(x => x.Role == "USER", ct));
    }

    public async Task<AdminUserResponse> GetUserByIdAsync(int id, CancellationToken cancellationToken = default) => ToResponse(await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, cancellationToken) ?? throw new ServiceException("User was not found.", 404));

    public async Task<AdminUserResponse> CreateAsync(AdminCreateUserRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.Query().AnyAsync(x => x.Email == email, ct)) throw new ServiceException("Email is already registered.", 409);
        await ValidateDietTypeAsync(request.DietTypeId, ct);
        var now = DateTime.UtcNow;
        var user = new User { Email = email, PasswordHash = PasswordHasher.Hash(request.Password), Role = NormalizeRole(request.Role), IsActive = request.IsActive, IsEmailVerified = request.IsEmailVerified, CreatedAt = now, UpdatedAt = now };
        user.Profile = BuildProfile(user.UserId, request.FullName, request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel, request.DietTypeId, request.Latitude, request.Longitude);
        await users.AddAsync(user, ct); await users.SaveChangesAsync(ct);
        return ToResponse(user);
    }

    public async Task<AdminUserResponse> UpdateAsync(int id, int actorId, AdminUpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw new ServiceException("User was not found.", 404);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.Query().AnyAsync(x => x.Email == email && x.UserId != id, ct)) throw new ServiceException("Email is already registered.", 409);
        await ValidateDietTypeAsync(request.DietTypeId, ct);
        var newRole = NormalizeRole(request.Role);
        if (id == actorId && (!request.IsActive || newRole != "ADMIN")) throw new ServiceException("You cannot deactivate or remove your own administrator role.", 409);
        if (user.Role == "ADMIN" && newRole != "ADMIN" && await users.Query().CountAsync(x => x.Role == "ADMIN" && x.IsActive, ct) <= 1) throw new ServiceException("The last active administrator cannot be demoted.", 409);
        user.Email = email; user.Role = newRole; user.IsActive = request.IsActive; user.IsEmailVerified = request.IsEmailVerified; user.UpdatedAt = DateTime.UtcNow;
        if (user.Profile is null) { user.Profile = BuildProfile(id, request.FullName, request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel, request.DietTypeId, request.Latitude, request.Longitude); await profiles.AddAsync(user.Profile, ct); }
        else ApplyProfile(user.Profile, request.FullName, request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel, request.DietTypeId, request.Latitude, request.Longitude);
        await users.SaveChangesAsync(ct); return ToResponse(user);
    }
    public async Task UpdateStatusAsync(int id, int actorId, bool isActive, CancellationToken cancellationToken = default) { if (id == actorId && !isActive) throw new ServiceException("You cannot deactivate your own administrator account.", 409); var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); if (!isActive && user.Role == "ADMIN" && await users.Query().CountAsync(x => x.Role == "ADMIN" && x.IsActive, cancellationToken) <= 1) throw new ServiceException("The last active administrator cannot be deactivated.", 409); user.IsActive = isActive; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
    public async Task DeleteAsync(int id, int actorId, CancellationToken cancellationToken = default) { if (id == actorId) throw new ServiceException("You cannot delete your own administrator account.", 409); var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); if (user.Role == "ADMIN" && user.IsActive && await users.Query().CountAsync(x => x.Role == "ADMIN" && x.IsActive, cancellationToken) <= 1) throw new ServiceException("The last active administrator cannot be deleted.", 409); user.IsActive = false; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }

    private async Task ValidateDietTypeAsync(int? id, CancellationToken ct) { if (id.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == id, ct)) throw new ServiceException("Diet type was not found.", 404); }
    private static string NormalizeRole(string role) { var value = role.Trim().ToUpperInvariant(); if (!AllowedRoles.Contains(value)) throw new ServiceException("Role must be USER or ADMIN."); return value; }
    private static UserProfile BuildProfile(int id, string name, string? sex, DateTime? birthDate, decimal? height, decimal? weight, string? activity, int? dietTypeId, decimal? latitude, decimal? longitude) { var profile = new UserProfile { UserId = id }; ApplyProfile(profile, name, sex, birthDate, height, weight, activity, dietTypeId, latitude, longitude); return profile; }
    private static void ApplyProfile(UserProfile profile, string name, string? sex, DateTime? birthDate, decimal? height, decimal? weight, string? activity, int? dietTypeId, decimal? latitude, decimal? longitude) { profile.FullName = name.Trim(); profile.Sex = sex?.Trim(); profile.BirthDate = birthDate; profile.HeightCm = height; profile.WeightKg = weight; profile.ActivityLevel = activity?.Trim(); profile.DietTypeId = dietTypeId; profile.Latitude = latitude; profile.Longitude = longitude; }
    private static AdminUserResponse ToResponse(User user) => new(user.UserId, user.Email, user.Role, user.Profile?.FullName ?? "Unknown user", user.Profile?.Sex, user.Profile?.BirthDate, user.Profile?.HeightCm, user.Profile?.WeightKg, user.Profile?.ActivityLevel, user.Profile?.DietTypeId, user.Profile?.Latitude, user.Profile?.Longitude, user.IsActive, user.IsEmailVerified, user.CreatedAt, user.UpdatedAt);
}
