using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence.Repositories;

public class OccasionRepository : IOccasionRepository
{
    private readonly AppDbContext _context;

    public OccasionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Occasion?> GetByIdAsync(int id)
    {
        return await _context.Occasions
            .Include(o => o.Translations)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<Occasion>> GetAllAsync()
    {
        return await _context.Occasions
            .Include(o => o.Translations)
            .ToListAsync();
    }

    public async Task AddAsync(Occasion occasion)
    {
        await _context.Occasions.AddAsync(occasion);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Occasion occasion)
    {
        _context.Occasions.Update(occasion);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Occasion occasion)
    {
        _context.OccasionTranslations.RemoveRange(occasion.Translations);
        _context.Occasions.Remove(occasion);
        await _context.SaveChangesAsync();
    }
}

