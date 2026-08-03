using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Lamie.Infrastructure.Persistence.Repositories;

public sealed class ProductTypeRepository : IProductTypeRepository
{
    private readonly AppDbContext _context;

    public ProductTypeRepository(AppDbContext context) => _context = context;

    public Task<ProductType?> GetByIdAsync(int id) => _context.ProductTypes
        .Include(productType => productType.Translations)
        .FirstOrDefaultAsync(productType => productType.Id == id);

    public Task<List<ProductType>> GetAllAsync() => _context.ProductTypes
        .Include(productType => productType.Translations)
        .OrderBy(productType => productType.SortOrder)
        .ThenBy(productType => productType.Code)
        .ToListAsync();

    public Task<bool> CodeExistsAsync(string code, int? excludingId = null)
    {
        var normalizedCode = code.Trim().ToUpper();
        return _context.ProductTypes.AnyAsync(productType =>
            productType.Code == normalizedCode &&
            (!excludingId.HasValue || productType.Id != excludingId.Value));
    }

    public Task<bool> IsInUseAsync(int id) =>
        _context.Products.AnyAsync(product => product.ProductTypeId == id);

    public async Task AddAsync(ProductType productType)
    {
        await _context.ProductTypes.AddAsync(productType);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(ProductType productType)
    {
        _context.ProductTypes.Update(productType);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(ProductType productType)
    {
        _context.ProductTypeTranslations.RemoveRange(productType.Translations);
        _context.ProductTypes.Remove(productType);
        await _context.SaveChangesAsync();
    }
}
