using Lamie.API.Models.Inventory;
using Lamie.API.Services;
using Lamie.Application.Identity;
using Lamie.Application.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.InventoryView)]
[Route("api/inventory")]
public sealed class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IAuthorizationService _authorizationService;

    public InventoryController(IInventoryService inventoryService, IAuthorizationService authorizationService)
    {
        _inventoryService = inventoryService;
        _authorizationService = authorizationService;
    }

    [HttpGet("stock-units")]
    public Task<PagedInventoryStockUnitsDto> ListStockUnits(
        [FromQuery] InventoryListQuery query,
        CancellationToken cancellationToken) =>
        _inventoryService.ListStockUnitsAsync(query, cancellationToken);

    [HttpGet("replenishment-tasks")]
    public Task<PagedReplenishmentTasksDto> ListReplenishmentTasks(
        [FromQuery] InventoryListQuery query,
        CancellationToken cancellationToken) =>
        _inventoryService.ListReplenishmentTasksAsync(query, cancellationToken);

    [HttpGet("items")]
    public Task<IReadOnlyList<InventoryItemDto>> ListItems(
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        _inventoryService.ListItemsAsync(includeInactive, cancellationToken);

    [HttpGet("items/{id:guid}")]
    public Task<InventoryItemDto> GetItem(Guid id, CancellationToken cancellationToken) =>
        _inventoryService.GetItemAsync(id, cancellationToken);

    [HttpPost("items")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public async Task<ActionResult<InventoryItemDto>> CreateItem(
        SaveInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryService.CreateItemAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetItem), new { id = item.Id }, item);
    }

    [HttpPut("items/{id:guid}")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public Task<InventoryItemDto> UpdateItem(
        Guid id,
        SaveInventoryItemRequest request,
        CancellationToken cancellationToken) =>
        _inventoryService.UpdateItemAsync(id, request, cancellationToken);

    [HttpGet("items/{id:guid}/stock-units")]
    public Task<IReadOnlyList<InventoryStockUnitDto>> GetItemStockUnits(
        Guid id,
        CancellationToken cancellationToken) =>
        _inventoryService.GetItemStockUnitsAsync(id, cancellationToken);

    [HttpPut("items/{id:guid}/stock-units")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public Task<IReadOnlyList<InventoryStockUnitDto>> SaveItemStockUnits(
        Guid id,
        SaveIngredientStockUnitsRequest request,
        CancellationToken cancellationToken) =>
        _inventoryService.SaveItemStockUnitsAsync(id, request, cancellationToken);

    [HttpGet("ingredients/{ingredientId:int}/stock-units")]
    public Task<IReadOnlyList<InventoryStockUnitDto>> GetIngredientStockUnits(
        int ingredientId,
        CancellationToken cancellationToken) =>
        _inventoryService.GetIngredientStockUnitsAsync(ingredientId, cancellationToken);

    [HttpPut("ingredients/{ingredientId:int}/stock-units")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public Task<IReadOnlyList<InventoryStockUnitDto>> SaveIngredientStockUnits(
        int ingredientId,
        SaveIngredientStockUnitsRequest request,
        CancellationToken cancellationToken) =>
        _inventoryService.SaveIngredientStockUnitsAsync(ingredientId, request, cancellationToken);

    [HttpGet("stock-units/{stockUnitId:guid}/transactions")]
    public Task<PagedStockTransactionsDto> ListTransactions(
        Guid stockUnitId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        _inventoryService.ListTransactionsAsync(stockUnitId, page, pageSize, cancellationToken);

    [HttpPost("stock-units/{stockUnitId:guid}/issues")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public Task<StockTransactionDto> CreateIssue(
        Guid stockUnitId,
        CreateStockIssueRequest request,
        CancellationToken cancellationToken) =>
        _inventoryService.CreateIssueAsync(stockUnitId, request, cancellationToken);

    [HttpPost("stock-units/{stockUnitId:guid}/adjustments")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public Task<StockTransactionDto> CreateAdjustment(
        Guid stockUnitId,
        CreateStockAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        _inventoryService.CreateAdjustmentAsync(stockUnitId, request, cancellationToken);

    [HttpPost("receipts")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = PermissionNames.InventoryManage)]
    public async Task<ActionResult<StockReceiptDto>> CreateReceipt(
        [FromForm] CreateStockReceiptForm form,
        CancellationToken cancellationToken)
    {
        if (form.CreateExpense)
        {
            var expensePermission = await _authorizationService.AuthorizeAsync(
                User,
                null,
                PermissionNames.ExpensesManage);
            if (!expensePermission.Succeeded)
                return Forbid();
        }
        var receipt = await _inventoryService.CreateReceiptAsync(form, cancellationToken);
        return CreatedAtAction(nameof(GetReceipt), new { id = receipt.Id }, receipt);
    }

    [HttpGet("receipts/{id:guid}")]
    public Task<StockReceiptDto> GetReceipt(Guid id, CancellationToken cancellationToken) =>
        _inventoryService.GetReceiptAsync(id, cancellationToken);

    [HttpPut("orders/{orderId:guid}/materials")]
    [HttpPut("orders/{orderId:guid}/inventory-usages")]
    [Authorize(Policy = PermissionNames.OrdersManage)]
    public Task<IReadOnlyList<OrderMaterialDto>> SaveOrderMaterials(
        Guid orderId,
        SaveOrderMaterialsRequest request,
        CancellationToken cancellationToken) =>
        _inventoryService.SaveOrderMaterialsAsync(orderId, request, cancellationToken);
}
