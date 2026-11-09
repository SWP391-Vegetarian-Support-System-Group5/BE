using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class UserAdministrationService(IRepository<User> users, DAL.Data.VegetarianDbContext db) : IUserAdministrationService
{
    public async Task<IReadOnlyCollection<UserResponse>> GetUsersAsync(CancellationToken cancellationToken = default) => (await users.Query().Include(x => x.Profile).OrderBy(x => x.UserId).ToListAsync(cancellationToken)).Select(AuthService.ToResponse).ToList();
    public async Task<UserResponse> GetUserByIdAsync(int id, CancellationToken cancellationToken = default) => AuthService.ToResponse(await users.Query().Include(x => x.Profile).SingleOrDefaultAsync(x => x.UserId == id, cancellationToken) ?? throw new ServiceException("User was not found.", 404));
    public async Task<AdminUserActivityResponse> GetActivityAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!await users.Query().AnyAsync(x => x.UserId == id, cancellationToken)) throw new ServiceException("User was not found.", 404);
        var posts = await db.Posts.AsNoTracking().Where(x => x.UserId == id && x.Status == "PUBLISHED")
            .Select(x => new { x.PostId, x.CreatedAt, x.UpdatedAt, x.VideoUrl, x.PostType }).ToListAsync(cancellationToken);
        var comments = await db.Comments.AsNoTracking().Where(x => x.UserId == id && x.Status == "PUBLISHED")
            .Select(x => new { x.CreatedAt, x.UpdatedAt }).ToListAsync(cancellationToken);
        var saved = await db.Bookmarks.CountAsync(x => x.UserId == id, cancellationToken);
        var ratings = await db.PostRatings.CountAsync(x => x.UserId == id, cancellationToken);
        var today = VietnamTime.Now.Date;
        var dates = posts.Select(x => x.CreatedAt.Date).Concat(comments.Select(x => x.CreatedAt.Date)).ToArray();
        var daily = Enumerable.Range(0, 30).Select(offset => dates.Count(date => date == today.AddDays(offset - 29))).ToArray();
        var lastActivity = posts.Select(x => x.UpdatedAt).Concat(comments.Select(x => x.UpdatedAt)).OrderByDescending(x => x).FirstOrDefault();
        return new AdminUserActivityResponse(id, posts.Count, posts.Count(x => !string.IsNullOrWhiteSpace(x.VideoUrl)), comments.Count,
            saved, posts.Count(x => x.PostType == "RECIPE"), ratings, lastActivity == default ? null : lastActivity, daily);
    }
    public async Task UpdateStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = isActive; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default) { var user = await users.FindAsync(id) ?? throw new ServiceException("User was not found.", 404); user.IsActive = false; user.UpdatedAt = DateTime.UtcNow; await users.SaveChangesAsync(cancellationToken); }
}
