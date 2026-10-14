using Lamie.API.Models.Orders;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;

namespace Lamie.API.Services;

public interface IOrderService
{
    Task<PagedOrdersDto> ListAsync(OrderListQuery query, CancellationToken cancellationToken);
    Task<OrderDeliveryBoardDto> DeliveryBoardAsync(OrderListQuery query, CancellationToken cancellationToken);
    Task<OrderDetailDto> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<OrderDetailDto> CreateAsync(CreateOrderForm form, CancellationToken cancellationToken);
    Task<BatchCreateOrdersDto> CreateBatchAsync(BatchCreateOrdersForm form, CancellationToken cancellationToken);
    Task<OrderDetailDto> UpdateAsync(Guid id, UpdateOrderForm form, CancellationToken cancellationToken);
    Task ChangeStatusAsync(Guid id, OrderStatus status, CancellationToken cancellationToken);
    Task<OrderDetailDto> OverrideStatusAsync(Guid id, OverrideOrderStatusRequest request, CancellationToken cancellationToken);
    Task<OrderDetailDto> RunWorkflowActionAsync(Guid id, string action, OrderWorkflowCommand command, CancellationToken cancellationToken);
    Task<OrderDetailDto> UpdateRecipientAsync(Guid id, UpdateOrderRecipientRequest request, CancellationToken cancellationToken);
    Task<OrderDetailDto> UpdateAssignmentAsync(Guid id, UpdateOrderAssignmentRequest request, CancellationToken cancellationToken);
    Task<OrderDetailDto> UpdateChecklistAsync(Guid id, UpdateOrderChecklistRequest request, CancellationToken cancellationToken);
    Task<OrderDetailDto> HoldAsync(Guid id, HoldOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDetailDto> ResumeAsync(Guid id, ResumeOrderRequest request, CancellationToken cancellationToken);
    Task ReturnToPreviousStatusAsync(Guid id, ReturnOrderToPreviousStatusRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderPaymentDto>> ListPaymentsAsync(Guid id, CancellationToken cancellationToken);
    Task<OrderPaymentDto> AddPaymentAsync(Guid id, CreateOrderPaymentRequest request, CancellationToken cancellationToken);
    Task ChangePaymentStatusAsync(Guid id, ChangePaymentStatusRequest request, CancellationToken cancellationToken);
    Task UpdateShippingFeeActualAsync(Guid id, UpdateShippingFeeActualRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderCalendarItemDto>> CalendarAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDeliveryLocationDto>> CalendarLocationsAsync(DateOnly date, CancellationToken cancellationToken);
}
