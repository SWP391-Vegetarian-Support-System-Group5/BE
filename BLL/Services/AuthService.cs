using System.Security.Cryptography;
using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class AuthService(
    IRepository<User> users,
    IRepository<UserProfile> profiles,
    IRepository<EmailOtpCode> otpCodes,
    IEmailSender emailSender) : IAuthService
{
    private const string RegistrationPurpose = "REGISTRATION";
    private const string PasswordResetPurpose = "PASSWORD_RESET";
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromMinutes(1);
    private const int MaximumOtpAttempts = 5;

    public async Task<EmailOtpSentResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.Query().AnyAsync(x => x.Email == email, cancellationToken))
            throw new ServiceException("Email is already registered.", 409);
        var now = DateTime.UtcNow;
        var user = new User
        {
            Email = email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Role = "USER",
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            IsEmailVerified = false
        };
        await users.AddAsync(user, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);
        await profiles.AddAsync(new UserProfile { UserId = user.UserId, FullName = request.Name.Trim() }, cancellationToken);
        await profiles.SaveChangesAsync(cancellationToken);
        return await CreateAndSendOtpAsync(email, RegistrationPurpose, cancellationToken);
    }

    public async Task VerifyRegistrationOtpAsync(VerifyEmailOtpRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().SingleOrDefaultAsync(x => x.Email == email, cancellationToken)
            ?? throw new ServiceException("The verification code is invalid or has expired.", 400);

        if (user.IsEmailVerified)
            throw new ServiceException("This email address has already been verified. Please log in.", 409);

        await ValidateOtpAsync(email, RegistrationPurpose, request.OtpCode, cancellationToken);
        user.IsEmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        await users.SaveChangesAsync(cancellationToken);
    }

    public async Task<EmailOtpSentResponse> ResendRegistrationOtpAsync(RequestEmailOtpRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || user.IsEmailVerified)
            throw new ServiceException("No pending email verification was found for this address.", 404);

        return await CreateAndSendOtpAsync(email, RegistrationPurpose, cancellationToken);
    }

    public async Task RequestPasswordResetAsync(RequestEmailOtpRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().SingleOrDefaultAsync(x => x.Email == email && x.IsActive && x.IsEmailVerified, cancellationToken);
        if (user is not null)
            await CreateAndSendOtpAsync(email, PasswordResetPurpose, cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordWithOtpRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().SingleOrDefaultAsync(x => x.Email == email && x.IsActive && x.IsEmailVerified, cancellationToken)
            ?? throw new ServiceException("The verification code is invalid or has expired.", 400);

        await ValidateOtpAsync(email, PasswordResetPurpose, request.OtpCode, cancellationToken);
        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await users.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserResponse> ValidateCredentialsAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            throw new ServiceException("Invalid email or password.", 401);
        if (!user.IsEmailVerified)
            throw new ServiceException("Please verify your email address before logging in.", 403);
        if (!user.IsActive)
            throw new ServiceException("This account is inactive.", 403);
        return ToResponse(user);
    }

    public async Task<UserResponse?> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return user is null ? null : ToResponse(user);
    }

    public async Task<UserResponse> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken)
            ?? throw new ServiceException("User was not found.", 404);
        return ToResponse(user);
    }

    public static UserResponse ToResponse(User user) => new(user.UserId, user.Email, user.Role, user.Profile?.FullName ?? "Unknown user", user.Profile?.Sex, user.Profile?.BirthDate, user.Profile?.HeightCm, user.Profile?.WeightKg, user.Profile?.ActivityLevel, user.Profile?.HealthGoal, user.Profile?.DietTypeId, user.IsActive);

    private async Task<EmailOtpSentResponse> CreateAndSendOtpAsync(string email, string purpose, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var latestCode = await otpCodes.Query()
            .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestCode is not null && latestCode.CreatedAt > now.Subtract(ResendDelay))
            throw new ServiceException("Please wait one minute before requesting another verification code.", 429);

        var activeCodes = await otpCodes.Query()
            .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var code in activeCodes) code.UsedAt = now;

        var otpCode = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        await otpCodes.AddAsync(new EmailOtpCode
        {
            Email = email,
            Purpose = purpose,
            CodeHash = PasswordHasher.Hash(otpCode),
            ExpiresAt = now.Add(OtpLifetime),
            CreatedAt = now
        }, cancellationToken);
        await otpCodes.SaveChangesAsync(cancellationToken);

        await emailSender.SendOtpAsync(email, purpose, otpCode, cancellationToken);
        var message = purpose == RegistrationPurpose
            ? "A verification code has been sent to your email address."
            : "If the email address exists, a password reset code has been sent.";
        return new EmailOtpSentResponse(email, message, (int)OtpLifetime.TotalSeconds);
    }

    private async Task ValidateOtpAsync(string email, string purpose, string otpCode, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var code = await otpCodes.Query()
            .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (code is null)
            throw new ServiceException("The verification code is invalid or has already been used.", 400);

        if (code.ExpiresAt <= now)
        {
            code.UsedAt = now;
            await otpCodes.SaveChangesAsync(cancellationToken);
            throw new ServiceException("The verification code has expired. Please request a new code.", 400);
        }

        if (code.FailedAttempts >= MaximumOtpAttempts)
        {
            code.UsedAt = now;
            await otpCodes.SaveChangesAsync(cancellationToken);
            throw new ServiceException("Too many incorrect attempts. Please request a new code.", 400);
        }

        if (!PasswordHasher.Verify(otpCode, code.CodeHash))
        {
            code.FailedAttempts++;
            if (code.FailedAttempts >= MaximumOtpAttempts) code.UsedAt = now;
            await otpCodes.SaveChangesAsync(cancellationToken);
            var remainingAttempts = MaximumOtpAttempts - code.FailedAttempts;
            var message = remainingAttempts > 0
                ? $"The verification code is incorrect. You have {remainingAttempts} attempt(s) remaining."
                : "The verification code is incorrect. Please request a new code.";
            throw new ServiceException(message, 400);
        }

        code.UsedAt = now;
        await otpCodes.SaveChangesAsync(cancellationToken);
    }
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
