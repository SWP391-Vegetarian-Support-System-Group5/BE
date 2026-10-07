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
    public async Task<ActionResult<EmailOtpSentResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return Accepted(result);
    }

    [HttpPost("register/verify-otp")]
    public async Task<ActionResult<ApiMessageResponse>> VerifyRegistrationOtp(VerifyEmailOtpRequest request, CancellationToken cancellationToken)
    {
        await authService.VerifyRegistrationOtpAsync(request, cancellationToken);
        return Ok(new ApiMessageResponse("Email verified successfully. You can now log in."));
    }

    [HttpPost("register/resend-otp")]
    public async Task<ActionResult<EmailOtpSentResponse>> ResendRegistrationOtp(RequestEmailOtpRequest request, CancellationToken cancellationToken) =>
        Accepted(await authService.ResendRegistrationOtpAsync(request, cancellationToken));

    [HttpPost("password/forgot")]
    public async Task<ActionResult<EmailOtpSentResponse>> ForgotPassword(RequestEmailOtpRequest request, CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(request, cancellationToken);
        return Accepted(new EmailOtpSentResponse(request.Email.Trim().ToLowerInvariant(), "If the email address exists, a password reset code has been sent.", 600));
    }

    [HttpPost("password/reset")]
    public async Task<ActionResult<ApiMessageResponse>> ResetPassword(ResetPasswordWithOtpRequest request, CancellationToken cancellationToken)
    {
        await authService.ResetPasswordAsync(request, cancellationToken);
        return Ok(new ApiMessageResponse("Password changed successfully. You can now log in."));
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
