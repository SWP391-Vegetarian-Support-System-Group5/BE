using System.Net.Http.Json;
using BLL.Common;
using BLL.Services;

namespace API.Services;

public sealed class SendGridEmailSender(
    HttpClient client,
    SendGridSettings settings,
    ILogger<SendGridEmailSender> logger) : IEmailSender
{
    public async Task SendOtpAsync(string recipientEmail, string purpose, string otpCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.FromEmail))
            throw new ServiceException("Email delivery is not configured. Set SendGrid:ApiKey and SendGrid:FromEmail.", 503);

        var isRegistration = purpose == "REGISTRATION";
        var subject = isRegistration ? "Verify your Vegetarian Support System account" : "Reset your Vegetarian Support System password";
        var action = isRegistration ? "complete your registration" : "reset your password";
        var plainText = $"Your verification code is {otpCode}. Use it to {action}. It expires in 10 minutes. Do not share this code with anyone.";
        var html = $"<p>Your verification code is <strong style=\"font-size:24px;letter-spacing:4px\">{otpCode}</strong>.</p><p>Use it to {action}. It expires in 10 minutes.</p><p>Do not share this code with anyone.</p>";

        using var request = new HttpRequestMessage(HttpMethod.Post, "mail/send")
        {
            Content = JsonContent.Create(new
            {
                personalizations = new[] { new { to = new[] { new { email = recipientEmail } } } },
                from = new { email = settings.FromEmail, name = settings.FromName },
                subject,
                content = new[]
                {
                    new { type = "text/plain", value = plainText },
                    new { type = "text/html", value = html }
                }
            })
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var providerMessage = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError("SendGrid rejected OTP email with status {StatusCode}: {ProviderMessage}", (int)response.StatusCode, providerMessage);
        throw new ServiceException("Could not send the verification email. Please try again later.", 503);
    }
}
