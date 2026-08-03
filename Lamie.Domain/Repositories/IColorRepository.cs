using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface IColorRepository
{
    Task<Color?> GetByIdAsync(int id);
    Task<List<Color>> GetAllAsync();
    Task AddAsync(Color color);
    Task UpdateAsync(Color color);
    Task DeleteAsync(Color color);
}

