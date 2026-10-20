using BLL.Common;
using BLL.DTOs;
using DAL.Data;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class RecipeBmiService(VegetarianDbContext db, ICommunityEventPublisher events)
{
    public Task<List<BmiTypeResponse>> ListAsync(CancellationToken ct) => db.BMITypes.AsNoTracking()
        .OrderBy(x => x.BMITypeId).Select(x => new BmiTypeResponse(x.BMITypeId, x.Code, x.Name, x.Description)).ToListAsync(ct);

    public async Task<List<AdminRecipeBmiResponse>> RecipesAsync(CancellationToken ct) => (await db.Recipes.AsNoTracking()
        .Include(x => x.Post).Include(x => x.BMITypes).ThenInclude(x => x.BMIType)
        .Include(x => x.CategoryMappings)
        .Where(x => x.Post.Status != "REMOVED").OrderByDescending(x => x.PostId).ToListAsync(ct))
        .Select(x => new AdminRecipeBmiResponse(x.PostId, x.RecipeName, x.Post.Title, x.Post.Status,
            x.BMITypes.Select(m => new BmiTypeResponse(m.BMITypeId, m.BMIType.Code, m.BMIType.Name, m.BMIType.Description)).ToList(),
            x.CategoryMappings.Select(m => m.CategoryId).ToList())).ToList();

    public async Task ValidateAsync(IReadOnlyCollection<int>? ids, int actorId, CancellationToken ct)
    {
        if (ids is null) return;
        if (!await db.Users.AnyAsync(x => x.UserId == actorId && x.Role == "ADMIN" && x.IsActive, ct))
            throw new ServiceException("Only administrators can assign recipe BMI types.", 403);
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length > 3 || await db.BMITypes.CountAsync(x => distinct.Contains(x.BMITypeId) && (x.Code == "UNDERWEIGHT" || x.Code == "NORMAL" || x.Code == "OBESE"), ct) != distinct.Length)
            throw new ServiceException("One or more BMI types are invalid.", 400);
    }

    // Caller owns the transaction and SaveChanges, including recipe create/update.
    public async Task SetMappingsAsync(int recipeId, IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        if (ids is null) return;
        var selected = ids.ToHashSet();
        var existing = await db.RecipeBMITypes.Where(x => x.RecipeId == recipeId).ToListAsync(ct);
        db.RecipeBMITypes.RemoveRange(existing.Where(x => !selected.Contains(x.BMITypeId)));
        var oldIds = existing.Select(x => x.BMITypeId).ToHashSet();
        foreach (var id in selected.Except(oldIds)) db.RecipeBMITypes.Add(new RecipeBMIType { RecipeId = recipeId, BMITypeId = id });
    }

    public async Task UpdateAsync(int recipeId, IReadOnlyCollection<int> ids, int actorId, CancellationToken ct)
    {
        await ValidateAsync(ids, actorId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Recipes.AnyAsync(x => x.PostId == recipeId && x.Post.Status != "REMOVED", ct)) throw new ServiceException("Recipe was not found.", 404);
        await SetMappingsAsync(recipeId, ids, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await events.PublishAsync(new CommunityEvent("postUpdated", recipeId), ct);
    }
}
