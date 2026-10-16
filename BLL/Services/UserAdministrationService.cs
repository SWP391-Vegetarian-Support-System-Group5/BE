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
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim().ToLower();
            query = query.Where(x => x.Email.ToLower().Contains(search) || (x.Profile != null && x.Profile.FullName.ToLower().Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = NormalizeRole(role);
            query = query.Where(x => x.Role == normalizedRole);
        }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        return (await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken)).Select(ToAdminResponse).ToList();
    }

    public async Task<AdminUserDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var query = users.Query();
        return new AdminUserDashboardResponse(
            await query.CountAsync(cancellationToken),
            await query.CountAsync(x => x.IsActive, cancellationToken),
            await query.CountAsync(x => !x.IsActive, cancellationToken),
            await query.CountAsync(x => x.IsEmailVerified, cancellationToken),
            await query.CountAsync(x => x.Role == "ADMIN", cancellationToken),
            await query.CountAsync(x => x.Role == "USER", cancellationToken));
    }

    public async Task<AdminUserResponse> GetUserByIdAsync(int id, CancellationToken cancellationToken = default) =>
        ToAdminResponse(await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, cancellationToken)
            ?? throw new ServiceException("User was not found.", 404));

    public async Task<AdminUserResponse> CreateAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.Query().AnyAsync(x => x.Email == email, cancellationToken)) throw new ServiceException("Email is already registered.", 409);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        var now = DateTime.UtcNow;
        var user = new User { Email = email, PasswordHash = PasswordHasher.Hash(request.Password), Role = NormalizeRole(request.Role), IsActive = request.IsActive, IsEmailVerified = request.IsEmailVerified, CreatedAt = now, UpdatedAt = now };
        await users.AddAsync(user, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);
        var profile = CreateProfile(user.UserId, request.FullName, request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel, request.HealthGoal, request.DietTypeId, request.Latitude, request.Longitude);
        await profiles.AddAsync(profile, cancellationToken);
        await profiles.SaveChangesAsync(cancellationToken);
        user.Profile = profile;
        return ToAdminResponse(user);
    }

    public async Task<AdminUserResponse> UpdateAsync(int id, AdminUpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, cancellationToken) ?? throw new ServiceException("User was not found.", 404);
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.Query().AnyAsync(x => x.Email == email && x.UserId != id, cancellationToken)) throw new ServiceException("Email is already registered.", 409);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        user.Email = email;
        user.Role = NormalizeRole(request.Role);
        user.IsActive = request.IsActive;
        user.IsEmailVerified = request.IsEmailVerified;
        user.UpdatedAt = DateTime.UtcNow;
        if (user.Profile is null)
        {
            user.Profile = CreateProfile(id, request.FullName, request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel, request.HealthGoal, request.DietTypeId, request.Latitude, request.Longitude);
            await profiles.AddAsync(user.Profile, cancellationToken);
        }
        else ApplyProfile(user.Profile, request.FullName, request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel, request.HealthGoal, request.DietTypeId, request.Latitude, request.Longitude);
        await users.SaveChangesAsync(cancellationToken);
        return ToAdminResponse(user);
    }

    public async Task UpdateStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = isActive; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = false; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }

    private async Task ValidateDietTypeAsync(int? id, CancellationToken ct)
    {
        if (id.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == id && x.IsActive, ct)) throw new ServiceException("Diet type was not found or is inactive.");
    }

    private static string NormalizeRole(string role)
    {
        var value = role.Trim().ToUpperInvariant();
        if (!AllowedRoles.Contains(value)) throw new ServiceException("Role must be USER or ADMIN.");
        return value;
    }

    private static UserProfile CreateProfile(int id, string name, string? sex, DateTime? birthDate, decimal? height, decimal? weight, string? activity, string? goal, int? dietTypeId, decimal? latitude, decimal? longitude)
    {
        var profile = new UserProfile { UserId = id };
        ApplyProfile(profile, name, sex, birthDate, height, weight, activity, goal, dietTypeId, latitude, longitude);
        return profile;
    }

    private static void ApplyProfile(UserProfile profile, string name, string? sex, DateTime? birthDate, decimal? height, decimal? weight, string? activity, string? goal, int? dietTypeId, decimal? latitude, decimal? longitude)
    {
        profile.FullName = name.Trim(); profile.Sex = sex?.Trim(); profile.BirthDate = birthDate; profile.HeightCm = height; profile.WeightKg = weight;
        profile.ActivityLevel = activity?.Trim(); profile.HealthGoal = goal?.Trim(); profile.DietTypeId = dietTypeId; profile.Latitude = latitude; profile.Longitude = longitude;
    }

    private static AdminUserResponse ToAdminResponse(User user) => new(user.UserId, user.Email, user.Role, user.Profile?.FullName ?? "Unknown user", user.Profile?.Sex, user.Profile?.BirthDate, user.Profile?.HeightCm, user.Profile?.WeightKg, user.Profile?.ActivityLevel, user.Profile?.HealthGoal, user.Profile?.DietTypeId, user.Profile?.Latitude, user.Profile?.Longitude, user.IsActive, user.IsEmailVerified, user.CreatedAt, user.UpdatedAt);
}
