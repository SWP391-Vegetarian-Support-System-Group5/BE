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
        if (request.BirthYear > VietnamTime.Now.Year)
            throw new ServiceException("Birth year cannot be in the future.", 400);

        var profile = await profiles.Query().Include(x => x.User).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new ServiceException("User profile was not found.", 404);
        var selectedDiet = await dietTypes.Query().AsNoTracking().SingleOrDefaultAsync(x => x.DietTypeId == request.DietTypeId, cancellationToken)
            ?? throw new ServiceException("Diet type was not found.", 404);
        if (!VegetarianDietRules.IsSupportedDietType(selectedDiet.Name))
            throw new ServiceException("Diet type is not supported by the profile and restaurant matching rules.", 400);
        if (NutritionProfileRules.Age(request.BirthYear, DateTime.UtcNow) is null)
            throw new ServiceException("Birth year must correspond to an age between 18 and 120.", 400);

        profile.FullName = request.FullName.Trim();
        profile.Sex = FixedValues.Sex(request.Sex);
        profile.BirthYear = request.BirthYear;
        profile.HeightCm = request.HeightCm;
        profile.WeightKg = request.WeightKg;
        profile.ActivityLevel = FixedValues.ActivityLevel(request.ActivityLevel);
        profile.GoalWeightKg = request.GoalWeightKg;
        NutritionProfileRules.Validate(profile);
        profile.DietTypeId = request.DietTypeId;
        profile.Latitude = request.Latitude;
        profile.Longitude = request.Longitude;
        profile.LivingAddress = request.LivingAddress.Trim();
        profile.User.UpdatedAt = DateTime.UtcNow;
        await SaveProfileAsync(cancellationToken);
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
        await SaveProfileAsync(cancellationToken);
        return AuthService.ToResponse(profile.User);
    }

    private async Task SaveProfileAsync(CancellationToken ct)
    {
        try { await profiles.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        { throw new ServiceException("Hồ sơ hoặc ảnh đại diện vừa được cập nhật. Hãy tải lại trước khi lưu.", 409); }
    }
}
