using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum MaterialGroup
{
    FreshFlowers = 1,
    Supplies = 2,
    ContainersAndAccessories = 3,
    Other = 99
}

public enum DocumentReviewStatus
{
    Missing = 1,
    NeedsReview = 2,
    Available = 3,
    Reconciled = 4
}

public enum TaxEligibilityStatus
{
    NotAssessed = 1,
    NeedsReview = 2,
    Eligible = 3,
    Ineligible = 4
}

public enum FinancePeriodStatus
{
    Draft = 1,
    Closed = 2
}

public enum StocktakeStatus
{
    Estimated = 1,
    Confirmed = 2
}

public enum FinancePeriodEventKind
{
    Created = 1,
    DataChanged = 2,
    StocktakeConfirmed = 3,
    ShippingConfirmed = 4,
    ExpensesConfirmed = 5,
    Closed = 6,
    Reopened = 7
}

public sealed class MaterialPurchase
{
    private MaterialPurchase() { }

    public MaterialPurchase(DateOnly purchaseDate, DateOnly receivedDate, string vendorName,
        string? vendorAddress, string? vendorTaxId, DocumentReviewStatus documentStatus,
        TaxEligibilityStatus taxEligibilityStatus, string? documentReference, string? attachmentUrl,
        string? notes, DateTime nowUtc, Guid? createdById, DateOnly? invoiceDate = null)
    {
        Id = Guid.NewGuid();
        Update(purchaseDate, receivedDate, vendorName, vendorAddress, vendorTaxId, documentStatus,
            taxEligibilityStatus, documentReference, attachmentUrl, notes, nowUtc, invoiceDate);
        CreatedAt = nowUtc;
        CreatedById = createdById;
    }

    public Guid Id { get; private set; }
    public DateOnly PurchaseDate { get; private set; }
    public DateOnly ReceivedDate { get; private set; }
    public DateOnly? InvoiceDate { get; private set; }
    public string VendorName { get; private set; } = string.Empty;
    public string? VendorAddress { get; private set; }
    public string? VendorTaxId { get; private set; }
    public DocumentReviewStatus DocumentStatus { get; private set; }
    public TaxEligibilityStatus TaxEligibilityStatus { get; private set; }
    public string? DocumentReference { get; private set; }
    public string? AttachmentUrl { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }

    public void Update(DateOnly purchaseDate, DateOnly receivedDate, string vendorName,
        string? vendorAddress, string? vendorTaxId, DocumentReviewStatus documentStatus,
        TaxEligibilityStatus taxEligibilityStatus, string? documentReference, string? attachmentUrl,
        string? notes, DateTime nowUtc, DateOnly? invoiceDate = null)
    {
        if (purchaseDate == default || receivedDate == default)
            throw new DomainException("Purchase and received dates are required.");
        if (receivedDate < purchaseDate)
            throw new DomainException("Received date cannot be before purchase date.");
        if (string.IsNullOrWhiteSpace(vendorName))
            throw new DomainException("Vendor name is required.");
        if (!Enum.IsDefined(documentStatus) || !Enum.IsDefined(taxEligibilityStatus))
            throw new DomainException("Document review status is invalid.");

        PurchaseDate = purchaseDate;
        ReceivedDate = receivedDate;
        InvoiceDate = invoiceDate;
        VendorName = Required(vendorName, 200, "Vendor name");
        VendorAddress = Optional(vendorAddress, 500, "Vendor address");
        VendorTaxId = Optional(vendorTaxId, 50, "Vendor tax id");
        DocumentStatus = documentStatus;
        TaxEligibilityStatus = taxEligibilityStatus;
        DocumentReference = Optional(documentReference, 200, "Document reference");
        AttachmentUrl = Optional(attachmentUrl, 2048, "Attachment url");
        Notes = Optional(notes, 2000, "Purchase notes");
        UpdatedAt = nowUtc;
    }

    private static string Required(string value, int max, string field) =>
        Optional(value, max, field) ?? throw new DomainException($"{field} is required.");

    internal static string? Optional(string? value, int max, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > max) throw new DomainException($"{field} cannot exceed {max} characters.");
        return normalized;
    }
}

public sealed class MaterialPurchaseLine
{
    private MaterialPurchaseLine() { }
    public MaterialPurchaseLine(Guid purchaseId, MaterialGroup group, string? description, decimal amount)
    {
        if (purchaseId == Guid.Empty) throw new DomainException("Purchase is required.");
        FinanceValue.ValidatePositive(amount, "Purchase line amount");
        if (!Enum.IsDefined(group)) throw new DomainException("Material group is invalid.");
        Id = Guid.NewGuid(); PurchaseId = purchaseId; Group = group; Amount = amount;
        Description = MaterialPurchase.Optional(description, 300, "Purchase line description");
    }
    public Guid Id { get; private set; }
    public Guid PurchaseId { get; private set; }
    public MaterialGroup Group { get; private set; }
    public string? Description { get; private set; }
    public decimal Amount { get; private set; }
}

