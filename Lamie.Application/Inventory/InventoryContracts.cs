using Lamie.Domain.Entities;

namespace Lamie.Application.Inventory;

public sealed class InventoryListQuery
{
    public string? Search { get; init; }
    public InventoryStockStatus? Status { get; init; }
    public bool IncludeInactive { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record InventoryStockUnitDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemCode,
    string InventoryItemName,
    string? SizeName,
    string UnitName,
    string? UnitSymbol,
    decimal Quantity,
    decimal LowStockThreshold,
    InventoryStockStatus Status,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    string? RowVersion);

public sealed record PagedInventoryStockUnitsDto(
    IReadOnlyList<InventoryStockUnitDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record ReplenishmentTaskDto(
    Guid Id,
    Guid StockUnitId,
    Guid InventoryItemId,
    string InventoryItemCode,
    string InventoryItemName,
    string? SizeName,
    string UnitName,
    string? UnitSymbol,
    decimal CurrentQuantity,
    decimal LowStockThreshold,
    InventoryStockStatus StockStatus,
    DateTimeOffset UpdatedAt);

public sealed record PagedReplenishmentTasksDto(
    IReadOnlyList<ReplenishmentTaskDto> Items,
    int TotalCount,
    int PendingCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record SaveInventoryStockUnitItem(
    Guid? Id,
    string? SizeName,
    decimal LowStockThreshold,
    bool IsActive);

public sealed record SaveIngredientStockUnitsRequest(
    IReadOnlyList<SaveInventoryStockUnitItem> Items);

public sealed record InventoryItemDto(
    Guid Id,
    string Code,
    string Name,
    int MeasurementUnitId,
    string UnitName,
    string? UnitSymbol,
    string? Note,
    bool IsActive,
    bool UsesSizes,
    int? LegacyIngredientId,
    IReadOnlyList<InventoryStockUnitDto> StockUnits,
    DateTimeOffset UpdatedAt,
    string? RowVersion);

public sealed record SaveInventoryItemRequest(
    string Code,
    string Name,
    int MeasurementUnitId,
    string? Note,
    bool IsActive,
    IReadOnlyList<SaveInventoryStockUnitItem> StockUnits);

public sealed record StockTransactionDto(
    Guid Id,
    Guid StockUnitId,
    StockTransactionType Type,
    decimal QuantityDelta,
    decimal BalanceAfter,
    Guid? ReceiptId,
    string? ReceiptNumber,
    Guid? OrderId,
    string? OrderCode,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record PagedStockTransactionsDto(
    IReadOnlyList<StockTransactionDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record CreateStockIssueRequest(
    string ClientRequestId,
    decimal Quantity,
    string? Note,
    string? RowVersion);

public sealed record CreateStockAdjustmentRequest(
    string ClientRequestId,
    decimal CountedQuantity,
    string? Note,
    string? RowVersion);

public sealed record StockReceiptItemDto(
    Guid Id,
    Guid StockUnitId,
    Guid InventoryItemId,
    string InventoryItemCode,
    string InventoryItemName,
    string? SizeName,
    string UnitName,
    string? UnitSymbol,
    decimal Quantity);

public sealed record StockReceiptDto(
    Guid Id,
    string ReceiptNumber,
    string ClientRequestId,
    DateOnly ReceivedDate,
    string? Note,
    string? InvoiceUrl,
    string? InvoiceFileName,
    Guid? ExpenseId,
    IReadOnlyList<StockReceiptItemDto> Items,
    DateTimeOffset CreatedAt);

public sealed record OrderMaterialDto(
    Guid Id,
    Guid StockUnitId,
    Guid InventoryItemId,
    string InventoryItemCode,
    string InventoryItemName,
    string? SizeName,
    string UnitName,
    string? UnitSymbol,
    decimal RequiredQuantity,
    decimal DeductedQuantity,
    decimal CurrentStock,
    bool StockUnitIsActive);

public sealed record SaveOrderMaterialItem(Guid? Id, Guid StockUnitId, decimal Quantity);

public sealed record SaveOrderMaterialsRequest(
    IReadOnlyList<SaveOrderMaterialItem> Items,
    string? OrderRowVersion);
