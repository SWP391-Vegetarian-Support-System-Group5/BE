using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/users/me")]
public class CurrentUserController(IAuthService authService, IUserProfileService userProfileService, IUserAllergenService userAllergenService, IPostService postService) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<UserResponse>> Get(CancellationToken ct) => (await authService.GetUserAsync(User.GetRequiredUserId(), ct)) is { } user ? Ok(user) : NotFound();
    [HttpPut("profile")] public Task<UserResponse> UpdateProfile(UpdateUserProfileRequest request, CancellationToken ct) => userProfileService.UpdateAsync(User.GetRequiredUserId(), request, ct);
    [HttpPut("location")] public Task<UserResponse> UpdateLocation(UpdateUserLocationRequest request, CancellationToken ct) => userProfileService.UpdateLocationAsync(User.GetRequiredUserId(), request, ct);
    [HttpGet("allergens")] public Task<UserAllergensResponse> GetAllergens(CancellationToken ct) => userAllergenService.GetAsync(User.GetRequiredUserId(), ct);
    [HttpPut("allergens")] public Task<UserAllergensResponse> UpdateAllergens(UpdateUserAllergensRequest request, CancellationToken ct) => userAllergenService.UpdateAsync(User.GetRequiredUserId(), request, ct);
    [HttpGet("bookmarks")] public Task<IReadOnlyCollection<PostResponse>> GetBookmarks(CancellationToken ct) => postService.GetBookmarksAsync(User.GetRequiredUserId(), ct);
}
