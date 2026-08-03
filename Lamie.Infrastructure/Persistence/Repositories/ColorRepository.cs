using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence.Repositories;

public class ColorRepository : IColorRepository
{
    private readonly AppDbContext _context;

    public ColorRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Color?> GetByIdAsync(int id)
    {
        return await _context.Colors
            .Include(c => c.Translations)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<Color>> GetAllAsync()
    {
        return await _context.Colors
            .Include(c => c.Translations)
            .ToListAsync();
    }

    public async Task AddAsync(Color color)
    {
        await _context.Colors.AddAsync(color);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Color color)
    {
        _context.Colors.Update(color);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Color color)
    {
        _context.ColorTranslations.RemoveRange(color.Translations);
        _context.Colors.Remove(color);
        await _context.SaveChangesAsync();
    }
}

