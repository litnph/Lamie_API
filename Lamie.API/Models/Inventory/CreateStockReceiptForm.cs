namespace Lamie.API.Models.Inventory;

public sealed class CreateStockReceiptForm
{
    public string ClientRequestId { get; init; } = string.Empty;
    public DateOnly ReceivedDate { get; init; }
    public string? Note { get; init; }
    public List<CreateStockReceiptItemForm> Items { get; init; } = [];
    public IFormFile? Invoice { get; init; }
    public bool CreateExpense { get; init; }
    public Guid? ExpenseCategoryId { get; init; }
    public decimal? ExpenseAmount { get; init; }
}

public sealed class CreateStockReceiptItemForm
{
    public Guid StockUnitId { get; init; }
    public decimal Quantity { get; init; }
}
