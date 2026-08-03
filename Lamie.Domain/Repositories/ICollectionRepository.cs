using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id);
    Task<List<Collection>> GetAllAsync();
    Task AddAsync(Collection collection);
    Task UpdateAsync(Collection collection);
    Task DeleteAsync(Collection collection);
}

