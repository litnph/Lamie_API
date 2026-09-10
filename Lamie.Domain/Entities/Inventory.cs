using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum InventoryStockStatus
{
    Normal = 1,
    Low = 2,
    OutOfStock = 3
}

public enum StockTransactionType
{
    Receipt = 1,
    OrderUsage = 2,
    OrderReturn = 3,
    ManualIssue = 4,
    Adjustment = 5
}

public sealed class InventoryItem
{
    private InventoryItem()
    {
    }

    public InventoryItem(
        string code,
        string name,
        int measurementUnitId,
        string? note,
        bool isActive,
        DateTime nowUtc,
        int? legacyIngredientId = null)
    {
        EnsureUtc(nowUtc);
        Id = Guid.NewGuid();
        LegacyIngredientId = legacyIngredientId;
        CreatedAt = nowUtc;
        Update(code, name, measurementUnitId, note, isActive, nowUtc);
    }

    public Guid Id { get; private set; }
    public int? LegacyIngredientId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int MeasurementUnitId { get; private set; }
    public string? Note { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Update(
        string code,
        string name,
        int measurementUnitId,
        string? note,
        bool isActive,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode) || normalizedCode.Length > 80
            || !normalizedCode.All(character => char.IsLetterOrDigit(character) || character is '_' or '-'))
            throw new DomainException("Inventory item code must contain only letters, digits, underscores, or hyphens and be at most 80 characters.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new DomainException("Inventory item name is required and cannot exceed 200 characters.");
        if (measurementUnitId <= 0)
            throw new DomainException("Inventory item measurement unit is required.");
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote?.Length > 2000)
            throw new DomainException("Inventory item note cannot exceed 2000 characters.");

        Code = normalizedCode;
        Name = name.Trim();
        MeasurementUnitId = measurementUnitId;
        Note = normalizedNote;
        IsActive = isActive;
        UpdatedAt = nowUtc;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");
    }
}

public sealed class InventoryStockUnit
{
    private InventoryStockUnit()
    {
    }

