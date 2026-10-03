using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/users/me")]
public class CurrentUserController(IAuthService authService, IUserAllergenService userAllergenService, IPostService postService) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<UserResponse>> Get(CancellationToken ct) => (await authService.GetUserAsync(User.GetRequiredUserId(), ct)) is { } user ? Ok(user) : NotFound();
    [HttpGet("allergens")] public Task<UserAllergensResponse> GetAllergens(CancellationToken ct) => userAllergenService.GetAsync(User.GetRequiredUserId(), ct);
    [HttpPut("allergens")] public Task<UserAllergensResponse> UpdateAllergens(UpdateUserAllergensRequest request, CancellationToken ct) => userAllergenService.UpdateAsync(User.GetRequiredUserId(), request, ct);
    [HttpGet("bookmarks")] public Task<IReadOnlyCollection<PostResponse>> GetBookmarks(CancellationToken ct) => postService.GetBookmarksAsync(User.GetRequiredUserId(), ct);
}
