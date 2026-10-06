using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ModerationService(IRepository<ModerationFlag> flags, IRepository<Post> posts, IRepository<Comment> comments) : IModerationService
{
    public Task<ModerationFlagResponse> ReportPostAsync(int postId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(postId, null, userId, request, cancellationToken);
    public Task<ModerationFlagResponse> ReportCommentAsync(int commentId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(null, commentId, userId, request, cancellationToken);
    public async Task<IReadOnlyCollection<ModerationFlagResponse>> GetAsync(CancellationToken cancellationToken = default) => (await flags.Query().OrderByDescending(x => x.FlagId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    public async Task<ModerationFlagResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default) => ToResponse(await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404));
    public async Task ReviewAsync(int id, int adminId, bool removeContent, CancellationToken cancellationToken = default)
    {
        var flag = await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404);
        flag.ReviewedBy = adminId; flag.Status = removeContent ? "REMOVED" : "APPROVED";
        if (removeContent && flag.PostId.HasValue) { var post = await posts.FindAsync(flag.PostId.Value); if (post is not null) post.Status = "REMOVED"; }
        if (removeContent && flag.CommentId.HasValue) { var comment = await comments.FindAsync(flag.CommentId.Value); if (comment is not null) comment.Status = "REMOVED"; }
        await flags.SaveChangesAsync(cancellationToken);
    }
    private async Task<ModerationFlagResponse> ReportAsync(int? postId, int? commentId, int userId, ReportRequest request, CancellationToken ct)
    {
        if (postId.HasValue && !await posts.Query().AnyAsync(x => x.PostId == postId, ct)) throw new ServiceException("Post was not found.", 404);
        if (commentId.HasValue && !await comments.Query().AnyAsync(x => x.CommentId == commentId, ct)) throw new ServiceException("Comment was not found.", 404);
        var flag = new ModerationFlag { PostId = postId, CommentId = commentId, Source = "USER_REPORT", ReportedBy = userId, Reason = request.Reason.Trim(), Status = "PENDING" };
        await flags.AddAsync(flag, ct); await flags.SaveChangesAsync(ct); return ToResponse(flag);
    }
    private static ModerationFlagResponse ToResponse(ModerationFlag x) => new(x.FlagId, x.PostId, x.CommentId, x.Source, x.Reason, x.Status, x.ReportedBy, x.ReviewedBy);
}

public class SeedService(IRepository<DietType> dietTypes, IRepository<Category> categories, IRepository<Allergen> allergens, IRepository<User> users) : ISeedService
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await AddMissingAsync(dietTypes, ["VEGAN", "VEGETARIAN", "LACTO_VEGETARIAN", "OVO_VEGETARIAN", "LACTO_OVO_VEGETARIAN"], name => new DietType { Name = name }, x => x.Name, cancellationToken);
        await AddMissingAsync(categories, ["Food", "Recipe", "Blog", "Video"], name => new Category { Name = name, Type = name.ToUpperInvariant() }, x => x.Name, cancellationToken);
        await AddMissingAsync(allergens, ["Peanut", "Milk", "Egg", "Soy", "Gluten", "Tree Nut"], name => new Allergen { Name = name }, x => x.Name, cancellationToken);
        if (!await users.Query().AnyAsync(x => x.Email == "admin@vegetarian.local", cancellationToken))
        {
            var now = DateTime.UtcNow;
            await users.AddAsync(new User { Email = "admin@vegetarian.local", PasswordHash = PasswordHasher.Hash("Admin@123"), Role = "ADMIN", FullName = "System Administrator", CreatedAt = now, UpdatedAt = now, IsActive = true }, cancellationToken);
            await users.SaveChangesAsync(cancellationToken);
        }
    }
    private static async Task AddMissingAsync<T>(IRepository<T> repository, IEnumerable<string> names, Func<string, T> create, Func<T, string> getName, CancellationToken ct) where T : class
    {
        var existing = (await repository.Query().ToListAsync(ct)).Select(getName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.Where(name => !existing.Contains(name))) await repository.AddAsync(create(name), ct);
        await repository.SaveChangesAsync(ct);
    }
}

public class HealthService(IRepository<DietType> dietTypes) : IHealthService
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await dietTypes.Query().Select(x => x.DietTypeId).Take(1).ToListAsync(cancellationToken);
        return true;
    }
}
