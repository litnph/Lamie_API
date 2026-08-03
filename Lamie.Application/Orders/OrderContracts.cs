using Lamie.Domain.Entities;

namespace Lamie.Application.Orders;

public sealed record OrderItemDto(
    Guid Id,
    string? ProductId,
    string? ProductSku,
    string ProductName,
    string? ThumbnailUrl,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string? Note);

public sealed record OrderImageDto(Guid Id, Guid? OrderItemId, string ImageUrl, int SortOrder, string? Description);

public sealed record OrderChangeLogDto(
    Guid Id,
    string EntityName,
    string FieldName,
    string? OldValue,
    string? NewValue,
    string ChangeType,
    Guid? ChangedById,
    string? ChangedByName,
    DateTimeOffset ChangedAt,
    string? Note);

public sealed record OrderListItemDto(
    Guid Id,
    string OrderCode,
    string OrdererName,
    string OrdererPhone,
    Guid ChannelId,
    string RecipientName,
    string RecipientPhone,
    bool PickupAtShop,
    bool ProvinceShipping,
    string? DeliveryAddress,
    DateTimeOffset DeliveryAt,
    DateTimeOffset? DeliveryTo,
    decimal DepositAmount,
    decimal ShippingFee,
    decimal? ShippingFeeActual,
    decimal SubTotal,
    decimal TotalAmount,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus,
    DateTimeOffset CreatedAt,
    string? ContentNote,
    string? ImageUrl);

public sealed record OrderDetailDto(
    Guid Id,
    string OrderCode,
    string OrdererName,
    string OrdererPhone,
    Guid ChannelId,
    string RecipientName,
    string RecipientPhone,
    bool PickupAtShop,
    bool ProvinceShipping,
    string? DeliveryAddress,
    string? DeliveryAddressDescription,
    DateTimeOffset DeliveryAt,
    DateTimeOffset? DeliveryTo,
    decimal DepositAmount,
    decimal ShippingFee,
    decimal? ShippingFeeActual,
    decimal SubTotal,
    decimal TotalAmount,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus,
    DateTimeOffset CreatedAt,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    string? Description,
    string? ContentNote,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderImageDto> Images,
    IReadOnlyList<OrderChangeLogDto> ChangeLogs);

public sealed record OrderCalendarItemDto(
    Guid Id,
    string OrderCode,
    string RecipientName,
    string RecipientPhone,
    DateTimeOffset DeliveryAt,
    DateTimeOffset? DeliveryTo,
    bool PickupAtShop,
    bool ProvinceShipping,
    string? DeliveryAddress,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    decimal TotalAmount);

public sealed record OrderDeliveryLocationDto(
    Guid Id,
    string OrderCode,
    string RecipientName,
    string? DeliveryAddress,
    decimal Latitude,
    decimal Longitude,
    DateTimeOffset DeliveryAt,
    OrderStatus OrderStatus);

public sealed record PagedOrdersDto(
    IReadOnlyList<OrderListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public enum OrderSortBy
{
    DeliveryAt = 1,
    CreatedAt = 2,
    TotalAmount = 3
}

public enum SortDirection
{
    Ascending = 1,
    Descending = 2
}

public sealed class OrderListQuery
{
    public OrderStatus? OrderStatus { get; init; }
    public PaymentStatus? PaymentStatus { get; init; }
    public Guid? ChannelId { get; init; }
    public DateTimeOffset? DeliveryFrom { get; init; }
    public DateTimeOffset? DeliveryTo { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public string? Phone { get; init; }
    public string? Search { get; init; }
    public OrderSortBy SortBy { get; init; } = OrderSortBy.DeliveryAt;
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class OrderLineRequest
{
    public string? Id { get; init; }
    public string? ProductId { get; init; }
    public string? ProductSku { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public string? Note { get; init; }
}

public sealed record ChangeOrderStatusRequest(OrderStatus Status);
public sealed record ChangePaymentStatusRequest(PaymentStatus Status);
