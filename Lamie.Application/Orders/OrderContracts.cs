using Lamie.Application.Inventory;
using Lamie.Domain.Entities;

namespace Lamie.Application.Orders;

public sealed record OrderItemIngredientSnapshotDto(
    Guid Id,
    int? IngredientId,
    string IngredientCode,
    string IngredientName,
    string BaseUnitCode,
    string BaseUnitName,
    string? BaseUnitSymbol,
    decimal PerProductBaseQuantity,
    int ProductQuantity,
    decimal TotalBaseQuantity,
    string? Note,
    int SortOrder,
    DateTimeOffset CapturedAt);

public sealed record OrderItemDto(
    Guid Id,
    string? ProductId,
    string? ProductSku,
    string ProductName,
    string? ThumbnailUrl,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string? Note,
    bool HasCard,
    string? CardMessage,
    bool HasBanner,
    string? BannerMessage,
    DateTimeOffset? IngredientSnapshotCapturedAt,
    IReadOnlyList<OrderItemIngredientSnapshotDto> IngredientSnapshots,
    IReadOnlyList<OrderImageDto> Images,
    int? ProductTypeId = null,
    string? ProductTypeName = null,
    int? CategoryId = null,
    string? CategoryName = null);

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
    string? RecipientName,
    string? RecipientPhone,
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
    string? ImageUrl)
{
    public string ProductSummary { get; init; } = string.Empty;
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public bool PaymentHistoryIncomplete { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ChannelName { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset? PaidInFullAt { get; init; }
    public DateOnly? PreparationDate { get; init; }
    public Guid? AssigneeId { get; init; }
    public string? AssigneeName { get; init; }
    public string? HoldReason { get; init; }
    public DateTimeOffset? HoldAt { get; init; }
    public string FulfillmentType { get; init; } = "local-delivery";
    public string TrackingGroup { get; init; } = "upcoming";
    public int QuantityTotal { get; init; }
    public bool HasCard { get; init; }
    public string? CardMessage { get; init; }
    public bool HasBanner { get; init; }
    public string? BannerMessage { get; init; }
    public string? RowVersion { get; init; }
}

public sealed record OrderDetailDto(
    Guid Id,
    string OrderCode,
    string OrdererName,
    string OrdererPhone,
    Guid ChannelId,
    string? RecipientName,
    string? RecipientPhone,
    bool PickupAtShop,
    bool ProvinceShipping,
    string? DeliveryAddress,
    string? DeliveryAddressDescription,
    AdministrativeScheme? AddressScheme,
    string? ProvinceCode,
    string? ProvinceName,
    string? DistrictCode,
    string? DistrictName,
    string? CommuneCode,
    string? CommuneName,
    string? AddressDetail,
    string? FullAddressSnapshot,
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
    string? RowVersion,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderImageDto> Images,
    IReadOnlyList<OrderChangeLogDto> ChangeLogs,
    IReadOnlyList<OrderMaterialDto>? Materials = null)
{
    public Guid? CustomerId { get; init; }
    public string SenderName { get; init; } = string.Empty;
    public string SenderPhone { get; init; } = string.Empty;
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public bool PaymentHistoryIncomplete { get; init; }
    public IReadOnlyList<OrderPaymentDto> Payments { get; init; } = [];
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ShippingFeeIncurredAt { get; init; }
    public DateTimeOffset? ShippingFeePaidAt { get; init; }
    public PaymentMethod? ShippingFeePaymentMethod { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
    public DateTimeOffset? PaidInFullAt { get; init; }
    public DateOnly? PreparationDate { get; init; }
    public Guid? AssigneeId { get; init; }
    public string? AssigneeName { get; init; }
    public string? HoldReason { get; init; }
    public DateTimeOffset? HoldAt { get; init; }
    public OrderChecklistDto Checklist { get; init; } = new(false, false, false, false);
    public string? CancelledReason { get; init; }
}

public sealed record OrderChecklistDto(bool DesignConfirmed, bool MaterialsPrepared, bool CardPrepared, bool QualityChecked);

public sealed record OrderCalendarItemDto(
    Guid Id,
    string OrderCode,
    string? OrdererName,
    string? OrdererPhone,
    string? RecipientName,
    string? RecipientPhone,
    DateTimeOffset DeliveryAt,
    DateTimeOffset? DeliveryTo,
    DateOnly? PreparationDate,
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

public sealed record BatchCreatedOrderDto(
    string ClientDraftId,
    Guid OrderId,
    string OrderNumber);

public sealed record BatchCreateOrdersDto(
    int CreatedCount,
    IReadOnlyList<BatchCreatedOrderDto> Orders);

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
    public DateTimeOffset? CompletedFrom { get; init; }
    public DateTimeOffset? CompletedTo { get; init; }
    public string? Phone { get; init; }
    public string? Search { get; init; }
    public string? StatusGroup { get; init; }
    public string? FulfillmentType { get; init; }
    public bool IncludeBacklog { get; init; }
    public OrderSortBy SortBy { get; init; } = OrderSortBy.DeliveryAt;
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class OrderLineRequest
{
    public string? Id { get; init; }
    public string? ProductId { get; init; }
    public int? CategoryId { get; init; }
    public int? ProductTypeId { get; init; }
    public string? ProductSku { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public string? Note { get; init; }
    public bool HasCard { get; init; }
    public string? CardMessage { get; init; }
    public bool HasBanner { get; init; }
    public string? BannerMessage { get; init; }
    public bool IngredientsSpecified { get; init; }
    public List<OrderLineIngredientRequest>? Ingredients { get; init; }
}

public sealed class OrderLineIngredientRequest
{
    public int IngredientId { get; init; }
    public decimal BaseQuantity { get; init; }
    public string? Note { get; init; }
    public int SortOrder { get; init; }
}

public sealed record ChangeOrderStatusRequest(OrderStatus Status);
public sealed record OverrideOrderStatusRequest(OrderStatus Status, string? RowVersion = null, string? Reason = null);
public sealed record ChangePaymentStatusRequest(
    PaymentStatus Status,
    decimal? Amount = null,
    DateTimeOffset? PaidAt = null,
    PaymentMethod? Method = null,
    string? IdempotencyKey = null,
    string? Note = null);
public sealed record UpdateShippingFeeActualRequest(
    decimal? ShippingFeeActual,
    DateTimeOffset? IncurredAt = null,
    DateTimeOffset? PaidAt = null,
    PaymentMethod? Method = null,
    string? IdempotencyKey = null,
    string? RowVersion = null);

public sealed record OrderWorkflowCommand(
    string? RowVersion = null,
    string? Reason = null,
    Guid? AssigneeId = null,
    string? AssigneeName = null,
    DateOnly? PreparationDate = null,
    string? IdempotencyKey = null);

public sealed record UpdateOrderRecipientRequest(string? RecipientName, string? RecipientPhone, string? DeliveryAddress, string? RowVersion = null);
public sealed record UpdateOrderAssignmentRequest(Guid? AssigneeId, string? AssigneeName, DateOnly? PreparationDate, string? RowVersion = null);
public sealed record UpdateOrderChecklistRequest(bool? DesignConfirmed, bool? MaterialsPrepared, bool? CardPrepared, bool? QualityChecked, string? RowVersion = null);
public sealed record HoldOrderRequest(string Reason, string? RowVersion = null);
public sealed record ResumeOrderRequest(string? RowVersion = null);
public sealed record ReturnOrderToPreviousStatusRequest(string? RowVersion = null);

public sealed record OrderPaymentDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    OrderPaymentKind Kind,
    PaymentMethod Method,
    DateTimeOffset PaidAt,
    PaymentTransactionStatus Status,
    string? Note,
    bool AffectsRecognizedRevenue,
    decimal ProductRevenueAdjustment,
    decimal ShippingRevenueAdjustment);

public sealed record CreateOrderPaymentRequest(
    decimal Amount,
    OrderPaymentKind Kind,
    PaymentMethod Method,
    DateTimeOffset PaidAt,
    string IdempotencyKey,
    string? Note = null,
    bool AffectsRecognizedRevenue = false,
    decimal ProductRevenueAdjustment = 0,
    decimal ShippingRevenueAdjustment = 0);

public sealed record OrderDeliveryBoardSummaryDto(
    int OrderCount,
    int TodayCount,
    int LateTodayCount,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RemainingAmount,
    int OverdueBeforeTodayCount)
{
    public int LateCount { get; init; }
    public int UndeliveredTodayCount { get; init; }
    public int UpcomingCount { get; init; }
    public int DeliveredUnpaidCount { get; init; }
    public int CompletedCount { get; init; }
    public int CancelledCount { get; init; }
}

public sealed record OrderDeliveryBoardDto(
    IReadOnlyList<OrderListItemDto> Items,
    OrderDeliveryBoardSummaryDto Summary,
    DateTimeOffset GeneratedAt)
{
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}
