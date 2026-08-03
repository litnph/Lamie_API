using Lamie.Application.Orders;
using Lamie.Domain.Entities;

namespace Lamie.Application.Customers;

public sealed record CustomerSummaryDto(
    Guid Id,
    string Name,
    string Phone,
    int OrderCount,
    int PaidOrderCount,
    decimal TotalSpent,
    OrderListItemDto LatestOrder,
    DateTimeOffset? LastPurchaseAt);

public sealed record CustomerAddressDto(
    string Address,
    Guid OrderId,
    string OrderCode,
    DateTimeOffset DeliveryAt);

public sealed record CustomerDetailDto(
    Guid Id,
    string Name,
    string Phone,
    string? Email,
    string? Notes,
    int OrderCount,
    int PaidOrderCount,
    decimal TotalSpent,
    OrderListItemDto LatestOrder,
    DateTimeOffset? LastPurchaseAt,
    IReadOnlyList<OrderListItemDto> Orders,
    IReadOnlyList<CustomerAddressDto> Addresses);

public sealed record CustomerOrderNoteDto(
    string Id,
    Guid OrderId,
    string OrderCode,
    DateTimeOffset CreatedAt,
    string Label,
    string Content);

public sealed record CustomerOrderNotesResultDto(
    IReadOnlyList<CustomerOrderNoteDto> Items,
    int FailedCount);

public sealed record PagedCustomersDto(
    IReadOnlyList<CustomerSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed class CustomerListQuery
{
    public string? Search { get; init; }
    public OrderStatus? OrderStatus { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record UpdateCustomerNotesRequest(string? Notes);
