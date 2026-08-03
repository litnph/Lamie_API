using Lamie.Application.Orders;
using Lamie.Domain.Entities;

namespace Lamie.Application.Dashboard;

public sealed record DashboardPeriodRangeDto(
    string Key,
    string Label,
    int Days,
    DateTimeOffset CurrentStart,
    DateTimeOffset CurrentEnd,
    DateTimeOffset PreviousStart,
    DateTimeOffset PreviousEnd);

public sealed record RevenuePointDto(
    DateTimeOffset Key,
    string Label,
    string ShortLabel,
    decimal Revenue,
    int OrderCount);

public sealed record DashboardRevenueDto(
    decimal CurrentRevenue,
    decimal PreviousRevenue,
    int PaidOrderCount,
    IReadOnlyList<RevenuePointDto> Points);

public sealed record DashboardDeliveryRiskDto(
    Guid Id,
    string OrderCode,
    string OrdererName,
    string OrdererPhone,
    Guid ChannelId,
    string RecipientName,
    string RecipientPhone,
    bool PickupAtShop,
    string? DeliveryAddress,
    DateTimeOffset DeliveryAt,
    decimal DepositAmount,
    decimal ShippingFee,
    decimal? ShippingFeeActual,
    decimal SubTotal,
    decimal TotalAmount,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus,
    DateTimeOffset CreatedAt,
    string DeliveryState);

public sealed record DashboardActiveOrdersDto(
    int AwaitingConfirmationCount,
    int PreparingCount,
    int ShippingCount,
    int NeedActionCount,
    int LateDeliveryCount,
    int UpcomingDeliveryCount,
    IReadOnlyList<OrderListItemDto> AttentionOrders,
    IReadOnlyList<DashboardDeliveryRiskDto> DeliveryRisks);

public sealed record DashboardStockProductDto(
    int Id,
    string Sku,
    string Name,
    int Stock,
    string? ThumbnailUrl);

public sealed record DashboardInventoryDto(
    int LowStockCount,
    int OutOfStockCount,
    IReadOnlyList<DashboardStockProductDto> Products);

public sealed record DashboardSourceErrorDto(string Source, string Label, string Message);

public sealed record DashboardDto(
    DashboardPeriodRangeDto Period,
    DateTimeOffset GeneratedAt,
    int? NewOrdersCount,
    DashboardActiveOrdersDto? ActiveOrders,
    DashboardRevenueDto? Revenue,
    DashboardInventoryDto? Inventory,
    IReadOnlyList<DashboardSourceErrorDto> SourceErrors);
