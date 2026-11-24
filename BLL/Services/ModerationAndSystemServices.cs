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
        if (flag.Source == "AI")
        {
            var duplicateFlags = await flags.Query().Where(x => x.FlagId != id && x.Source == "AI" && x.Status == "PENDING"
                && x.PostId == flag.PostId && x.CommentId == flag.CommentId).ToListAsync(cancellationToken);
            foreach (var duplicate in duplicateFlags)
            {
                duplicate.Status = flag.Status;
                duplicate.ReviewedBy = adminId;
            }
        }

        var post = flag.PostId.HasValue ? await posts.FindAsync(flag.PostId.Value) : null;
        var comment = flag.CommentId.HasValue
            ? await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile).SingleOrDefaultAsync(x => x.CommentId == flag.CommentId, cancellationToken)
            : null;
        var changePost = post is not null && (removeContent ? post.Status != "REMOVED" : flag.Source == "AI" && (post.Status is "DRAFT" or "PENDING_REVIEW"));
        var changeComment = comment is not null && (removeContent ? comment.Status != "REMOVED" : flag.Source == "AI" && comment.Status == "PENDING_REVIEW");
        if (changePost) { post!.Status = removeContent ? "REMOVED" : "PUBLISHED"; post.UpdatedAt = VietnamTime.Now; }
        if (changeComment) { comment!.Status = removeContent ? "REMOVED" : "PUBLISHED"; comment.UpdatedAt = VietnamTime.Now; }
        try
        {
            await flags.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ServiceException("Moderation flag changed. Reload it before reviewing.", 409);
        }

        if (!removeContent)
        {
            if (changePost && post is not null)
                await events.PublishAsync(new CommunityEvent("postCreated", post.PostId), cancellationToken);
            if (changeComment && comment is not null)
            {
                var count = await comments.Query().CountAsync(x => x.PostId == comment.PostId && x.Status == "PUBLISHED", cancellationToken);
                await events.PublishAsync(new CommunityEvent("commentCreated", comment.PostId,
                    Comment: PostService.ToCommentResponse(comment, []), CommentCount: count), cancellationToken);
            }
            return;
        }
        if (changePost && post is not null)
        {
            await events.PublishAsync(new CommunityEvent("postRemoved", post.PostId), cancellationToken);
            if (post.UserId != adminId) await notifications.CreateAsync(post.UserId, "POST_REMOVED", "Bài viết của bạn đã bị quản trị viên gỡ.", post.PostId, cancellationToken);
        }
        if (changeComment && comment is not null)
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
