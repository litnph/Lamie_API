using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface IStyleRepository
{
    Task<Style?> GetByIdAsync(int id);
    Task<List<Style>> GetAllAsync();
    Task AddAsync(Style style);
    Task UpdateAsync(Style style);
    Task DeleteAsync(Style style);
}

