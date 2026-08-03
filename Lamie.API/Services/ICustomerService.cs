using Lamie.Application.Customers;

namespace Lamie.API.Services;

public interface ICustomerService
{
    Task<PagedCustomersDto> ListAsync(CustomerListQuery query, CancellationToken cancellationToken);
    Task<CustomerDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerOrderNotesResultDto> GetOrderNotesAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerDetailDto> UpdateNotesAsync(
        Guid id,
        UpdateCustomerNotesRequest request,
        CancellationToken cancellationToken);
}
