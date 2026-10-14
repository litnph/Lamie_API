namespace Lamie.Application.Reports;

public sealed class FinancialReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string GroupBy { get; init; } = "auto";
    // Preserve the pre-existing API meaning of Revenue for callers that do not send the new option.
    public bool IncludeShippingFeeInRevenue { get; init; } = true;
}

public sealed record FinancialReportPeriodDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,
    int Days);

public sealed record FinancialReportPointDto(
    DateOnly From,
    DateOnly To,
    string Label,
    decimal Revenue,
    decimal Expense,
    decimal Profit,
    int OrderCount,
    int ExpenseCount)
{
    public decimal ProductRevenue { get; init; }
    public decimal ShippingFee { get; init; }
    public decimal ShippingFeeActual { get; init; }
    public decimal OtherExpense { get; init; }
}

public sealed record FinancialReportExpenseCategoryDto(
    Guid ExpenseCategoryId,
    string ExpenseCategoryName,
    decimal TotalAmount,
    int ExpenseCount);

public sealed record ProductRevenueBreakdownDto(
    int? Id,
    string Name,
    decimal Revenue);

public sealed record DailyRevenueOrderDto(
    Guid Id,
    string Code,
    DateOnly DeliveryDate,
    DateTimeOffset CompletedAt,
    decimal ProductRevenue,
    decimal ShippingFee,
    decimal TotalRevenue);

public sealed record DailyRevenueAdjustmentDto(
    Guid Id,
    string OrderCode,
    DateOnly Date,
    DateTimeOffset OccurredAt,
    decimal ProductRevenueAdjustment,
    decimal ShippingRevenueAdjustment,
    decimal TotalAdjustment,
    string? Note);

public sealed record FinancialReportDto(
    FinancialReportPeriodDto Period,
    DateTimeOffset GeneratedAt,
    decimal Revenue,
    decimal Expense,
    decimal Profit,
    decimal? ProfitMarginPercent,
    int OrderCount,
    int ExpenseCount,
    IReadOnlyList<FinancialReportPointDto> Points,
    IReadOnlyList<FinancialReportExpenseCategoryDto> ExpensesByCategory,
    string RevenueBasis,
    string ProfitBasis)
{
    public decimal ProductRevenue { get; init; }
    public decimal ShippingFee { get; init; }
    public decimal ShippingFeeActual { get; init; }
    public decimal OtherExpense { get; init; }
    public decimal TemporaryDifference { get; init; }
    public int IncompleteExpenseHistoryCount { get; init; }
    public int UnreconciledShippingCount { get; init; }
    public decimal MaterialProductCogs { get; init; }
    public decimal MaterialWaste { get; init; }
    public bool IsProfitProvisional { get; init; }
    public IReadOnlyList<string> ProfitProvisionalReasons { get; init; } = [];
    public bool IncludeShippingFeeInRevenue { get; init; }
    public IReadOnlyList<ProductRevenueBreakdownDto> RevenueByProductLine { get; init; } = [];
    public IReadOnlyList<ProductRevenueBreakdownDto> RevenueByCategory { get; init; } = [];
    public IReadOnlyList<DailyRevenueOrderDto> DailyOrders { get; init; } = [];
    public IReadOnlyList<DailyRevenueAdjustmentDto> DailyRevenueAdjustments { get; init; } = [];
}

public sealed record ReportFileDto(
    byte[] Content,
    string ContentType,
    string FileName);

public interface IFinancialReportService
{
    Task<FinancialReportDto> GetAsync(FinancialReportQuery query, CancellationToken cancellationToken);
    Task<CashFlowReportDto> GetCashFlowAsync(FinancialReportQuery query, CancellationToken cancellationToken);
}

public sealed record CashFlowPointDto(
    DateOnly Date,
    string Label,
    decimal CashIn,
    decimal CashOut,
    decimal NetCash);

public sealed record CashTransactionDto(
    string Id,
    DateTimeOffset OccurredAt,
    string Direction,
    string Kind,
    string Reference,
    DateOnly? DeliveryDate,
    string Method,
    decimal Amount,
    string? Note,
    bool NeedsSupplementation = false);

public sealed record CashFlowTodayDto(
    DateOnly Date,
    decimal Deposits,
    decimal Remainders,
    decimal ShippingPaid,
    decimal OtherExpensesPaid,
    decimal NetCash);

public sealed record CashFlowReportDto(
    FinancialReportPeriodDto Period,
    DateTimeOffset GeneratedAt,
    decimal CashIn,
    decimal CashOut,
    decimal NetCash,
    IReadOnlyList<CashFlowPointDto> Points,
    IReadOnlyList<CashTransactionDto> Transactions,
    CashFlowTodayDto Today,
    int IncompleteHistoryCount);

public interface IFinancialReportExportService
{
    ReportFileDto CreateExcel(FinancialReportDto report);
    ReportFileDto CreatePrintableHtml(FinancialReportDto report);
}
