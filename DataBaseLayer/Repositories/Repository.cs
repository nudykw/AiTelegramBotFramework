using DataBaseLayer.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace DataBaseLayer.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly DbContext _dbContext;

    public Repository(StoreContext dbContext)
    {
        _dbContext = dbContext;
    }

    public T? GetById(int id) => _dbContext.Set<T>().Find(id);
    public Task<T?> Get(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return GetAll().FirstOrDefaultAsync(predicate, cancellationToken);
    }
    public IQueryable<T> GetAll()
    {
        return _dbContext.Set<T>();
    }

    public void Add(T entity)
    {
        _dbContext.Set<T>().Add(entity);
    }

    public void Update(T entity)
    {
        _dbContext.Set<T>().Update(entity);
    }
    public void Delete(T entity)
    {
        _dbContext.Set<T>().Remove(entity);
    }
    public Task<int> SaveChanges()
    {
        return _dbContext.SaveChangesAsync();
    }
    public IQueryable<TResult> SqlQuery<TResult>([NotParameterized] FormattableString sql)
    {
        return _dbContext.Database.SqlQuery<TResult>(sql);
    }
}

