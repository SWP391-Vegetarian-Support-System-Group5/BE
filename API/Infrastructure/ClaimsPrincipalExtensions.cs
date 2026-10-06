using System.Security.Claims;
using BLL.Common;

namespace API.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    public static int GetRequiredUserId(this ClaimsPrincipal principal) => int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : throw new ServiceException("Authenticated user identity is invalid.", 401);
    public static string GetRoleName(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.Role) ?? "USER";
}
