using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence.Repositories;

public class StyleRepository : IStyleRepository
{
    private readonly AppDbContext _context;

    public StyleRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Style?> GetByIdAsync(int id)
    {
        return await _context.Styles
            .Include(s => s.Translations)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<List<Style>> GetAllAsync()
    {
        return await _context.Styles
            .Include(s => s.Translations)
            .ToListAsync();
    }

    public async Task AddAsync(Style style)
    {
        await _context.Styles.AddAsync(style);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Style style)
    {
        _context.Styles.Update(style);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Style style)
    {
        _context.StyleTranslations.RemoveRange(style.Translations);
        _context.Styles.Remove(style);
        await _context.SaveChangesAsync();
    }
}

