using System.Linq.Expressions;

namespace DAL.Repositories;

public interface IRepository<T> where T : class
{
    IQueryable<T> Query();
    Task<T?> FindAsync(params object[] keyValues);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Remove(T entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
