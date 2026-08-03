using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(int id);
    Task<List<Tag>> GetAllAsync();
    Task AddAsync(Tag tag);
    Task UpdateAsync(Tag tag);
    Task DeleteAsync(Tag tag);
}

