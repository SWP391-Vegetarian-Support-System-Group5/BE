using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class PostService(
    IRepository<Post> posts,
    IRepository<Comment> comments,
    IRepository<PostRating> ratings,
    IRepository<Bookmark> bookmarks,
    IRepository<ModerationFlag> moderationFlags,
    DAL.Data.VegetarianDbContext db,
    IModerationAiService moderationAiService,
    IUserNotificationService notifications,
    ICommunityEventPublisher communityEvents) : IPostService
{
    public async Task<PostResponse> CreateAsync(int userId, CreatePostRequest request, CancellationToken cancellationToken = default)
    {
        var videoUrl = request.VideoUrl?.Trim();
        var now = VietnamTime.Now;
        var reason = await moderationAiService.FlagReasonAsync($"Tiêu đề: {request.Title}\nNội dung: {request.Content}", cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var post = new Post { UserId = userId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = FixedValues.PostType(request.PostType), VideoUrl = videoUrl, ThumbnailUrl = MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(videoUrl), Status = reason is null ? "PUBLISHED" : "DRAFT", CreatedAt = now, UpdatedAt = now };
        await posts.AddAsync(post, cancellationToken);
        await posts.SaveChangesAsync(cancellationToken);
        if (reason is not null) await SaveAiPostFlagAsync(post.PostId, reason, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var response = await GetByIdAsync(post.PostId, userId, "USER", cancellationToken);
        if (reason is null) await communityEvents.PublishAsync(new CommunityEvent("postCreated", post.PostId, Post: response), cancellationToken);
        return response;
    }

    public async Task<IReadOnlyCollection<PostResponse>> GetAsync(string? keyword, int? categoryId, string? postType, string? categoryType, string? status, bool isAdmin, int? actorId = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        var query = BaseQuery();
        if (!isAdmin) query = query.Where(x => x.Status == "PUBLISHED");
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(keyword)) { var term = keyword.Trim().ToLower(); query = query.Where(x => x.Title.ToLower().Contains(term) || (x.Recipe != null && x.Recipe.RecipeName.ToLower().Contains(term)) || x.Content.ToLower().Contains(term)); }
        if (categoryId.HasValue) query = query.Where(x => x.Recipe != null && x.Recipe.CategoryMappings.Any(m => m.CategoryId == categoryId && m.Category.IsActive));
        if (!string.IsNullOrWhiteSpace(categoryType)) { var type = RecipeCategoryService.ValidateType(categoryType); query = query.Where(x => x.Recipe != null && x.Recipe.CategoryMappings.Any(m => m.Category.Type == type && m.Category.IsActive)); }
        if (!string.IsNullOrWhiteSpace(postType)) query = query.Where(x => x.PostType == postType.Trim().ToUpperInvariant());
        query = query.OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.PostId);
        if (limit is > 0) query = query.Take(limit.Value);
        return (await query.ToListAsync(cancellationToken)).Select(x => ToResponse(x, actorId)).ToList();
    }

    public async Task<IReadOnlyCollection<PostResponse>> GetMineAsync(int userId, CancellationToken cancellationToken = default)
    {
        return (await BaseQuery().Where(x => x.UserId == userId && x.Status != "REMOVED").OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.PostId).ToListAsync(cancellationToken))
            .Select(x => ToResponse(x, userId))
            .ToList();
    }

    public async Task<PostResponse> GetByIdAsync(int id, int? actorId, string? role, CancellationToken cancellationToken = default)
    {
        var post = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Post was not found.", 404);
        if (post.Status != "PUBLISHED" && post.UserId != actorId && !string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)) throw new ServiceException("Post was not found.", 404);
        return ToResponse(post, actorId);
    }

    public async Task UpdateAsync(int id, int actorId, string role, UpdatePostRequest request, CancellationToken cancellationToken = default)
    {
        var post = await posts.FindAsync(id) ?? throw new ServiceException("Post was not found.", 404);
        EnsureOwner(post.UserId, actorId, role);
        if (post.Status == "REMOVED") throw new ServiceException("Bài đã bị gỡ không thể đăng lại.", 409);
        var reason = await moderationAiService.FlagReasonAsync($"Tiêu đề: {request.Title}\nNội dung: {request.Content}", cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var wasPublished = post.Status == "PUBLISHED";
        var videoUrl = request.VideoUrl?.Trim();
        post.Title = request.Title.Trim(); post.Content = request.Content.Trim(); post.PostType = FixedValues.PostType(request.PostType); post.VideoUrl = videoUrl; post.ThumbnailUrl = MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(videoUrl); post.UpdatedAt = VietnamTime.Now;
        post.Status = reason is null ? "PUBLISHED" : "DRAFT";
        if (reason is not null) await SaveAiPostFlagAsync(id, reason, cancellationToken);
        else await ResolveAiPostFlagsAsync(id, cancellationToken);
        await posts.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (post.Status == "PUBLISHED") await communityEvents.PublishAsync(new CommunityEvent(wasPublished ? "postUpdated" : "postCreated", id, Post: await GetByIdAsync(id, actorId, role, cancellationToken)), cancellationToken);
        else if (wasPublished) await communityEvents.PublishAsync(new CommunityEvent("postRemoved", id), cancellationToken);
    }

    public async Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var post = await posts.FindAsync(id) ?? throw new ServiceException("Post was not found.", 404);
        EnsureOwner(post.UserId, actorId, role);
        if (post.Status == "REMOVED") return;
        post.Status = "REMOVED"; post.UpdatedAt = VietnamTime.Now; await posts.SaveChangesAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("postRemoved", id), cancellationToken);
        if (role == "ADMIN" && post.UserId != actorId) await notifications.CreateAsync(post.UserId, "POST_REMOVED", "Bài viết của bạn đã bị quản trị viên gỡ.", id, cancellationToken);
    }

    public async Task<CommentResponse> AddCommentAsync(int postId, int userId, int? parentCommentId, CommentRequest request, CancellationToken cancellationToken = default)
    {
        var postOwnerId = await posts.Query().Where(x => x.PostId == postId && x.Status == "PUBLISHED").Select(x => (int?)x.UserId).SingleOrDefaultAsync(cancellationToken);
        if (!postOwnerId.HasValue) throw new ServiceException("Post was not found.", 404);
        if (parentCommentId.HasValue && !await comments.Query().AnyAsync(x => x.CommentId == parentCommentId && x.PostId == postId && x.Status == "PUBLISHED", cancellationToken)) throw new ServiceException("Parent comment was not found.", 404);
        var now = VietnamTime.Now;
        var comment = new Comment { PostId = postId, UserId = userId, ParentCommentId = parentCommentId, Content = request.Content.Trim(), Status = "PUBLISHED", CreatedAt = now, UpdatedAt = now };
        await comments.AddAsync(comment, cancellationToken); await comments.SaveChangesAsync(cancellationToken);
        await CreateAiCommentFlagAsync(comment.CommentId, comment.Content, cancellationToken);
        var created = await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile).SingleAsync(x => x.CommentId == comment.CommentId, cancellationToken);
        var response = ToCommentResponse(created, []);
        await communityEvents.PublishAsync(new CommunityEvent("commentCreated", postId, Comment: response, CommentCount: await CountPublishedCommentsAsync(postId, cancellationToken)), cancellationToken);
        if (postOwnerId != userId) await notifications.CreateAsync(postOwnerId.Value, "POST_COMMENTED", "Có bình luận mới về bài viết của bạn.", postId, cancellationToken);
        return response;
    }

    public async Task<CommentResponse> AddReplyAsync(int parentCommentId, int userId, CommentRequest request, CancellationToken cancellationToken = default)
    {
        var parent = await comments.FindAsync(parentCommentId) ?? throw new ServiceException("Parent comment was not found.", 404);
        return await AddCommentAsync(parent.PostId, userId, parentCommentId, request, cancellationToken);
    }

    public async Task<IReadOnlyCollection<CommentResponse>> GetCommentsAsync(int postId, CancellationToken cancellationToken = default)
    {
        if (!await posts.Query().AnyAsync(x => x.PostId == postId && x.Status == "PUBLISHED", cancellationToken)) throw new ServiceException("Post was not found.", 404);
        var items = await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile).Where(x => x.PostId == postId && x.Status == "PUBLISHED").OrderBy(x => x.CommentId).ToListAsync(cancellationToken);
        return items.Where(x => x.ParentCommentId is null).Select(x => ToCommentTree(x, items)).ToList();
    }

    public async Task<IReadOnlyCollection<CommentResponse>> GetAllCommentsAsync(CancellationToken cancellationToken = default)
    {
        var items = await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile).OrderByDescending(x => x.CommentId).ToListAsync(cancellationToken);
        return items.Select(x => ToCommentResponse(x, [])).ToList();
    }

    public async Task UpdateCommentAsync(int id, int actorId, string role, CommentRequest request, CancellationToken cancellationToken = default)
    {
        var comment = await comments.FindAsync(id) ?? throw new ServiceException("Comment was not found.", 404);
        EnsureOwner(comment.UserId, actorId, role); comment.Content = request.Content.Trim(); comment.UpdatedAt = VietnamTime.Now; await comments.SaveChangesAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("commentUpdated", comment.PostId, Comment: await GetCommentResponseAsync(id, cancellationToken), CommentCount: await CountPublishedCommentsAsync(comment.PostId, cancellationToken)), cancellationToken);
    }

    public async Task DeleteCommentAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var comment = await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile)
            .SingleOrDefaultAsync(x => x.CommentId == id, cancellationToken) ?? throw new ServiceException("Comment was not found.", 404);
        EnsureOwner(comment.UserId, actorId, role);
        if (comment.Status == "REMOVED") return;
        comment.Status = "REMOVED"; comment.UpdatedAt = VietnamTime.Now; await comments.SaveChangesAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("commentRemoved", comment.PostId, Comment: ToCommentResponse(comment, []), CommentCount: await CountPublishedCommentsAsync(comment.PostId, cancellationToken)), cancellationToken);
        if (role == "ADMIN" && comment.UserId != actorId) await notifications.CreateAsync(comment.UserId, "COMMENT_REMOVED", "Bình luận của bạn đã bị quản trị viên gỡ.", comment.PostId, cancellationToken);
    }

    public async Task SetCommentVisibilityAsync(int id, bool hidden, CancellationToken cancellationToken = default)
    {
        var comment = await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile)
            .SingleOrDefaultAsync(x => x.CommentId == id, cancellationToken) ?? throw new ServiceException("Comment was not found.", 404);
        if (comment.Status == "REMOVED") throw new ServiceException("Removed comments cannot change visibility.", 409);
        if ((comment.Status == "HIDDEN") == hidden) return;
        comment.Status = hidden ? "HIDDEN" : "PUBLISHED";
        comment.UpdatedAt = VietnamTime.Now;
        await comments.SaveChangesAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent(hidden ? "commentRemoved" : "commentUpdated", comment.PostId,
            Comment: ToCommentResponse(comment, []), CommentCount: await CountPublishedCommentsAsync(comment.PostId, cancellationToken)), cancellationToken);
        if (hidden) await notifications.CreateAsync(comment.UserId, "COMMENT_REMOVED", "Bình luận của bạn đã bị quản trị viên ẩn.", comment.PostId, cancellationToken);
    }

    public async Task SetRatingAsync(int postId, int userId, int rating, bool updateOnly, CancellationToken cancellationToken = default)
    {
        var postOwnerId = await posts.Query().Where(x => x.PostId == postId && x.Status == "PUBLISHED").Select(x => (int?)x.UserId).SingleOrDefaultAsync(cancellationToken);
        if (!postOwnerId.HasValue) throw new ServiceException("Post was not found.", 404);
        var existing = await ratings.FindAsync(postId, userId);
        if (updateOnly && existing is null) throw new ServiceException("Rating was not found.", 404);
        if (!updateOnly && existing is not null) throw new ServiceException("Rating already exists. Use PUT to update it.", 409);
        if (existing is null) { await ratings.AddAsync(new PostRating { PostId = postId, UserId = userId, Rating = rating }, cancellationToken); } else existing.Rating = rating;
        await ratings.SaveChangesAsync(cancellationToken);
        var values = await ratings.Query().Where(x => x.PostId == postId).Select(x => x.Rating).ToListAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("ratingUpdated", postId, AverageRating: values.Count == 0 ? 0 : values.Average(), RatingCount: values.Count), cancellationToken);
        if (!updateOnly && postOwnerId != userId) await notifications.CreateAsync(postOwnerId.Value, "POST_RATED", "Có người vừa đánh giá bài viết của bạn.", postId, cancellationToken);
    }

    public async Task DeleteRatingAsync(int postId, int userId, CancellationToken cancellationToken = default)
    {
        var rating = await ratings.FindAsync(postId, userId) ?? throw new ServiceException("Rating was not found.", 404); ratings.Remove(rating); await ratings.SaveChangesAsync(cancellationToken);
        var values = await ratings.Query().Where(x => x.PostId == postId).Select(x => x.Rating).ToListAsync(cancellationToken);
        await communityEvents.PublishAsync(new CommunityEvent("ratingUpdated", postId, AverageRating: values.Count == 0 ? 0 : values.Average(), RatingCount: values.Count), cancellationToken);
    }
    public async Task<double> GetRatingAsync(int postId, CancellationToken cancellationToken = default)
    {
        if (!await posts.Query().AnyAsync(x => x.PostId == postId, cancellationToken)) throw new ServiceException("Post was not found.", 404);
        var values = await ratings.Query().Where(x => x.PostId == postId).Select(x => x.Rating).ToListAsync(cancellationToken); return values.Count == 0 ? 0 : values.Average();
    }
    public async Task AddBookmarkAsync(int postId, int userId, CancellationToken cancellationToken = default)
    {
        if (!await posts.Query().AnyAsync(x => x.PostId == postId && x.Status == "PUBLISHED", cancellationToken)) throw new ServiceException("Post was not found.", 404);
        if (await bookmarks.Query().AnyAsync(x => x.PostId == postId && x.UserId == userId, cancellationToken)) throw new ServiceException("Post is already bookmarked.", 409);
        await bookmarks.AddAsync(new Bookmark { PostId = postId, UserId = userId }, cancellationToken); await bookmarks.SaveChangesAsync(cancellationToken);
    }
    public async Task DeleteBookmarkAsync(int postId, int userId, CancellationToken cancellationToken = default)
    {
        var bookmark = await bookmarks.FindAsync(userId, postId) ?? throw new ServiceException("Bookmark was not found.", 404); bookmarks.Remove(bookmark); await bookmarks.SaveChangesAsync(cancellationToken);
    }
    public async Task<IReadOnlyCollection<PostResponse>> GetBookmarksAsync(int userId, CancellationToken cancellationToken = default) =>
        (await BaseQuery().Where(x => x.Status == "PUBLISHED" && x.Bookmarks.Any(b => b.UserId == userId)).ToListAsync(cancellationToken)).Select(x => ToResponse(x, userId)).ToList();

    private IQueryable<Post> BaseQuery() => posts.Query().AsSplitQuery().Include(x => x.User).ThenInclude(x => x.Profile).Include(x => x.Ratings).Include(x => x.Comments).Include(x => x.ModerationFlags).Include(x => x.Recipe).ThenInclude(x => x!.DietCompatibilities).ThenInclude(x => x.DietType);
    private static PostResponse ToResponse(Post x, int? actorId = null) => new(
        x.PostId,
        x.UserId,
        x.User.Profile?.FullName ?? "Unknown user",
        x.Title,
        x.Content,
        x.PostType,
        x.VideoUrl,
        x.ThumbnailUrl ?? MediaUrlHelpers.BuildCloudinaryVideoThumbnailUrl(x.VideoUrl),
        x.Status,
        x.Ratings.Count == 0 ? 0 : x.Ratings.Average(r => r.Rating),
        x.Ratings.Count,
        x.Comments.Count(c => c.Status == "PUBLISHED"),
        actorId.HasValue ? x.Ratings.FirstOrDefault(r => r.UserId == actorId.Value)?.Rating : null,
        x.CreatedAt,
        x.UpdatedAt,
        x.Recipe?.DietCompatibilities.Where(d => d.IsCompatible).OrderBy(d => d.DietTypeId).Select(d => new PostDietTypeResponse(d.DietTypeId, d.DietType.Name)).ToList() ?? [],
        actorId == x.UserId ? x.ModerationFlags.Where(f => f.Source == "AI" && f.Status == "PENDING").OrderByDescending(f => f.FlagId).FirstOrDefault()?.Reason : null);
    private static CommentResponse ToCommentTree(Comment comment, IReadOnlyCollection<Comment> all) => ToCommentResponse(comment, all.Where(x => x.ParentCommentId == comment.CommentId).Select(x => ToCommentTree(x, all)).ToList());
    private static CommentResponse ToCommentResponse(Comment x, IReadOnlyCollection<CommentResponse> replies) => new(x.CommentId, x.PostId, x.UserId, x.User.Profile?.FullName ?? "Unknown user", x.ParentCommentId, x.Content, x.Status, replies, x.CreatedAt, x.UpdatedAt);
    private async Task<int> CountPublishedCommentsAsync(int postId, CancellationToken ct) => await comments.Query().CountAsync(x => x.PostId == postId && x.Status == "PUBLISHED", ct);
    private async Task<CommentResponse> GetCommentResponseAsync(int id, CancellationToken ct)
    {
        var comment = await comments.Query().Include(x => x.User).ThenInclude(x => x.Profile).SingleAsync(x => x.CommentId == id, ct);
        return ToCommentResponse(comment, []);
    }
    private static void EnsureOwner(int ownerId, int actorId, string role) { if (ownerId != actorId && role != "ADMIN") throw new ServiceException("You do not have permission to modify this content.", 403); }
    private async Task SaveAiPostFlagAsync(int postId, string reason, CancellationToken ct)
    {
        var existing = await moderationFlags.Query().Where(f => f.PostId == postId && f.Source == "AI" && f.Status == "PENDING").FirstOrDefaultAsync(ct);
        if (existing is null) await moderationFlags.AddAsync(new ModerationFlag { PostId = postId, Source = "AI", Reason = reason, Status = "PENDING" }, ct);
        else existing.Reason = reason;
        await moderationFlags.SaveChangesAsync(ct);
    }
    private async Task ResolveAiPostFlagsAsync(int postId, CancellationToken ct)
    {
        var pending = await moderationFlags.Query().Where(f => f.PostId == postId && f.Source == "AI" && f.Status == "PENDING").ToListAsync(ct);
        foreach (var flag in pending) flag.Status = "APPROVED";
    }
    private async Task CreateAiCommentFlagAsync(int commentId, string content, CancellationToken ct)
    {
        var reason = await moderationAiService.FlagReasonAsync(content, ct);
        if (reason is null) return;
        await moderationFlags.AddAsync(new ModerationFlag { CommentId = commentId, Source = "AI", Reason = reason, Status = "PENDING" }, ct);
        await moderationFlags.SaveChangesAsync(ct);
    }
}
