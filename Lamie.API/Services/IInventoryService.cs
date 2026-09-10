using Lamie.API.Models.Inventory;
using Lamie.Application.Inventory;

namespace Lamie.API.Services;

public interface IInventoryService
{
    Task<PagedInventoryStockUnitsDto> ListStockUnitsAsync(InventoryListQuery query, CancellationToken cancellationToken);
    Task<PagedReplenishmentTasksDto> ListReplenishmentTasksAsync(InventoryListQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryItemDto>> ListItemsAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<InventoryItemDto> GetItemAsync(Guid id, CancellationToken cancellationToken);
    Task<InventoryItemDto> CreateItemAsync(SaveInventoryItemRequest request, CancellationToken cancellationToken);
    Task<InventoryItemDto> UpdateItemAsync(Guid id, SaveInventoryItemRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryStockUnitDto>> GetItemStockUnitsAsync(Guid inventoryItemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryStockUnitDto>> SaveItemStockUnitsAsync(Guid inventoryItemId, SaveIngredientStockUnitsRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryStockUnitDto>> GetIngredientStockUnitsAsync(int ingredientId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InventoryStockUnitDto>> SaveIngredientStockUnitsAsync(int ingredientId, SaveIngredientStockUnitsRequest request, CancellationToken cancellationToken);
    Task<PagedStockTransactionsDto> ListTransactionsAsync(Guid stockUnitId, int page, int pageSize, CancellationToken cancellationToken);
    Task<StockTransactionDto> CreateIssueAsync(Guid stockUnitId, CreateStockIssueRequest request, CancellationToken cancellationToken);
    Task<StockTransactionDto> CreateAdjustmentAsync(Guid stockUnitId, CreateStockAdjustmentRequest request, CancellationToken cancellationToken);
    Task<StockReceiptDto> CreateReceiptAsync(CreateStockReceiptForm form, CancellationToken cancellationToken);
    Task<StockReceiptDto> GetReceiptAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderMaterialDto>> GetOrderMaterialsAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderMaterialDto>> SaveOrderMaterialsAsync(Guid orderId, SaveOrderMaterialsRequest request, CancellationToken cancellationToken);
}
