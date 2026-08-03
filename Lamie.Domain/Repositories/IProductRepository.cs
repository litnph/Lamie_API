using Lamie.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamie.Domain.Repositories
{
    public interface IProductRepository
    {
        Task AddAsync(Product product);
        Task<Product?> GetByIdAsync(int id);
        Task<List<Product>?> GetAllAsync();
        Task UpdateAsync(Product product);
        Task DeleteAsync(Product product);
        Task<bool> HasOrderReferencesAsync(int productId, CancellationToken cancellationToken = default);
    }
}
