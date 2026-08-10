namespace Lamie.Application.Reports;

public sealed class FinancialReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string GroupBy { get; init; } = "auto";
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
    int ExpenseCount);

public sealed record FinancialReportExpenseCategoryDto(
    Guid ExpenseCategoryId,
    string ExpenseCategoryName,
    decimal TotalAmount,
    int ExpenseCount);

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
    string ProfitBasis);

public sealed record ReportFileDto(
    byte[] Content,
    string ContentType,
    string FileName);

public interface IFinancialReportService
{
    Task<FinancialReportDto> GetAsync(FinancialReportQuery query, CancellationToken cancellationToken);
}

public interface IFinancialReportExportService
{
    ReportFileDto CreateExcel(FinancialReportDto report);
    ReportFileDto CreatePrintableHtml(FinancialReportDto report);
}
