using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BLL.Common;

internal static class ContentWriteLock
{
    // The lock belongs to the transaction and is released on commit or disposal.
    public static async Task<IDbContextTransaction> AcquireAsync(
        VegetarianDbContext db, int? postId, int? commentId, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsRelational())
            {
                if (commentId.HasValue)
                    await db.Database.SqlQuery<int>($"SELECT CommentId FROM Comments WHERE CommentId = {commentId.Value} FOR UPDATE").ToListAsync(ct);
                else
                    await db.Database.SqlQuery<int>($"SELECT PostId FROM Posts WHERE PostId = {postId!.Value} FOR UPDATE").ToListAsync(ct);
            }
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
