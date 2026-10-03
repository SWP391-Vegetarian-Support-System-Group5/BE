using System.Security.Cryptography;
using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class AuthService(IRepository<User> users, IRepository<DietType> dietTypes) : IAuthService
{
    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.Query().AnyAsync(x => x.Email == email, cancellationToken))
            throw new ServiceException("Email is already registered.", 409);
        if (request.DietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == request.DietTypeId, cancellationToken))
            throw new ServiceException("Diet type was not found.", 404);

        var sex = FixedValues.Sex(request.Sex);
        var activityLevel = FixedValues.ActivityLevel(request.ActivityLevel);
        var healthGoal = FixedValues.HealthGoal(request.HealthGoal);

        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "USER",
            FullName = request.FullName.Trim(),
            Age = request.Age,
            Sex = sex,
            HeightCm = request.HeightCm,
            WeightKg = request.WeightKg,
            ActivityLevel = activityLevel,
            HealthGoal = healthGoal,
            DietTypeId = request.DietTypeId,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        await users.AddAsync(user, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);
        return ToResponse(user);
    }

    public async Task<UserResponse> ValidateCredentialsAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            throw new ServiceException("Invalid email or password.", 401);
        if (!user.IsActive)
            throw new ServiceException("This account is inactive.", 403);
        return ToResponse(user);
    }

    public async Task<UserResponse?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await users.Query().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return user is null ? null : ToResponse(user);
    }

    public static UserResponse ToResponse(User user) => new(user.UserId, user.Email, user.Role, user.FullName, user.Age, user.Sex, user.HeightCm, user.WeightKg, user.ActivityLevel, user.HealthGoal, user.DietTypeId, user.IsActive);
}

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
