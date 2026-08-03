using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface IOccasionRepository
{
    Task<Occasion?> GetByIdAsync(int id);
    Task<List<Occasion>> GetAllAsync();
    Task AddAsync(Occasion occasion);
    Task UpdateAsync(Occasion occasion);
    Task DeleteAsync(Occasion occasion);
}

