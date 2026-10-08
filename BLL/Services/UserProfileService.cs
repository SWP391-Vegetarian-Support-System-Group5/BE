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
        var profile = await profiles.Query().Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new ServiceException("User profile was not found.", 404);
        if (request.DietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == request.DietTypeId.Value, cancellationToken))
            throw new ServiceException("Diet type was not found.", 404);

        profile.FullName = request.FullName.Trim();
        profile.Sex = string.IsNullOrWhiteSpace(request.Sex) ? null : FixedValues.Sex(request.Sex);
        profile.BirthDate = request.BirthDate?.Date;
        profile.HeightCm = request.HeightCm;
        profile.WeightKg = request.WeightKg;
        profile.ActivityLevel = string.IsNullOrWhiteSpace(request.ActivityLevel) ? null : FixedValues.ActivityLevel(request.ActivityLevel);
        profile.HealthGoal = string.IsNullOrWhiteSpace(request.HealthGoal) ? null : FixedValues.HealthGoal(request.HealthGoal);
        profile.DietTypeId = request.DietTypeId;
        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        profile.User.UpdatedAt = DateTime.UtcNow;
        await profiles.SaveChangesAsync(cancellationToken);
        return AuthService.ToResponse(profile.User);
    }
}
