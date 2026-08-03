using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence.Repositories;

public class LanguageRepository : ILanguageRepository
{
    private readonly AppDbContext _context;

    public LanguageRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> ExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return await _context.Languages
            .AsNoTracking()
            .AnyAsync(l => l.Code == code && l.IsActive, cancellationToken);
    }

    public async Task<List<Language>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Languages
            .AsNoTracking()
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<Language?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return await _context.Languages
            .FirstOrDefaultAsync(l => l.Code == code, cancellationToken);
    }

    public async Task AddAsync(Language language, CancellationToken cancellationToken = default)
    {
        await _context.Languages.AddAsync(language, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Language language, CancellationToken cancellationToken = default)
    {
        _context.Languages.Update(language);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Language language, CancellationToken cancellationToken = default)
    {
        _context.Languages.Remove(language);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

