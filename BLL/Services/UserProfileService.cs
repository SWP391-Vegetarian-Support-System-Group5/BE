using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class UserProfileService(IRepository<UserProfile> profiles, IRepository<DietType> dietTypes) : IUserProfileService
{
    public async Task<UserResponse> UpdateAsync(int userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (request.BirthYear.HasValue && (request.BirthYear.Value < 1 || request.BirthYear.Value > VietnamTime.Now.Year))
            throw new ServiceException("Birth year must be between 1 and the current year.", 400);

        var profile = await profiles.Query().Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new ServiceException("User profile was not found.", 404);
        if (request.DietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == request.DietTypeId.Value, cancellationToken))
            throw new ServiceException("Diet type was not found.", 404);
        if (request.BirthYear.HasValue && NutritionProfileRules.Age(request.BirthYear, DateTime.UtcNow) is null)
            throw new ServiceException("Birth year must correspond to an age between 18 and 120.", 400);

        profile.FullName = request.FullName.Trim();
        profile.Sex = string.IsNullOrWhiteSpace(request.Sex) ? null : FixedValues.Sex(request.Sex);
        profile.BirthYear = request.BirthYear;
        profile.HeightCm = request.HeightCm;
        profile.WeightKg = request.WeightKg;
        profile.ActivityLevel = string.IsNullOrWhiteSpace(request.ActivityLevel) ? null : FixedValues.ActivityLevel(request.ActivityLevel);
        profile.GoalWeightKg = request.GoalWeightKg;
        NutritionProfileRules.Validate(profile);
        profile.DietTypeId = request.DietTypeId;
        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        profile.LivingAddress = string.IsNullOrWhiteSpace(request.LivingAddress) ? null : request.LivingAddress.Trim();
        profile.User.UpdatedAt = DateTime.UtcNow;
        await profiles.SaveChangesAsync(cancellationToken);
        return AuthService.ToResponse(profile.User);
    }

    public async Task<UserResponse> UpdateLocationAsync(int userId, UpdateUserLocationRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await profiles.Query().Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new ServiceException("User profile was not found.", 404);

        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        if (!string.IsNullOrWhiteSpace(request.LivingAddress)) profile.LivingAddress = request.LivingAddress.Trim();
        profile.User.UpdatedAt = DateTime.UtcNow;
        await profiles.SaveChangesAsync(cancellationToken);
        return AuthService.ToResponse(profile.User);
    }
}
