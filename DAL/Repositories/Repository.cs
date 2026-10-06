using DAL.Data;
using Microsoft.EntityFrameworkCore;

namespace DAL.Repositories;

public class Repository<T>(VegetarianDbContext dbContext) : IRepository<T> where T : class
{
    public IQueryable<T> Query() => dbContext.Set<T>();

    public Task<T?> FindAsync(params object[] keyValues) => dbContext.Set<T>().FindAsync(keyValues).AsTask();

    public Task AddAsync(T entity, CancellationToken cancellationToken = default) =>
        dbContext.Set<T>().AddAsync(entity, cancellationToken).AsTask();

    public void Remove(T entity) => dbContext.Set<T>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