    public InventoryStockUnit(
        Guid inventoryItemId,
        string? sizeName,
        decimal lowStockThreshold,
        bool isActive,
        DateTime nowUtc)
    {
        if (inventoryItemId == Guid.Empty)
            throw new DomainException("Inventory item is required.");
        EnsureUtc(nowUtc);

        Id = Guid.NewGuid();
        InventoryItemId = inventoryItemId;
        Quantity = 0;
        CreatedAt = nowUtc;
        UpdateSettings(sizeName, lowStockThreshold, isActive, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public int? LegacyIngredientId { get; private set; }
    public string? SizeName { get; private set; }
    public string SizeKey { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal LowStockThreshold { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public InventoryStockStatus Status => Quantity <= 0
        ? InventoryStockStatus.OutOfStock
        : Quantity <= LowStockThreshold
            ? InventoryStockStatus.Low
            : InventoryStockStatus.Normal;

    public void UpdateSettings(
        string? sizeName,
        decimal lowStockThreshold,
        bool isActive,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        var normalizedSize = string.IsNullOrWhiteSpace(sizeName) ? null : sizeName.Trim();
        if (normalizedSize?.Length > 100)
            throw new DomainException("Inventory size cannot exceed 100 characters.");
        var threshold = DecimalQuantity.Normalize(lowStockThreshold);
        if (threshold < 0)
            throw new DomainException("Low-stock threshold cannot be negative.");

        SizeName = normalizedSize;
        SizeKey = normalizedSize?.ToUpperInvariant() ?? string.Empty;
        LowStockThreshold = threshold;
        IsActive = isActive;
        UpdatedAt = nowUtc;
    }

    public decimal Add(decimal quantity, DateTime nowUtc)
    {
        EnsurePositive(quantity);
        EnsureUtc(nowUtc);
        Quantity = DecimalQuantity.Normalize(Quantity + quantity);
        UpdatedAt = nowUtc;
        return Quantity;
    }

    public decimal Remove(decimal quantity, DateTime nowUtc)
    {
        EnsurePositive(quantity);
        EnsureUtc(nowUtc);
        var normalized = DecimalQuantity.Normalize(quantity);
        if (normalized > Quantity)
            throw new DomainException("Insufficient inventory stock.");
        Quantity = DecimalQuantity.Normalize(Quantity - normalized);
        UpdatedAt = nowUtc;
        return Quantity;
    }

    public decimal AdjustTo(decimal countedQuantity, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        var normalized = DecimalQuantity.Normalize(countedQuantity);
        if (normalized < 0)
            throw new DomainException("Inventory quantity cannot be negative.");
        Quantity = normalized;
        UpdatedAt = nowUtc;
        return Quantity;
    }

    private static void EnsurePositive(decimal value)
    {
        if (DecimalQuantity.Normalize(value) <= 0)
            throw new DomainException("Inventory quantity must be greater than zero.");
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");
    }
}

public sealed class StockReceipt
{
    private StockReceipt()
    {
    }

    public StockReceipt(
        string receiptNumber,
        string clientRequestId,
        DateOnly receivedDate,
        string? note,
        string? invoiceUrl,
        string? invoiceFileName,
        string? invoiceContentType,
        Guid? createdById,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(receiptNumber) || receiptNumber.Trim().Length > 40)
            throw new DomainException("Receipt number is required and cannot exceed 40 characters.");
        if (string.IsNullOrWhiteSpace(clientRequestId) || clientRequestId.Trim().Length > 120)
            throw new DomainException("Receipt request id is required and cannot exceed 120 characters.");
        if (receivedDate == default)
            throw new DomainException("Receipt date is required.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");

        Id = Guid.NewGuid();
        ReceiptNumber = receiptNumber.Trim();
        ClientRequestId = clientRequestId.Trim();
        ReceivedDate = receivedDate;
        Note = Optional(note, 2000, "Receipt note");
        InvoiceUrl = Optional(invoiceUrl, 2048, "Invoice URL");
        InvoiceFileName = Optional(invoiceFileName, 260, "Invoice file name");
        InvoiceContentType = Optional(invoiceContentType, 100, "Invoice content type");
        CreatedById = createdById;
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string ReceiptNumber { get; private set; } = string.Empty;
    public string ClientRequestId { get; private set; } = string.Empty;
    public DateOnly ReceivedDate { get; private set; }
    public string? Note { get; private set; }
    public string? InvoiceUrl { get; private set; }
    public string? InvoiceFileName { get; private set; }
    public string? InvoiceContentType { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void AttachInvoice(string invoiceUrl, string invoiceFileName, string invoiceContentType)
    {
        InvoiceUrl = Optional(invoiceUrl, 2048, "Invoice URL");
        InvoiceFileName = Optional(invoiceFileName, 260, "Invoice file name");
        InvoiceContentType = Optional(invoiceContentType, 100, "Invoice content type");
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}

public sealed class StockReceiptItem
{
    private StockReceiptItem()
    {
    }

    public StockReceiptItem(Guid receiptId, Guid stockUnitId, decimal quantity)
    {
        if (receiptId == Guid.Empty || stockUnitId == Guid.Empty)
            throw new DomainException("Receipt and stock unit are required.");
        var normalized = DecimalQuantity.Normalize(quantity);
        if (normalized <= 0)
            throw new DomainException("Receipt quantity must be greater than zero.");

        Id = Guid.NewGuid();
        ReceiptId = receiptId;
        StockUnitId = stockUnitId;
        Quantity = normalized;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid StockUnitId { get; private set; }
    public decimal Quantity { get; private set; }
}

public sealed class StockTransaction
{
    private StockTransaction()
    {
    }

    public StockTransaction(
        Guid stockUnitId,
        StockTransactionType type,
        decimal quantityDelta,
        decimal balanceAfter,
        string operationKey,
        Guid? receiptId,
        Guid? orderId,
        Guid? orderMaterialId,
        string? note,
        Guid? createdById,
        DateTime nowUtc)
    {
        if (stockUnitId == Guid.Empty || !Enum.IsDefined(type))
            throw new DomainException("Stock transaction reference is invalid.");
        var delta = DecimalQuantity.Normalize(quantityDelta);
        if (delta == 0)
            throw new DomainException("Stock transaction quantity cannot be zero.");
        if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Trim().Length > 180)
            throw new DomainException("Stock transaction operation key is invalid.");
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");

        Id = Guid.NewGuid();
        StockUnitId = stockUnitId;
        Type = type;
        QuantityDelta = delta;
        BalanceAfter = DecimalQuantity.Normalize(balanceAfter);
        OperationKey = operationKey.Trim();
        ReceiptId = receiptId;
        OrderId = orderId;
        OrderMaterialId = orderMaterialId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (Note?.Length > 1000)
            throw new DomainException("Stock transaction note cannot exceed 1000 characters.");
        CreatedById = createdById;
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid StockUnitId { get; private set; }
    public StockTransactionType Type { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string OperationKey { get; private set; } = string.Empty;
    public Guid? ReceiptId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? OrderMaterialId { get; private set; }
    public string? Note { get; private set; }
    public Guid? CreatedById { get; private set; }
    public DateTime CreatedAt { get; private set; }
}

public sealed class OrderMaterial
{
    private OrderMaterial()
    {
    }

    public OrderMaterial(
        Guid orderId,
        InventoryStockUnit stockUnit,
        string inventoryItemCode,
        string inventoryItemName,
        string unitName,
        string? unitSymbol,
        decimal requiredQuantity,
        DateTime nowUtc)
    {
        if (orderId == Guid.Empty)
            throw new DomainException("Order is required.");
        ArgumentNullException.ThrowIfNull(stockUnit);

        Id = Guid.NewGuid();
        OrderId = orderId;
        StockUnitId = stockUnit.Id;
        InventoryItemId = stockUnit.InventoryItemId;
        InventoryItemCode = Required(inventoryItemCode, 80, "Inventory item code");
        InventoryItemName = Required(inventoryItemName, 200, "Inventory item name");
        SizeName = stockUnit.SizeName;
        UnitName = Required(unitName, 120, "Unit name");
        UnitSymbol = Optional(unitSymbol, 30);
        DeductedQuantity = 0;
        UpdateRequiredQuantity(requiredQuantity, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid StockUnitId { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public int? LegacyIngredientId { get; private set; }
    public string InventoryItemCode { get; private set; } = string.Empty;
    public string InventoryItemName { get; private set; } = string.Empty;
    public string? SizeName { get; private set; }
    public string UnitName { get; private set; } = string.Empty;
    public string? UnitSymbol { get; private set; }
    public decimal RequiredQuantity { get; private set; }
    public decimal DeductedQuantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdateRequiredQuantity(decimal quantity, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");
        var normalized = DecimalQuantity.Normalize(quantity);
        if (normalized <= 0)
            throw new DomainException("Order material quantity must be greater than zero.");
        RequiredQuantity = normalized;
        if (CreatedAt == default)
            CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void SetDeductedQuantity(decimal quantity, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");
        var normalized = DecimalQuantity.Normalize(quantity);
        if (normalized < 0)
            throw new DomainException("Deducted material quantity cannot be negative.");
        DeductedQuantity = normalized;
        UpdatedAt = nowUtc;
    }

    private static string Required(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new DomainException($"{field} is required and cannot exceed {maxLength} characters.");
        return value.Trim();
    }

    private static string? Optional(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"Value cannot exceed {maxLength} characters.");
        return normalized;
    }
}
