using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamie.Infrastructure.Persistence.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly AppDbContext _context;

        public ProductRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Product product)
        {
            await _context.Products.AddAsync(product);
            await _context.SaveChangesAsync();
        }

        public async Task<Product?> GetByIdAsync(int id)
        {
            return await _context.Products
                .Include(p => p.Translations)
                .Include(p => p.Images)
                .Include(p => p.Tags)
                .Include(p => p.Colors)
                .Include(p => p.Collections)
                .Include(p => p.Styles)
                .Include(p => p.Occasions)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<List<Product>?> GetAllAsync()
        {
            return await _context.Products
                .Include(p => p.Translations)
                .Include(p => p.Images)
                .Include(p => p.Tags)
                .Include(p => p.Colors)
                .Include(p => p.Collections)
                .Include(p => p.Styles)
                .Include(p => p.Occasions)
                .AsSplitQuery()
                .ToListAsync();
        }

        public async Task UpdateAsync(Product product)
        {
            _context.Products.Update(product);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Product product)
        {
            _context.ProductTranslations.RemoveRange(product.Translations);
            _context.ProductImages.RemoveRange(product.Images);
            _context.ProductCollections.RemoveRange(product.Collections);
            _context.ProductColors.RemoveRange(product.Colors);
            _context.ProductTags.RemoveRange(product.Tags);
            _context.ProductStyles.RemoveRange(product.Styles);
            _context.ProductOccasions.RemoveRange(product.Occasions);
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
        }

        public Task<bool> HasOrderReferencesAsync(
            int productId,
            CancellationToken cancellationToken = default) =>
            _context.OrderItems
                .AsNoTracking()
                .AnyAsync(item => item.ProductId == productId, cancellationToken);

        public Task<bool> SkuExistsAsync(string sku, int? excludingProductId = null, CancellationToken cancellationToken = default) =>
            _context.Products.AsNoTracking().AnyAsync(
                product => product.Sku == sku && (!excludingProductId.HasValue || product.Id != excludingProductId.Value),
                cancellationToken);
    }
}
