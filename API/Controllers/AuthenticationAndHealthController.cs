using API.Services;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthenticationController(IAuthService authService, IJwtTokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await authService.RegisterAsync(request, cancellationToken);
        return Created($"/api/users/{user.UserId}", user);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await authService.ValidateCredentialsAsync(request, cancellationToken);
        return Ok(new AuthResponse(tokenService.Create(user), user));
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();
}

[ApiController]
[Route("api/health")]
public class HealthController(IHealthService healthService) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "Healthy" });

    [HttpGet("db")]
    public async Task<IActionResult> Database(CancellationToken cancellationToken) => await healthService.CanConnectAsync(cancellationToken) ? Ok(new { status = "Healthy" }) : StatusCode(503, new { success = false, message = "Database is unavailable.", errors = Array.Empty<string>() });
}
