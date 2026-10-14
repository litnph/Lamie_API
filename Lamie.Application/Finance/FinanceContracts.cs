using Lamie.Domain.Entities;

namespace Lamie.Application.Finance;

public sealed record PurchaseLineRequest(MaterialGroup Group, string? Description, decimal Amount);
public sealed record SaveMaterialPurchaseRequest(DateOnly PurchaseDate, DateOnly ReceivedDate, string VendorName,
    string? VendorAddress, string? VendorTaxId, DocumentReviewStatus DocumentStatus,
    TaxEligibilityStatus TaxEligibilityStatus, string? DocumentReference, string? AttachmentUrl,
    string? Notes, IReadOnlyList<PurchaseLineRequest> Lines, DateOnly? InvoiceDate = null);
public sealed record AddFinancePaymentRequest(decimal Amount, DateTimeOffset PaidAt, PaymentMethod Method,
    string IdempotencyKey, string? Note);
public sealed record FinancePaymentDto(Guid Id, decimal Amount, DateTimeOffset PaidAt, PaymentMethod Method,
    PaymentTransactionStatus Status, string? Note);
public sealed record MaterialPurchaseLineDto(Guid Id, MaterialGroup Group, string? Description, decimal Amount);
public sealed record MaterialPurchaseDto(Guid Id, DateOnly PurchaseDate, DateOnly ReceivedDate, string VendorName,
    string? VendorAddress, string? VendorTaxId, DocumentReviewStatus DocumentStatus,
    TaxEligibilityStatus TaxEligibilityStatus, string? DocumentReference, string? AttachmentUrl, string? Notes,
    decimal TotalAmount, decimal PaidAmount, decimal OutstandingAmount,
    IReadOnlyList<MaterialPurchaseLineDto> Lines, IReadOnlyList<FinancePaymentDto> Payments,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public DateOnly? InvoiceDate { get; init; }
}

public sealed record StocktakeLineRequest(MaterialGroup Group, decimal OpeningValue, decimal EndingValue,
    decimal WasteValue, decimal AdjustmentValue, bool IsEstimated, string? Note);
public sealed record SaveFinancePeriodRequest(DateOnly From, DateOnly To, decimal? OpeningCashBalance);
public sealed record SaveStocktakeRequest(IReadOnlyList<StocktakeLineRequest> Lines);
public sealed record ReopenFinancePeriodRequest(string Reason);
public sealed record StocktakeLineDto(Guid Id, MaterialGroup Group, decimal OpeningValue, decimal PurchasesValue,
    decimal EndingValue, decimal WasteValue, decimal AdjustmentValue, decimal ConsumptionValue,
    decimal ProductCogsValue, bool IsEstimated, StocktakeStatus Status, string? Note);
public sealed record FinancePeriodEventDto(Guid Id, FinancePeriodEventKind Kind, long DataVersion,
    string? Reason, DateTimeOffset OccurredAt, Guid? ActorUserId);
public sealed record FinancePeriodDto(Guid Id, DateOnly From, DateOnly To, decimal? OpeningCashBalance,
    FinancePeriodStatus Status, long DataVersion, bool StocktakeConfirmed, bool ShippingConfirmed,
    bool ExpensesConfirmed, bool CanClose, DateTimeOffset? ClosedAt,
    IReadOnlyList<StocktakeLineDto> Stocktake, IReadOnlyList<FinancePeriodEventDto> History);

public sealed record MaterialGroupSummaryDto(MaterialGroup Group, decimal OpeningValue, decimal PurchasesValue,
    decimal EndingValue, decimal ConsumptionValue, decimal WasteValue, decimal ProductCogsValue, bool IsEstimated);
public sealed record FinanceDocumentSummaryDto(DocumentReviewStatus Status, int Count, decimal Amount);
public sealed record ManagerialFinanceSummaryDto(DateOnly From, DateOnly To, Guid? PeriodId,
    FinancePeriodStatus? PeriodStatus, bool IsProvisional, IReadOnlyList<string> ProvisionalReasons,
    decimal ProductRevenue, decimal CustomerShippingFee, decimal ActualCarrierCost,
    decimal OperatingExpense, decimal MaterialProductCogs, decimal MaterialWaste,
    decimal ProfitBeforeTax, decimal? ProfitMarginPercent,
    decimal? OpeningCashBalance, decimal CashIn, decimal CashOut, decimal? ClosingCashBalance,
    decimal SupplierDebt, decimal OperatingExpenseDebt, IReadOnlyList<MaterialGroupSummaryDto> Materials,
    IReadOnlyList<FinanceDocumentSummaryDto> Documents, bool TaxConfigured,
    string TaxMessage, int UnreconciledShippingCount, int IncompleteExpensePaymentCount);

public interface IFinanceOperationsService
{
    Task<IReadOnlyList<MaterialPurchaseDto>> ListPurchasesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
    Task<MaterialPurchaseDto> GetPurchaseAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> CreatePurchaseAsync(SaveMaterialPurchaseRequest request, CancellationToken cancellationToken);
    Task UpdatePurchaseAsync(Guid id, SaveMaterialPurchaseRequest request, CancellationToken cancellationToken);
    Task AddPurchasePaymentAsync(Guid id, AddFinancePaymentRequest request, CancellationToken cancellationToken);
    Task AddExpensePaymentAsync(Guid id, AddFinancePaymentRequest request, CancellationToken cancellationToken);
    Task<FinancePeriodDto> GetOrCreatePeriodAsync(SaveFinancePeriodRequest request, CancellationToken cancellationToken);
    Task<FinancePeriodDto> GetPeriodAsync(Guid id, CancellationToken cancellationToken);
    Task SaveStocktakeAsync(Guid id, SaveStocktakeRequest request, CancellationToken cancellationToken);
    Task ConfirmStocktakeAsync(Guid id, CancellationToken cancellationToken);
    Task ConfirmShippingAsync(Guid id, CancellationToken cancellationToken);
    Task ConfirmExpensesAsync(Guid id, CancellationToken cancellationToken);
    Task ClosePeriodAsync(Guid id, CancellationToken cancellationToken);
    Task ReopenPeriodAsync(Guid id, ReopenFinancePeriodRequest request, CancellationToken cancellationToken);
    Task<ManagerialFinanceSummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface IFinancePeriodGuard
{
    Task InvalidateAsync(DateOnly affectedDate, string reason, CancellationToken cancellationToken);
}
