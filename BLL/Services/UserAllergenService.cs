using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Data;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace BLL.Services;

public class UserAllergenService(IRepository<UserAllergen> userAllergens, IRepository<Allergen> allergens, IRepository<User> users, VegetarianDbContext db, IAllergenNameValidator validator) : IUserAllergenService
{
    public async Task<UserAllergensResponse> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var items = await userAllergens.Query().Include(x => x.Allergen).Where(x => x.UserId == userId).OrderBy(x => x.Allergen.Name).ToListAsync(cancellationToken);
        return new(userId, items.Select(x => new CatalogItemResponse(x.AllergenId, x.Allergen.Name, null)).ToList());
    }
    public async Task<UserAllergensResponse> UpdateAsync(int userId, UpdateUserAllergensRequest request, CancellationToken cancellationToken = default)
    {
        if (!await users.Query().AnyAsync(x => x.UserId == userId, cancellationToken)) throw new ServiceException("User was not found.", 404);
        var ids = request.AllergenIds.Distinct().ToHashSet();
        if (ids.Count != await allergens.Query().CountAsync(x => ids.Contains(x.AllergenId), cancellationToken)) throw new ServiceException("One or more allergens were not found.", 404);
        foreach (var item in await userAllergens.Query().Where(x => x.UserId == userId).ToListAsync(cancellationToken)) userAllergens.Remove(item);
        foreach (var id in ids) await userAllergens.AddAsync(new UserAllergen { UserId = userId, AllergenId = id }, cancellationToken);
        await userAllergens.SaveChangesAsync(cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    public async Task<AllergenSubmissionResponse> AddCustomAsync(int userId, string name, CancellationToken cancellationToken = default)
    {
        if (!await users.Query().AnyAsync(x => x.UserId == userId, cancellationToken))
            throw new ServiceException("Không tìm thấy tài khoản.", 404);
        if (string.IsNullOrWhiteSpace(name))
            throw new ServiceException("Vui lòng nhập tên nguyên liệu gây dị ứng.", 422);

        var catalog = await allergens.Query().AsNoTracking().ToListAsync(cancellationToken);
        if (AllergenLexicon.FindExisting(name, catalog) is { } existing)
            throw new ServiceException($"Dị ứng này trùng hoặc đồng nghĩa với '{existing}' đã có. Hãy chọn mục có sẵn.", 409);
        var pending = await db.AllergenSubmissions.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == "PENDING").ToListAsync(cancellationToken);
        EnsureNotPending(name, pending);
        var validated = await validator.ValidateAsync(name, catalog, cancellationToken);
        EnsureNotPending(validated.Name, pending);
        var latestCatalog = await allergens.Query().AsNoTracking().ToListAsync(cancellationToken);
        foreach (var value in new[] { name, validated.Name })
            if (AllergenLexicon.FindExisting(value, latestCatalog) is { } addedMeanwhile)
                throw new ServiceException($"Dị ứng này vừa có trong danh mục: '{addedMeanwhile}'. Hãy chọn mục có sẵn.", 409);

        var pendingKey = $"{userId}:{AllergenLexicon.DuplicateKey(validated.Name)}";
        if (await db.AllergenSubmissions.AnyAsync(x => x.PendingKey == pendingKey, cancellationToken))
            throw new ServiceException("Bạn đã gửi dị ứng này và đang chờ quản trị viên duyệt.", 409);
        var submission = new AllergenSubmission
        {
            UserId = userId, SubmittedName = name.Trim(), Name = validated.Name,
            PendingKey = pendingKey, SubmittedPendingKey = $"{userId}:{AllergenLexicon.DuplicateKey(name)}", CreatedAt = DateTime.UtcNow
        };
        db.AllergenSubmissions.Add(submission);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlConnector.MySqlException { Number: 1062 })
        {
            throw new ServiceException("Bạn đã gửi dị ứng này và đang chờ quản trị viên duyệt.", 409);
        }
        return ToResponse(submission);
    }

    private static void EnsureNotPending(string name, IEnumerable<AllergenSubmission> pending)
    {
        var names = pending.SelectMany(x => new[] { new Allergen { Name = x.Name }, new Allergen { Name = x.SubmittedName } });
        if (AllergenLexicon.FindExisting(name, names) is not null)
            throw new ServiceException("Bạn đã gửi dị ứng này và đang chờ quản trị viên duyệt.", 409);
    }

    public async Task<IReadOnlyCollection<AllergenSubmissionResponse>> GetSubmissionsAsync(int userId, CancellationToken cancellationToken = default) =>
        (await db.AllergenSubmissions.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(cancellationToken)).Select(ToResponse).ToList();

    public async Task<IReadOnlyCollection<AllergenSubmissionResponse>> GetReviewQueueAsync(string? status, CancellationToken cancellationToken = default)
    {
        status = string.IsNullOrWhiteSpace(status) ? "PENDING" : status;
        var query = db.AllergenSubmissions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim().ToUpperInvariant();
            if (status is not ("PENDING" or "APPROVED" or "REJECTED"))
                throw new ServiceException("Trạng thái phải là PENDING, APPROVED hoặc REJECTED.", 400);
            query = query.Where(x => x.Status == status);
        }
        return (await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    }

    public async Task<AllergenSubmissionResponse> ReviewAsync(int id, int adminId, bool approve, string? reason, CancellationToken cancellationToken = default)
    {
        if (!await users.Query().AnyAsync(x => x.UserId == adminId && x.Role == "ADMIN" && x.IsActive, cancellationToken))
            throw new ServiceException("Chỉ quản trị viên đang hoạt động được duyệt dị ứng.", 403);
        reason = reason?.Trim();
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000))
            throw new ServiceException("Vui lòng nhập lý do từ chối từ 1 đến 1000 ký tự.", 400);

        // Keep the catalog insertion and decision atomic. The status
        // concurrency token also prevents a second reviewer overwriting a decision.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var submission = await db.AllergenSubmissions.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new ServiceException("Không tìm thấy yêu cầu dị ứng.", 404);
            if (submission.Status != "PENDING")
                throw new ServiceException("Yêu cầu này đã được xử lý. Vui lòng tải lại danh sách.", 409);

            if (approve)
            {
                var catalog = await allergens.Query().ToListAsync(cancellationToken);
                var existingName = AllergenLexicon.FindExisting(submission.Name, catalog)
                    ?? AllergenLexicon.FindExisting(submission.SubmittedName, catalog);
                var allergen = existingName is null ? null : catalog.First(x => x.Name == existingName);
                if (allergen is null)
                {
                    allergen = new Allergen { Name = submission.Name };
                    db.Allergens.Add(allergen);
                    await db.SaveChangesAsync(cancellationToken);
                }
                submission.AllergenId = allergen.AllergenId;
            }
            submission.Status = approve ? "APPROVED" : "REJECTED";
            submission.RejectionReason = approve ? null : reason;
            submission.PendingKey = null;
            submission.SubmittedPendingKey = null;
            submission.ReviewedByUserId = adminId;
            submission.ReviewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return ToResponse(submission);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new ServiceException("Yêu cầu vừa được quản trị viên khác xử lý. Vui lòng tải lại danh sách.", 409);
        }
        catch (Exception ex) when (ex.GetBaseException() is MySqlConnector.MySqlException { Number: 1062 or 1213 or 1205 })
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw new ServiceException("Dữ liệu dị ứng vừa thay đổi. Vui lòng tải lại và thử duyệt lại.", 409);
        }
    }

    private static AllergenSubmissionResponse ToResponse(AllergenSubmission x) => new(x.Id, x.UserId, x.SubmittedName, x.Name, x.Status,
        x.Status switch
        {
            "APPROVED" => "Dị ứng đã được duyệt. Bạn có thể chọn mục này trong danh sách và lưu hồ sơ.",
            "REJECTED" => $"Yêu cầu dị ứng không hợp lệ: {x.RejectionReason}",
            _ => "Yêu cầu dị ứng đã được gửi, đang chờ quản trị viên duyệt."
        }, x.RejectionReason, x.AllergenId, x.CreatedAt, x.ReviewedAt, x.ReviewedByUserId);
}
