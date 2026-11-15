using System.Security.Claims;
using System.Text.Json;
using API.Controllers;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

var protector = new EphemeralDataProtectionProvider();
int passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
string Cookie(int id = 7, string token = "old-token") => protector.CreateProtector("VeggieMate.GuestTrial.v1").Protect(JsonSerializer.Serialize(new { SessionId = id, Token = token }));
ChatController Controller(FakeChat service, string? cookie = null, int? userId = null)
{
    var context = new DefaultHttpContext();
    context.Request.Scheme = "https";
    if (cookie is not null) context.Request.Headers.Cookie = $"veggiemate_guest_trial={cookie}";
    if (userId.HasValue) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "test"));
    return new ChatController(service, protector) { ControllerContext = new ControllerContext { HttpContext = context } };
}
async Task Run(string name, Func<Task> action)
{
    await action(); passed++; Console.WriteLine($"PASS {name}");
}

await Run("new guest receives a protected replacement cookie", async () => {
    var service = new FakeChat(); var controller = Controller(service);
    var result = await controller.CreateSession(default);
    Check(result.Result is CreatedResult { StatusCode: 201 }, "Expected 201");
    Check(service.CreateCalls == 1 && service.LoadCalls == 0, "Create once");
    var cookie = controller.Response.Headers.SetCookie.ToString();
    Check(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("path=/api/chat", StringComparison.OrdinalIgnoreCase), "Cookie flags");
    var protectedValue = cookie.Split(';')[0].Split('=', 2)[1];
    using var trial = JsonDocument.Parse(protector.CreateProtector("VeggieMate.GuestTrial.v1").Unprotect(protectedValue));
    Check(trial.RootElement.GetProperty("SessionId").GetInt32() == 8 && trial.RootElement.GetProperty("Token").GetString() == "new-token", "Cookie matches new session");
});

foreach (var status in new[] { 403, 404 })
await Run($"stale cookie HTTP {status} creates one replacement", async () => {
    var service = new FakeChat { LoadFailure = new ServiceException("Chat session was not found.", status) }; var controller = Controller(service, Cookie());
    var result = await controller.CreateSession(default);
    Check(result.Result is CreatedResult { Value: ChatSessionResponse { ChatSessionId: 8 } }, "Replacement response");
    Check(service.LoadCalls == 1 && service.CreateCalls == 1, "Only one restore and create");
    Check(controller.Response.Headers.SetCookie.Count > 0, "Overwrite stale cookie");
});

await Run("valid exhausted guest retains history and quota", async () => {
    var service = new FakeChat { Existing = new(7, null, DateTime.UtcNow, Enumerable.Range(1, 5).Select(i => new ChatMessageResponse(i, "USER", "Question", DateTime.UtcNow)).ToArray()) };
    var controller = Controller(service, Cookie()); var result = await controller.CreateSession(default);
    Check(result.Result is OkObjectResult { Value: ChatSessionResponse { ChatSessionId: 7, GuestAccessToken: "old-token", Messages.Count: 5 } }, "Restore existing history and token");
    Check(service.CreateCalls == 0, "Never reset valid guest quota");
    Check(controller.Response.Headers.SetCookie.Count == 0, "No cookie replacement");
});

foreach (var cookie in new[] { "tampered-cookie", Cookie(-1), Cookie(7, ""), protector.CreateProtector("VeggieMate.GuestTrial.v1").Protect("invalid-json") })
await Run("invalid cookie is replaced without querying invalid credentials", async () => {
    var service = new FakeChat(); var result = await Controller(service, cookie).CreateSession(default);
    Check(result.Result is CreatedResult, "Invalid cookie should create");
    Check(service.LoadCalls == 0 && service.CreateCalls == 1, "Invalid credentials must not be queried");
});

foreach (var status in new[] { 429, 500, 503 })
await Run($"temporary HTTP {status} preserves cookie and does not reset trial", async () => {
    var service = new FakeChat { LoadFailure = new ServiceException("Temporary failure", status) }; var controller = Controller(service, Cookie());
    try { await controller.CreateSession(default); throw new Exception("Expected failure"); }
    catch (ServiceException error) when (error.StatusCode == status) { }
    Check(service.CreateCalls == 0 && controller.Response.Headers.SetCookie.Count == 0, "Preserve existing trial");
});

await Run("cancellation propagates and releases creation gate", async () => {
    var service = new FakeChat { LoadFailure = new OperationCanceledException() };
    try { await Controller(service, Cookie()).CreateSession(default); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
    Check(service.CreateCalls == 0, "No creation on cancellation");
    service.LoadFailure = null;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    Check((await Controller(service, Cookie()).CreateSession(timeout.Token)).Result is OkObjectResult, "Gate released");
});

await Run("member creates own session without reading guest cookie", async () => {
    var service = new FakeChat(); await Controller(service, Cookie(), 42).CreateSession(default);
    Check(service.CreatedForUser == 42 && service.LoadCalls == 0, "Authenticated path unchanged");
});

await Run("GET missing session remains 404 and does not silently create one", async () => {
    var service = new FakeChat { LoadFailure = new ServiceException("Not found", 404) };
    try { await Controller(service).GetSession(7, "old-token", default); throw new Exception("Expected 404"); }
    catch (ServiceException error) when (error.StatusCode == 404) { }
    Check(service.CreateCalls == 0, "GET is read only");
});

await Run("chat responses cannot be cached", () => {
    var attribute = (ResponseCacheAttribute?)Attribute.GetCustomAttribute(typeof(ChatController), typeof(ResponseCacheAttribute));
    Check(attribute is { NoStore: true, Location: ResponseCacheLocation.None }, "No-store response policy");
    return Task.CompletedTask;
});
Console.WriteLine($"{passed} controller tests passed (mock IChatService; no database or AI calls).");

sealed class FakeChat : IChatService
{
    public Exception? LoadFailure { get; set; }
    public int CreateCalls { get; private set; }
    public int LoadCalls { get; private set; }
    public int? CreatedForUser { get; private set; }
    public ChatSessionResponse Existing { get; set; } = new(7, null, DateTime.UtcNow, []);
    public Task<ChatSessionResponse> CreateSessionAsync(int? userId, CancellationToken cancellationToken = default)
    {
        CreateCalls++; CreatedForUser = userId;
        return Task.FromResult(new ChatSessionResponse(8, userId, DateTime.UtcNow, [], userId.HasValue ? null : "new-token"));
    }
    public Task<IReadOnlyCollection<ChatSessionResponse>> GetSessionsAsync(int? userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ChatSessionResponse>>([]);
    public Task<ChatSessionResponse> GetSessionAsync(int id, int? userId, string? role, string? guestAccessToken, CancellationToken cancellationToken = default)
    {
        LoadCalls++;
        if (LoadFailure is not null) throw LoadFailure;
        return Task.FromResult(Existing);
    }
    public Task<ChatReplyResponse> SendAsync(int id, int? userId, string? role, string? guestAccessToken, ChatMessageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
