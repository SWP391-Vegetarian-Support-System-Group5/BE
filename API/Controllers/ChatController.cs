using System.Security.Claims;
using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using System.Security.Cryptography;

namespace API.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController(IChatService chatService, IDataProtectionProvider protection) : ControllerBase
{
    private const string GuestCookie = "veggiemate_guest_trial";
    private static readonly SemaphoreSlim GuestCreationGate = new(1, 1);
    private static readonly SemaphoreSlim[] MessageGates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private sealed record GuestTrial(int SessionId, string Token);
    private int? CurrentUserId()
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
    }

    private string CurrentRole() => User.Identity?.IsAuthenticated == true ? User.GetRoleName() : "GUEST";

    [HttpPost("sessions")]
    public async Task<ActionResult<ChatSessionResponse>> CreateSession(CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId.HasValue)
        {
            var memberSession = await chatService.CreateSessionAsync(userId, ct);
            return Created($"/api/chat/sessions/{memberSession.ChatSessionId}", memberSession);
        }
        await GuestCreationGate.WaitAsync(ct);
        try
        {
            var protector = protection.CreateProtector("VeggieMate.GuestTrial.v1");
            if (Request.Cookies.TryGetValue(GuestCookie, out var cookie))
            {
                GuestTrial? trial;
                try { trial = JsonSerializer.Deserialize<GuestTrial>(protector.Unprotect(cookie)); }
                catch (Exception error) when (error is CryptographicException or JsonException) { trial = null; }
                if (trial is not null)
                {
                    var existing = await chatService.GetSessionAsync(trial.SessionId, null, "GUEST", trial.Token, ct);
                    return Ok(existing with { GuestAccessToken = trial.Token });
                }
            }
            var session = await chatService.CreateSessionAsync(null, ct);
            Response.Cookies.Append(GuestCookie, protector.Protect(JsonSerializer.Serialize(new GuestTrial(session.ChatSessionId, session.GuestAccessToken!))), new CookieOptions
            {
                HttpOnly = true, Secure = Request.IsHttps,
                SameSite = Request.IsHttps ? SameSiteMode.None : SameSiteMode.Lax,
                Path = "/api/chat", MaxAge = TimeSpan.FromDays(365), IsEssential = true
            });
            return Created($"/api/chat/sessions/{session.ChatSessionId}", session);
        }
        finally { GuestCreationGate.Release(); }
    }

    [Authorize]
    [HttpGet("sessions")]
    public Task<IReadOnlyCollection<ChatSessionResponse>> GetSessions(CancellationToken ct) =>
        chatService.GetSessionsAsync(User.GetRequiredUserId(), ct);

    [HttpGet("sessions/{id:int}")]
    public Task<ChatSessionResponse> GetSession(int id, [FromHeader(Name = "X-Guest-Chat-Token")] string? guestToken, CancellationToken ct) =>
        chatService.GetSessionAsync(id, CurrentUserId(), CurrentRole(), guestToken, ct);

    [HttpPost("sessions/{id:int}/messages")]
    public async Task<ChatReplyResponse> SendMessage(int id, ChatMessageRequest request, [FromHeader(Name = "X-Guest-Chat-Token")] string? guestToken, CancellationToken ct)
    {
        var gate = MessageGates[(int)((uint)id % (uint)MessageGates.Length)];
        await gate.WaitAsync(ct);
        try { return await chatService.SendAsync(id, CurrentUserId(), CurrentRole(), guestToken, request, ct); }
        finally { gate.Release(); }
    }
}
