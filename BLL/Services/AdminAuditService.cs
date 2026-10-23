using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class AdminAuditService(IRepository<AdminAuditLog> auditLogs) : IAdminAuditService
{
    public async Task RecordAsync(int adminUserId, string method, string path, int statusCode, CancellationToken cancellationToken = default)
    {
        await auditLogs.AddAsync(new AdminAuditLog
        {
            AdminUserId = adminUserId,
            Method = method[..Math.Min(method.Length, 10)],
            Path = path[..Math.Min(path.Length, 500)],
            StatusCode = statusCode,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
        await auditLogs.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AdminAuditLogResponse>> GetAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (page < 1) throw new ServiceException("Page must be at least 1.");
        if (pageSize is < 1 or > 100) throw new ServiceException("Page size must be between 1 and 100.");
        return await auditLogs.Query()
            .AsNoTracking()
            .Include(x => x.AdminUser).ThenInclude(x => x.Profile)
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminAuditLogResponse(x.AdminAuditLogId, x.AdminUserId, x.AdminUser.Email, x.AdminUser.Profile != null ? x.AdminUser.Profile.FullName : "Unknown administrator", x.Method, x.Path, x.StatusCode, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
