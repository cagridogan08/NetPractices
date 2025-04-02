
namespace MinimalAPIPractices.Domain.Service
{
    public interface IService<T> where T : BaseEntity
    {
        Task<IEnumerable<T>> GetAll();
        Task<T?> GetById(Guid id);
        Task<T?> Create(T entity);
        Task<T?> Update(Guid id, T entity);
        Task<T?> Delete(Guid id);
    }
}