public sealed class MaterialPurchasePayment
{
    private MaterialPurchasePayment() { }
    public MaterialPurchasePayment(Guid purchaseId, decimal amount, DateTime paidAtUtc, PaymentMethod method,
        string idempotencyKey, string? note, DateTime nowUtc, Guid? createdById)
    {
        if (purchaseId == Guid.Empty) throw new DomainException("Purchase is required.");
        FinanceValue.ValidatePositive(amount, "Payment amount");
        FinanceValue.ValidatePayment(paidAtUtc, method, idempotencyKey);
        Id = Guid.NewGuid(); PurchaseId = purchaseId; Amount = amount; PaidAt = paidAtUtc; Method = method;
        IdempotencyKey = idempotencyKey.Trim(); Note = MaterialPurchase.Optional(note, 1000, "Payment note");
        Status = PaymentTransactionStatus.Succeeded; CreatedAt = nowUtc; CreatedById = createdById;
    }
    public Guid Id { get; private set; }
    public Guid PurchaseId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime PaidAt { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public PaymentTransactionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
}

public sealed class ExpensePayment
{
    private ExpensePayment() { }
    public ExpensePayment(Guid expenseId, decimal amount, DateTime paidAtUtc, PaymentMethod method,
        string idempotencyKey, string? note, DateTime nowUtc, Guid? createdById)
    {
        if (expenseId == Guid.Empty) throw new DomainException("Expense is required.");
        FinanceValue.ValidatePositive(amount, "Payment amount");
        FinanceValue.ValidatePayment(paidAtUtc, method, idempotencyKey);
        Id = Guid.NewGuid(); ExpenseId = expenseId; Amount = amount; PaidAt = paidAtUtc; Method = method;
        IdempotencyKey = idempotencyKey.Trim(); Note = MaterialPurchase.Optional(note, 1000, "Payment note");
        Status = PaymentTransactionStatus.Succeeded; CreatedAt = nowUtc; CreatedById = createdById;
    }
    public Guid Id { get; private set; }
    public Guid ExpenseId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTime PaidAt { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public PaymentTransactionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
}

public sealed class FinancePeriod
{
    private FinancePeriod() { }
    public FinancePeriod(DateOnly from, DateOnly to, decimal? openingCashBalance, DateTime nowUtc, Guid? actorId)
    {
        if (from == default || to == default || from > to) throw new DomainException("Finance period range is invalid.");
        if (openingCashBalance.HasValue) FinanceValue.ValidateNonNegative(openingCashBalance.Value, "Opening cash balance");
        Id = Guid.NewGuid(); From = from; To = to; OpeningCashBalance = openingCashBalance;
        Status = FinancePeriodStatus.Draft; DataVersion = 1; CreatedAt = nowUtc; UpdatedAt = nowUtc; CreatedById = actorId;
    }
    public Guid Id { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public decimal? OpeningCashBalance { get; private set; }
    public FinancePeriodStatus Status { get; private set; }
    public long DataVersion { get; private set; }
    public long? StocktakeConfirmedVersion { get; private set; }
    public long? ShippingConfirmedVersion { get; private set; }
    public long? ExpensesConfirmedVersion { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public Guid? ClosedById { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? CreatedById { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsFullyConfirmed => StocktakeConfirmedVersion == DataVersion
        && ShippingConfirmedVersion == DataVersion && ExpensesConfirmedVersion == DataVersion;

    public void EnsureEditable()
    {
        if (Status == FinancePeriodStatus.Closed) throw new DomainException("The finance period is closed and must be reopened before changes are made.");
    }
    public void Invalidate(DateTime nowUtc)
    {
        EnsureEditable(); DataVersion++; StocktakeConfirmedVersion = null; ShippingConfirmedVersion = null;
        ExpensesConfirmedVersion = null; UpdatedAt = nowUtc;
    }
    public void ConfirmStocktake(DateTime nowUtc) { EnsureEditable(); StocktakeConfirmedVersion = DataVersion; UpdatedAt = nowUtc; }
    public void ConfirmShipping(DateTime nowUtc) { EnsureEditable(); ShippingConfirmedVersion = DataVersion; UpdatedAt = nowUtc; }
    public void ConfirmExpenses(DateTime nowUtc) { EnsureEditable(); ExpensesConfirmedVersion = DataVersion; UpdatedAt = nowUtc; }
    public void UpdateOpeningCashBalance(decimal? value, DateTime nowUtc)
    {
        EnsureEditable();
        if (value.HasValue) FinanceValue.ValidateNonNegative(value.Value, "Opening cash balance");
        if (OpeningCashBalance == value) return;
        OpeningCashBalance = value;
        Invalidate(nowUtc);
    }
    public void Close(DateTime nowUtc, Guid? actorId)
    {
        EnsureEditable();
        if (!IsFullyConfirmed) throw new DomainException("Stocktake, shipping and expenses must all be confirmed for the current data version.");
        Status = FinancePeriodStatus.Closed; ClosedAt = nowUtc; ClosedById = actorId; UpdatedAt = nowUtc;
    }
    public void Reopen(DateTime nowUtc)
    {
        if (Status != FinancePeriodStatus.Closed) throw new DomainException("Only a closed period can be reopened.");
        Status = FinancePeriodStatus.Draft; ClosedAt = null; ClosedById = null; Invalidate(nowUtc);
    }
}

public sealed class MaterialStocktakeLine
{
    private MaterialStocktakeLine() { }
    public MaterialStocktakeLine(Guid periodId, MaterialGroup group, decimal openingValue, decimal endingValue,
        decimal wasteValue, decimal adjustmentValue, bool isEstimated, string? note, DateTime nowUtc)
    {
        Id = Guid.NewGuid(); PeriodId = periodId; Group = group;
        Update(openingValue, endingValue, wasteValue, adjustmentValue, isEstimated, note, nowUtc);
    }
    public Guid Id { get; private set; }
    public Guid PeriodId { get; private set; }
    public MaterialGroup Group { get; private set; }
    public decimal OpeningValue { get; private set; }
    public decimal EndingValue { get; private set; }
    public decimal WasteValue { get; private set; }
    public decimal AdjustmentValue { get; private set; }
    public bool IsEstimated { get; private set; }
    public string? Note { get; private set; }
    public StocktakeStatus Status { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? ConfirmedById { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public void Update(decimal openingValue, decimal endingValue, decimal wasteValue, decimal adjustmentValue,
        bool isEstimated, string? note, DateTime nowUtc)
    {
        FinanceValue.ValidateNonNegative(openingValue, "Opening inventory");
        FinanceValue.ValidateNonNegative(endingValue, "Ending inventory");
        FinanceValue.ValidateNonNegative(wasteValue, "Waste");
        FinanceValue.ValidateMoney(adjustmentValue, "Adjustment");
        OpeningValue = openingValue; EndingValue = endingValue; WasteValue = wasteValue;
        AdjustmentValue = adjustmentValue; IsEstimated = isEstimated;
        Note = MaterialPurchase.Optional(note, 1000, "Stocktake note"); Status = StocktakeStatus.Estimated;
        ConfirmedAt = null; ConfirmedById = null; UpdatedAt = nowUtc;
    }
    public void Confirm(DateTime nowUtc, Guid? actorId) { Status = StocktakeStatus.Confirmed; ConfirmedAt = nowUtc; ConfirmedById = actorId; }
}

public sealed class FinancePeriodEvent
{
    private FinancePeriodEvent() { }
    public FinancePeriodEvent(Guid periodId, FinancePeriodEventKind kind, long dataVersion, string? reason,
        DateTime occurredAtUtc, Guid? actorId)
    {
        Id = Guid.NewGuid(); PeriodId = periodId; Kind = kind; DataVersion = dataVersion;
        Reason = MaterialPurchase.Optional(reason, 1000, "Period event reason"); OccurredAt = occurredAtUtc; ActorUserId = actorId;
    }
    public Guid Id { get; private set; }
    public Guid PeriodId { get; private set; }
    public FinancePeriodEventKind Kind { get; private set; }
    public long DataVersion { get; private set; }
    public string? Reason { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public Guid? ActorUserId { get; private set; }
}

internal static class FinanceValue
{
    public static void ValidateMoney(decimal value, string field)
    {
        if (Math.Abs(value) > Expense.MaximumAmount || decimal.Round(value, 2, MidpointRounding.AwayFromZero) != value)
            throw new DomainException($"{field} is outside the supported money range or has more than two decimal places.");
    }
    public static void ValidatePositive(decimal value, string field) { ValidateMoney(value, field); if (value <= 0) throw new DomainException($"{field} must be greater than zero."); }
    public static void ValidateNonNegative(decimal value, string field) { ValidateMoney(value, field); if (value < 0) throw new DomainException($"{field} cannot be negative."); }
    public static void ValidatePayment(DateTime paidAtUtc, PaymentMethod method, string key)
    {
        if (paidAtUtc.Kind != DateTimeKind.Utc) throw new DomainException("Payment time must be UTC.");
        if (!Enum.IsDefined(method)) throw new DomainException("Payment method is invalid.");
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100) throw new DomainException("A payment idempotency key of at most 100 characters is required.");
    }
}
