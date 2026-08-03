using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext _context;

    public CollectionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Collection?> GetByIdAsync(int id)
    {
        return await _context.Collections
            .Include(c => c.Translations)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<Collection>> GetAllAsync()
    {
        return await _context.Collections
            .Include(c => c.Translations)
            .ToListAsync();
    }

    public async Task AddAsync(Collection collection)
    {
        await _context.Collections.AddAsync(collection);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Collection collection)
    {
        _context.Collections.Update(collection);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Collection collection)
    {
        _context.CollectionTranslations.RemoveRange(collection.Translations);
        _context.Collections.Remove(collection);
        await _context.SaveChangesAsync();
    }
}

