using BLL.Common;
using Microsoft.AspNetCore.Mvc;

namespace API.Infrastructure;

public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger, IWebHostEnvironment environment)
{
    public async Task Invoke(HttpContext context)
    {
        try { await next(context); }
        catch (ServiceException exception)
        {
            logger.LogWarning(exception, "Request failed with a business validation error.");
            await WriteAsync(context, exception.StatusCode, exception.Message, exception.Errors);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API error.");
            var errors = environment.IsDevelopment() ? [exception.GetBaseException().Message] : Array.Empty<string>();
            await WriteAsync(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred.", errors);
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string message, IReadOnlyCollection<string> errors)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { success = false, message, errors });
    }
}
