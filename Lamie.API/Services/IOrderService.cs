using Lamie.API.Models.Orders;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;

namespace Lamie.API.Services;

public interface IOrderService
{
    Task<PagedOrdersDto> ListAsync(OrderListQuery query, CancellationToken cancellationToken);
    Task<OrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<OrderDetailDto> CreateAsync(CreateOrderForm form, CancellationToken cancellationToken);
    Task<OrderDetailDto> UpdateAsync(Guid id, UpdateOrderForm form, CancellationToken cancellationToken);
    Task ChangeStatusAsync(Guid id, OrderStatus status, CancellationToken cancellationToken);
    Task ChangePaymentStatusAsync(Guid id, PaymentStatus status, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderCalendarItemDto>> CalendarAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDeliveryLocationDto>> CalendarLocationsAsync(DateOnly date, CancellationToken cancellationToken);
}
