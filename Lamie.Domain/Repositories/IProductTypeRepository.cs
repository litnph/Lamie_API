using Lamie.Domain.Entities;

namespace Lamie.Domain.Repositories;

public interface IProductTypeRepository
{
    Task<ProductType?> GetByIdAsync(int id);
    Task<List<ProductType>> GetAllAsync();
    Task<bool> CodeExistsAsync(string code, int? excludingId = null);
    Task<bool> IsInUseAsync(int id);
    Task AddAsync(ProductType productType);
    Task UpdateAsync(ProductType productType);
    Task DeleteAsync(ProductType productType);
}
