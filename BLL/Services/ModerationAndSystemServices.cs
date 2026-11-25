using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ModerationService(IRepository<ModerationFlag> flags, IRepository<Post> posts, IRepository<Comment> comments, IUserNotificationService notifications, ICommunityEventPublisher events, DAL.Data.VegetarianDbContext db) : IModerationService
{
    public Task<ModerationFlagResponse> ReportPostAsync(int postId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(postId, null, userId, request, cancellationToken);
    public Task<ModerationFlagResponse> ReportCommentAsync(int commentId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(null, commentId, userId, request, cancellationToken);
    public async Task<IReadOnlyCollection<ModerationFlagResponse>> GetAsync(CancellationToken cancellationToken = default) => (await flags.Query().OrderByDescending(x => x.FlagId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    public async Task<ModerationFlagResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default) => ToResponse(await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404));

    public async Task ReviewAsync(int id, int adminId, bool removeContent, CancellationToken cancellationToken = default)
    {
        var target = await flags.Query().Where(x => x.FlagId == id)
            .Select(x => new { x.PostId, x.CommentId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ServiceException("Moderation flag was not found.", 404);
        await using var transaction = await ContentWriteLock.AcquireAsync(db, target.PostId, target.CommentId, cancellationToken);
        var flag = await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404);
        if (flag.Status != "PENDING" && !(removeContent && flag.Status is "APPROVED" or "REMOVED"))
            throw new ServiceException("Moderation flag was already reviewed.", 409);
        var post = flag.PostId.HasValue ? await posts.FindAsync(flag.PostId.Value) : null;
        var comment = flag.CommentId.HasValue
            ? await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile).SingleOrDefaultAsync(x => x.CommentId == flag.CommentId, cancellationToken)
            : null;
        if (!removeContent && (post?.Status == "REMOVED" || comment?.Status == "REMOVED"))
            throw new ServiceException("Removed content cannot be approved.", 409);

        if (flag.Status != "REMOVED")
        {
            flag.ReviewedBy = adminId;
            flag.Status = removeContent ? "REMOVED" : "APPROVED";
        }
        if (removeContent || flag.Source == "AI")
        {
            var pendingFlags = flags.Query().Where(x => x.FlagId != id && x.Status == "PENDING"
                && x.PostId == target.PostId && x.CommentId == target.CommentId);
            if (!removeContent) pendingFlags = pendingFlags.Where(x => x.Source == "AI");
            foreach (var pending in await pendingFlags.ToListAsync(cancellationToken))
            {
                pending.Status = flag.Status;
                pending.ReviewedBy = adminId;
            }
        }
        var changePost = post is not null && (removeContent ? post.Status != "REMOVED" : flag.Source == "AI" && (post.Status is "DRAFT" or "PENDING_REVIEW"));
        var changeComment = comment is not null && (removeContent ? comment.Status != "REMOVED" : flag.Source == "AI" && comment.Status == "PENDING_REVIEW");
        if (changePost) { post!.Status = removeContent ? "REMOVED" : "PUBLISHED"; post.UpdatedAt = VietnamTime.Now; }
        if (changeComment) { comment!.Status = removeContent ? "REMOVED" : "PUBLISHED"; comment.UpdatedAt = VietnamTime.Now; }
        try
        {
            await flags.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ServiceException("Content or moderation flag changed. Reload it before reviewing.", 409);
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
        await using var transaction = await ContentWriteLock.AcquireAsync(db, postId, commentId, ct);
        if (postId.HasValue)
        {
            var post = await posts.FindAsync(postId.Value) ?? throw new ServiceException("Post was not found.", 404);
            if (post.Status == "REMOVED") throw new ServiceException("Removed content cannot be reported.", 409);
        }
        if (commentId.HasValue)
        {
            var comment = await comments.FindAsync(commentId.Value) ?? throw new ServiceException("Comment was not found.", 404);
            if (comment.UserId == userId) throw new ServiceException("You cannot report your own comment.", 403);
            if (comment.Status != "PUBLISHED" || !await posts.Query().AnyAsync(
                x => x.PostId == comment.PostId && x.Status == "PUBLISHED", ct))
                throw new ServiceException("Only published comments can be reported.", 409);
        }
        var flag = new ModerationFlag { PostId = postId, CommentId = commentId, Source = "USER_REPORT", ReportedBy = userId, Reason = request.Reason.Trim(), Status = "PENDING" };
        await flags.AddAsync(flag, ct);
        await flags.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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
