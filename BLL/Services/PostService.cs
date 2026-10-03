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
    IRepository<Tag> tags,
    IRepository<Category> categories,
    IRepository<ModerationFlag> moderationFlags,
    IModerationAiService moderationAiService) : IPostService
{
    public async Task<PostResponse> CreateAsync(int userId, CreatePostRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateCategoryAndTagsAsync(request.CategoryId, request.TagIds, cancellationToken);
        var post = new Post { UserId = userId, CategoryId = request.CategoryId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = FixedValues.PostType(request.PostType), VideoUrl = request.VideoUrl?.Trim(), Status = "PUBLISHED" };
        await posts.AddAsync(post, cancellationToken);
        await posts.SaveChangesAsync(cancellationToken);
        await SetTagsAsync(post, request.TagIds, cancellationToken);
        await CreateAiPostFlagAsync(post.PostId, post.Content, cancellationToken);
        return await GetByIdAsync(post.PostId, userId, "USER", cancellationToken);
    }

    public async Task<IReadOnlyCollection<PostResponse>> GetAsync(string? keyword, int? categoryId, string? postType, int? tagId, string? status, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var query = BaseQuery();
        if (!isAdmin) query = query.Where(x => x.Status == "PUBLISHED");
        else if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(keyword)) { var term = keyword.Trim().ToLower(); query = query.Where(x => x.Title.ToLower().Contains(term) || x.Content.ToLower().Contains(term) || (x.Category != null && x.Category.Name.ToLower().Contains(term)) || x.PostTags.Any(pt => pt.Tag.Name.ToLower().Contains(term))); }
        if (categoryId.HasValue) query = query.Where(x => x.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(postType)) query = query.Where(x => x.PostType == postType.Trim().ToUpperInvariant());
        if (tagId.HasValue) query = query.Where(x => x.PostTags.Any(pt => pt.TagId == tagId));
        return (await query.OrderByDescending(x => x.PostId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<PostResponse> GetByIdAsync(int id, int? actorId, string? role, CancellationToken cancellationToken = default)
    {
        var post = await BaseQuery().SingleOrDefaultAsync(x => x.PostId == id, cancellationToken) ?? throw new ServiceException("Post was not found.", 404);
        if (post.Status != "PUBLISHED" && post.UserId != actorId && !string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase)) throw new ServiceException("Post was not found.", 404);
        return ToResponse(post);
    }

    public async Task UpdateAsync(int id, int actorId, string role, UpdatePostRequest request, CancellationToken cancellationToken = default)
    {
        var post = await posts.FindAsync(id) ?? throw new ServiceException("Post was not found.", 404);
        EnsureOwner(post.UserId, actorId, role);
        await ValidateCategoryAndTagsAsync(request.CategoryId, request.TagIds, cancellationToken);
        post.Title = request.Title.Trim(); post.Content = request.Content.Trim(); post.PostType = FixedValues.PostType(request.PostType); post.CategoryId = request.CategoryId; post.VideoUrl = request.VideoUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(request.Status) && role == "ADMIN") post.Status = FixedValues.PostStatus(request.Status);
        await posts.SaveChangesAsync(cancellationToken); await SetTagsAsync(post, request.TagIds, cancellationToken);
    }

    public async Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var post = await posts.FindAsync(id) ?? throw new ServiceException("Post was not found.", 404);
        EnsureOwner(post.UserId, actorId, role); post.Status = "REMOVED"; await posts.SaveChangesAsync(cancellationToken);
    }

    public async Task<CommentResponse> AddCommentAsync(int postId, int userId, int? parentCommentId, CommentRequest request, CancellationToken cancellationToken = default)
    {
        if (!await posts.Query().AnyAsync(x => x.PostId == postId && x.Status == "PUBLISHED", cancellationToken)) throw new ServiceException("Post was not found.", 404);
        if (parentCommentId.HasValue && !await comments.Query().AnyAsync(x => x.CommentId == parentCommentId && x.PostId == postId, cancellationToken)) throw new ServiceException("Parent comment was not found.", 404);
        var comment = new Comment { PostId = postId, UserId = userId, ParentCommentId = parentCommentId, Content = request.Content.Trim(), Status = "PUBLISHED" };
        await comments.AddAsync(comment, cancellationToken); await comments.SaveChangesAsync(cancellationToken);
        await CreateAiCommentFlagAsync(comment.CommentId, comment.Content, cancellationToken);
        var created = await comments.Query().Include(x => x.User).SingleAsync(x => x.CommentId == comment.CommentId, cancellationToken);
        return ToCommentResponse(created, []);
    }

    public async Task<CommentResponse> AddReplyAsync(int parentCommentId, int userId, CommentRequest request, CancellationToken cancellationToken = default)
    {
        var parent = await comments.FindAsync(parentCommentId) ?? throw new ServiceException("Parent comment was not found.", 404);
        return await AddCommentAsync(parent.PostId, userId, parentCommentId, request, cancellationToken);
    }

    public async Task<IReadOnlyCollection<CommentResponse>> GetCommentsAsync(int postId, CancellationToken cancellationToken = default)
    {
        if (!await posts.Query().AnyAsync(x => x.PostId == postId && x.Status == "PUBLISHED", cancellationToken)) throw new ServiceException("Post was not found.", 404);
        var items = await comments.Query().Include(x => x.User).Where(x => x.PostId == postId && x.Status == "PUBLISHED").OrderBy(x => x.CommentId).ToListAsync(cancellationToken);
        return items.Where(x => x.ParentCommentId is null).Select(x => ToCommentTree(x, items)).ToList();
    }

    public async Task<IReadOnlyCollection<CommentResponse>> GetAllCommentsAsync(CancellationToken cancellationToken = default)
    {
        var items = await comments.Query().Include(x => x.User).OrderByDescending(x => x.CommentId).ToListAsync(cancellationToken);
        return items.Select(x => ToCommentResponse(x, [])).ToList();
    }

    public async Task UpdateCommentAsync(int id, int actorId, string role, CommentRequest request, CancellationToken cancellationToken = default)
    {
        var comment = await comments.FindAsync(id) ?? throw new ServiceException("Comment was not found.", 404);
        EnsureOwner(comment.UserId, actorId, role); comment.Content = request.Content.Trim(); await comments.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCommentAsync(int id, int actorId, string role, CancellationToken cancellationToken = default)
    {
        var comment = await comments.FindAsync(id) ?? throw new ServiceException("Comment was not found.", 404);
        EnsureOwner(comment.UserId, actorId, role); comment.Status = "REMOVED"; await comments.SaveChangesAsync(cancellationToken);
    }

    public async Task SetRatingAsync(int postId, int userId, int rating, bool updateOnly, CancellationToken cancellationToken = default)
    {
        if (!await posts.Query().AnyAsync(x => x.PostId == postId && x.Status == "PUBLISHED", cancellationToken)) throw new ServiceException("Post was not found.", 404);
        var existing = await ratings.FindAsync(postId, userId);
        if (updateOnly && existing is null) throw new ServiceException("Rating was not found.", 404);
        if (!updateOnly && existing is not null) throw new ServiceException("Rating already exists. Use PUT to update it.", 409);
        if (existing is null) { await ratings.AddAsync(new PostRating { PostId = postId, UserId = userId, Rating = rating }, cancellationToken); } else existing.Rating = rating;
        await ratings.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRatingAsync(int postId, int userId, CancellationToken cancellationToken = default)
    {
        var rating = await ratings.FindAsync(postId, userId) ?? throw new ServiceException("Rating was not found.", 404); ratings.Remove(rating); await ratings.SaveChangesAsync(cancellationToken);
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
        (await BaseQuery().Where(x => x.Bookmarks.Any(b => b.UserId == userId)).ToListAsync(cancellationToken)).Select(ToResponse).ToList();

    private IQueryable<Post> BaseQuery() => posts.Query().Include(x => x.User).Include(x => x.Category).Include(x => x.PostTags).ThenInclude(x => x.Tag).Include(x => x.Ratings);
    private async Task SetTagsAsync(Post post, List<int>? tagIds, CancellationToken ct)
    {
        var ids = (tagIds ?? []).Distinct().ToHashSet();
        var current = await posts.Query().Include(x => x.PostTags).SingleAsync(x => x.PostId == post.PostId, ct);
        current.PostTags.Clear();
        foreach (var id in ids) current.PostTags.Add(new PostTag { PostId = post.PostId, TagId = id });
        await posts.SaveChangesAsync(ct);
    }
    private async Task ValidateCategoryAndTagsAsync(int? categoryId, List<int>? tagIds, CancellationToken ct)
    {
        if (categoryId.HasValue && !await categories.Query().AnyAsync(x => x.CategoryId == categoryId.Value, ct)) throw new ServiceException("Category was not found.", 404);
        var ids = (tagIds ?? []).Distinct().ToHashSet();
        if (ids.Count > 0 && await tags.Query().CountAsync(x => ids.Contains(x.TagId), ct) != ids.Count) throw new ServiceException("One or more tags were not found.", 404);
    }
    private static PostResponse ToResponse(Post x) => new(x.PostId, x.UserId, x.User.FullName, x.CategoryId, x.Category?.Name, x.Title, x.Content, x.PostType, x.VideoUrl, x.Status, x.PostTags.Select(pt => pt.Tag.Name).ToList(), x.Ratings.Count == 0 ? 0 : x.Ratings.Average(r => r.Rating));
    private static CommentResponse ToCommentTree(Comment comment, IReadOnlyCollection<Comment> all) => ToCommentResponse(comment, all.Where(x => x.ParentCommentId == comment.CommentId).Select(x => ToCommentTree(x, all)).ToList());
    private static CommentResponse ToCommentResponse(Comment x, IReadOnlyCollection<CommentResponse> replies) => new(x.CommentId, x.PostId, x.UserId, x.User.FullName, x.ParentCommentId, x.Content, x.Status, replies);
    private static void EnsureOwner(int ownerId, int actorId, string role) { if (ownerId != actorId && role != "ADMIN") throw new ServiceException("You do not have permission to modify this content.", 403); }
    private async Task CreateAiPostFlagAsync(int postId, string content, CancellationToken ct)
    {
        var reason = await moderationAiService.FlagReasonAsync(content, ct);
        if (reason is null) return;
        await moderationFlags.AddAsync(new ModerationFlag { PostId = postId, Source = "AI", Reason = reason, Status = "PENDING" }, ct);
        await moderationFlags.SaveChangesAsync(ct);
    }
    private async Task CreateAiCommentFlagAsync(int commentId, string content, CancellationToken ct)
    {
        var reason = await moderationAiService.FlagReasonAsync(content, ct);
        if (reason is null) return;
        await moderationFlags.AddAsync(new ModerationFlag { CommentId = commentId, Source = "AI", Reason = reason, Status = "PENDING" }, ct);
        await moderationFlags.SaveChangesAsync(ct);
    }
}
