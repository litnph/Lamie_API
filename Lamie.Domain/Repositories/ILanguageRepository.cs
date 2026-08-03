using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface ILanguageRepository
{
    Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default);

    Task<List<Language>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Language?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task AddAsync(Language language, CancellationToken cancellationToken = default);

    Task UpdateAsync(Language language, CancellationToken cancellationToken = default);

    Task DeleteAsync(Language language, CancellationToken cancellationToken = default);
}

