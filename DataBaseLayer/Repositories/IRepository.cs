using System.Linq.Expressions;

namespace DataBaseLayer.Repositories;

public interface IRepository<T> where T : class
{
    T? GetById(int id);
    Task<T?> Get(Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);
    IQueryable<T> GetAll();
    void Add(T entity);
    void Update(T entity);
    void Delete(T entity);
    Task<int> SaveChanges();
}
