using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ModerationService(IRepository<ModerationFlag> flags, IRepository<Post> posts, IRepository<Comment> comments, IUserNotificationService notifications, ICommunityEventPublisher events) : IModerationService
{
    public Task<ModerationFlagResponse> ReportPostAsync(int postId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(postId, null, userId, request, cancellationToken);
    public Task<ModerationFlagResponse> ReportCommentAsync(int commentId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(null, commentId, userId, request, cancellationToken);
    public async Task<IReadOnlyCollection<ModerationFlagResponse>> GetAsync(CancellationToken cancellationToken = default) => (await flags.Query().OrderByDescending(x => x.FlagId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    public async Task<ModerationFlagResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default) => ToResponse(await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404));

    public async Task ReviewAsync(int id, int adminId, bool removeContent, CancellationToken cancellationToken = default)
    {
        var flag = await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404);
        if (flag.Status != "PENDING") throw new ServiceException("Moderation flag was already reviewed.", 409);
        flag.ReviewedBy = adminId;
        flag.Status = removeContent ? "REMOVED" : "APPROVED";
        var post = removeContent && flag.PostId.HasValue ? await posts.FindAsync(flag.PostId.Value) : null;
        var comment = removeContent && flag.CommentId.HasValue ? await comments.FindAsync(flag.CommentId.Value) : null;
        if (post?.Status == "REMOVED") post = null;
        if (comment?.Status == "REMOVED") comment = null;
        if (post is not null) { post.Status = "REMOVED"; post.UpdatedAt = VietnamTime.Now; }
        if (comment is not null) { comment.Status = "REMOVED"; comment.UpdatedAt = VietnamTime.Now; }
        await flags.SaveChangesAsync(cancellationToken);
        if (post is not null)
        {
            await events.PublishAsync(new CommunityEvent("postRemoved", post.PostId), cancellationToken);
            if (post.UserId != adminId) await notifications.CreateAsync(post.UserId, "POST_REMOVED", "Bài viết của bạn đã bị quản trị viên gỡ.", post.PostId, cancellationToken);
        }
        if (comment is not null)
        {
            await events.PublishAsync(new CommunityEvent("commentRemoved", comment.PostId), cancellationToken);
            if (comment.UserId != adminId) await notifications.CreateAsync(comment.UserId, "COMMENT_REMOVED", "Bình luận của bạn đã bị quản trị viên gỡ.", comment.PostId, cancellationToken);
        }
    }

    private async Task<ModerationFlagResponse> ReportAsync(int? postId, int? commentId, int userId, ReportRequest request, CancellationToken ct)
    {
        if (postId.HasValue && !await posts.Query().AnyAsync(x => x.PostId == postId, ct)) throw new ServiceException("Post was not found.", 404);
        if (commentId.HasValue && !await comments.Query().AnyAsync(x => x.CommentId == commentId, ct)) throw new ServiceException("Comment was not found.", 404);
        var flag = new ModerationFlag { PostId = postId, CommentId = commentId, Source = "USER_REPORT", ReportedBy = userId, Reason = request.Reason.Trim(), Status = "PENDING" };
        await flags.AddAsync(flag, ct);
        await flags.SaveChangesAsync(ct);
        return ToResponse(flag);
    }

    private static ModerationFlagResponse ToResponse(ModerationFlag x) => new(x.FlagId, x.PostId, x.CommentId, x.Source, x.Reason, x.Status, x.ReportedBy, x.ReviewedBy);
}

public class HealthService(IRepository<DietType> dietTypes) : IHealthService
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await dietTypes.Query().Select(x => x.DietTypeId).Take(1).ToListAsync(cancellationToken);
        return true;
    }
}
