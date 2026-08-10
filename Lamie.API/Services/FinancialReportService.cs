using System.Globalization;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Reports;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class FinancialReportService : IFinancialReportService
{
    private const int MaximumRangeDays = 366;
    private const int AutomaticDailyRangeDays = 62;
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public FinancialReportService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<FinancialReportDto> GetAsync(
        FinancialReportQuery query,
        CancellationToken cancellationToken)
    {
        var generatedAt = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(generatedAt.ToOffset(BusinessOffset).DateTime);
        var period = ResolveRange(query, today);
        var startUtc = new DateTimeOffset(
            period.From.ToDateTime(TimeOnly.MinValue),
            BusinessOffset).UtcDateTime;
        var endExclusiveUtc = new DateTimeOffset(
            period.To.AddDays(1).ToDateTime(TimeOnly.MinValue),
            BusinessOffset).UtcDateTime;

        var revenueRows = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.DeliveryAt >= startUtc &&
                order.DeliveryAt < endExclusiveUtc &&
                order.PaymentStatus == PaymentStatus.Paid &&
                order.OrderStatus != OrderStatus.Cancelled)
            .GroupBy(order => order.DeliveryAt.AddHours(BusinessOffset.TotalHours).Date)
            .Select(group => new
            {
                Date = group.Key,
                Revenue = group.Sum(order => order.TotalAmount),
                OrderCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        var expenseRows = await _dbContext.Expenses
            .AsNoTracking()
            .Where(expense => expense.ExpenseDate >= period.From && expense.ExpenseDate <= period.To)
            .GroupBy(expense => expense.ExpenseDate)
            .Select(group => new
            {
                Date = group.Key,
                Expense = group.Sum(expense => expense.Amount),
                ExpenseCount = group.Count()
            })
            .ToListAsync(cancellationToken);

        var categoryRows = await (
                from expense in _dbContext.Expenses.AsNoTracking()
                join category in _dbContext.ExpenseCategories.AsNoTracking()
                    on expense.ExpenseCategoryId equals category.Id
                where expense.ExpenseDate >= period.From && expense.ExpenseDate <= period.To
                group expense by new { category.Id, category.Name }
                into categoryExpenses
                select new
                {
                    categoryExpenses.Key.Id,
                    categoryExpenses.Key.Name,
                    TotalAmount = categoryExpenses.Sum(expense => expense.Amount),
                    ExpenseCount = categoryExpenses.Count()
                })
            .OrderByDescending(row => row.TotalAmount)
            .ThenBy(row => row.Name)
            .ToListAsync(cancellationToken);

        var revenueByDate = revenueRows.ToDictionary(
            row => DateOnly.FromDateTime(row.Date),
            row => new DailyRevenue(row.Revenue, row.OrderCount));
        var expenseByDate = expenseRows.ToDictionary(
            row => row.Date,
            row => new DailyExpense(row.Expense, row.ExpenseCount));
        var points = period.GroupBy == "month"
            ? BuildMonthlyPoints(period.From, period.To, revenueByDate, expenseByDate)
            : BuildDailyPoints(period.From, period.To, revenueByDate, expenseByDate);
        var revenue = points.Sum(point => point.Revenue);
        var totalExpense = points.Sum(point => point.Expense);
        var profit = revenue - totalExpense;
        decimal? margin = revenue == 0
            ? null
            : decimal.Round(profit / revenue * 100, 2, MidpointRounding.AwayFromZero);

        return new FinancialReportDto(
            period,
            generatedAt.ToUniversalTime(),
            revenue,
            totalExpense,
            profit,
            margin,
            points.Sum(point => point.OrderCount),
            points.Sum(point => point.ExpenseCount),
            points,
            categoryRows.Select(row => new FinancialReportExpenseCategoryDto(
                row.Id,
                row.Name,
                row.TotalAmount,
                row.ExpenseCount)).ToList(),
            "Doanh thu gồm đơn đã thanh toán, không bị hủy, theo ngày giao hàng tại múi giờ Việt Nam.",
            "Lợi nhuận trong báo cáo bằng doanh thu trừ chi phí đã ghi nhận; chưa bao gồm giá vốn chưa được nhập vào hệ thống.");
    }

    public static FinancialReportPeriodDto ResolveRange(FinancialReportQuery query, DateOnly today)
    {
        DateOnly rangeStart;
        DateOnly rangeEnd;
        if (!query.From.HasValue && !query.To.HasValue)
        {
            rangeStart = new DateOnly(today.Year, today.Month, 1);
            rangeEnd = today;
        }
        else
        {
            var errors = new Dictionary<string, string[]>();
            if (!query.From.HasValue)
                errors[nameof(query.From)] = ["From date is required when to date is provided."];
            if (!query.To.HasValue)
                errors[nameof(query.To)] = ["To date is required when from date is provided."];
            if (errors.Count > 0)
                throw new ValidationException(errors);

            rangeStart = query.From!.Value;
            rangeEnd = query.To!.Value;
        }

        var days = rangeEnd.DayNumber - rangeStart.DayNumber + 1;
        var validationErrors = new Dictionary<string, string[]>();
        if (days <= 0)
            validationErrors[nameof(query.From)] = ["From date cannot be after to date."];
        if (days > MaximumRangeDays)
            validationErrors[nameof(query.To)] = [$"Report range cannot exceed {MaximumRangeDays} days."];

        var requestedGroup = query.GroupBy?.Trim().ToLowerInvariant() ?? "auto";
        if (requestedGroup is not ("auto" or "day" or "month"))
            validationErrors[nameof(query.GroupBy)] = ["Group by must be one of: auto, day, month."];
        if (validationErrors.Count > 0)
            throw new ValidationException(validationErrors);

        var resolvedGroup = requestedGroup == "auto"
            ? days <= AutomaticDailyRangeDays ? "day" : "month"
            : requestedGroup;
        return new FinancialReportPeriodDto(rangeStart, rangeEnd, resolvedGroup, days);
    }

    public static bool CountsAsRevenue(PaymentStatus paymentStatus, OrderStatus orderStatus) =>
        paymentStatus == PaymentStatus.Paid && orderStatus != OrderStatus.Cancelled;

    private static IReadOnlyList<FinancialReportPointDto> BuildDailyPoints(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyDictionary<DateOnly, DailyRevenue> revenueByDate,
        IReadOnlyDictionary<DateOnly, DailyExpense> expenseByDate)
    {
        var points = new List<FinancialReportPointDto>();
        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            revenueByDate.TryGetValue(date, out var revenue);
            expenseByDate.TryGetValue(date, out var expense);
            points.Add(CreatePoint(
                date,
                date,
                date.ToString("dd/MM", CultureInfo.InvariantCulture),
                revenue,
                expense));
        }

        return points;
    }

    private static IReadOnlyList<FinancialReportPointDto> BuildMonthlyPoints(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyDictionary<DateOnly, DailyRevenue> revenueByDate,
        IReadOnlyDictionary<DateOnly, DailyExpense> expenseByDate)
    {
        var points = new List<FinancialReportPointDto>();
        for (var month = new DateOnly(rangeStart.Year, rangeStart.Month, 1);
             month <= rangeEnd;
             month = month.AddMonths(1))
        {
            var bucketStart = month < rangeStart ? rangeStart : month;
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var bucketEnd = monthEnd > rangeEnd ? rangeEnd : monthEnd;
            var revenue = new DailyRevenue(
                revenueByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Amount),
                revenueByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Count));
            var expense = new DailyExpense(
                expenseByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Amount),
                expenseByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Count));
            points.Add(CreatePoint(
                bucketStart,
                bucketEnd,
                month.ToString("MM/yyyy", CultureInfo.InvariantCulture),
                revenue,
                expense));
        }

        return points;
    }

    private static FinancialReportPointDto CreatePoint(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        string label,
        DailyRevenue? revenue,
        DailyExpense? expense)
    {
        var revenueAmount = revenue?.Amount ?? 0;
        var expenseAmount = expense?.Amount ?? 0;
        return new FinancialReportPointDto(
            rangeStart,
            rangeEnd,
            label,
            revenueAmount,
            expenseAmount,
            revenueAmount - expenseAmount,
            revenue?.Count ?? 0,
            expense?.Count ?? 0);
    }

    private sealed record DailyRevenue(decimal Amount, int Count);
    private sealed record DailyExpense(decimal Amount, int Count);
}
