using System.Security.Claims;
using BLL.Services;

namespace API.Infrastructure;

/// <summary>Records successful state-changing admin requests without storing request bodies or secrets.</summary>
public class AdminAuditMiddleware(RequestDelegate next, IServiceScopeFactory scopeFactory, ILogger<AdminAuditMiddleware> logger)
{
    private static readonly HashSet<string> WriteMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);
        if (!context.User.IsInRole("ADMIN") || !context.Request.Path.StartsWithSegments("/api/admin") || !WriteMethods.Contains(context.Request.Method) || context.Response.StatusCode >= 400) return;
        if (!int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId)) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAdminAuditService>().RecordAsync(adminUserId, context.Request.Method, context.Request.Path.Value ?? "/api/admin", context.Response.StatusCode, context.RequestAborted);
        }
        catch (Exception error)
        {
            // Audit failure is reported but must never turn a successful admin operation into a failed response.
            logger.LogError(error, "Could not persist the admin audit event for {Method} {Path}.", context.Request.Method, context.Request.Path);
        }
    }
}
