using Microsoft.EntityFrameworkCore;
using MinimalAPIPractices.Domain;
using MinimalAPIPractices.Domain.Service;
using MinimalAPIPractices.Infastructure.Context;

namespace MinimalAPIPractices.Infastructure.Services
{
    public class BaseService<TEntity> : IService<TEntity> where TEntity : BaseEntity
    {
        private readonly ApplicationContext _context;

        public BaseService(ApplicationContext context)
        {
            _context = context;
        }

        public virtual async Task<IEnumerable<TEntity>> GetAll()
        {
            return await _context.Set<TEntity>().ToListAsync();
        }

        public virtual async Task<TEntity?> GetById(Guid id)
        {
            return await _context.Set<TEntity>().FindAsync(id);
        }

        public virtual async Task<TEntity?> Create(TEntity entity)
        {
            await _context.Set<TEntity>().AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public virtual async Task<TEntity?> Update(Guid id, TEntity entity)
        {
            var existingEntity = await _context.Set<TEntity>().FindAsync(id);

            if (existingEntity == null)
                return null;

            // Ensure the ID remains the same
            entity.Id = id;

            // Detach the existing entity from the context
            _context.Entry(existingEntity).State = EntityState.Detached;

            // Attach the new entity and mark it as modified
            _context.Set<TEntity>().Attach(entity);
            _context.Entry(entity).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return entity;
        }

        public virtual async Task<TEntity?> Delete(Guid id)
        {
            var entity = await _context.Set<TEntity>().FindAsync(id);

            if (entity == null)
                return null;

            _context.Set<TEntity>().Remove(entity);
            await _context.SaveChangesAsync();

            return entity;
        }
    }
}
