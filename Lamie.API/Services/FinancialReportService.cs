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
                order.DeliveredAt.HasValue &&
                order.DeliveredAt.Value >= startUtc &&
                order.DeliveredAt.Value < endExclusiveUtc &&
                (order.OrderStatus == OrderStatus.Delivered || order.OrderStatus == OrderStatus.Completed))
            .GroupBy(order => order.DeliveredAt!.Value.AddHours(BusinessOffset.TotalHours).Date)
            .Select(group => new
            {
                Date = group.Key,
                ProductRevenue = group.Sum(order => order.SubTotal - order.DiscountTotal),
                ShippingFee = group.Sum(order => (decimal?)order.ShippingFee) ?? 0,
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

        var shippingExpenseRows = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.ShippingFeeActual.HasValue &&
                order.ShippingFeeIncurredAt.HasValue &&
                order.ShippingFeeIncurredAt.Value >= startUtc &&
                order.ShippingFeeIncurredAt.Value < endExclusiveUtc &&
                order.OrderStatus != OrderStatus.Cancelled)
            .GroupBy(order => order.ShippingFeeIncurredAt!.Value.AddHours(BusinessOffset.TotalHours).Date)
            .Select(group => new
            {
                Date = group.Key,
                Amount = group.Sum(order => order.ShippingFeeActual ?? 0)
            })
            .ToListAsync(cancellationToken);

        var refundRows = await (
                from payment in _dbContext.OrderPayments.AsNoTracking()
                join order in _dbContext.Orders.AsNoTracking() on payment.OrderId equals order.Id
                where payment.Kind == OrderPaymentKind.Refund
                      && payment.Status == PaymentTransactionStatus.Succeeded
                      && payment.AffectsRecognizedRevenue
                      && payment.PaidAt >= startUtc
                      && payment.PaidAt < endExclusiveUtc
                select new
                {
                    payment.Id,
                    payment.OrderId,
                    payment.PaidAt,
                    payment.ProductRevenueAdjustment,
                    payment.ShippingRevenueAdjustment,
                    payment.Note,
                    order.OrderCode
                })
            .ToListAsync(cancellationToken);

        var dailyOrderRows = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.DeliveredAt.HasValue &&
                order.DeliveredAt.Value >= startUtc &&
                order.DeliveredAt.Value < endExclusiveUtc &&
                (order.OrderStatus == OrderStatus.Delivered || order.OrderStatus == OrderStatus.Completed))
            .Select(order => new
            {
                order.Id,
                order.OrderCode,
                DeliveredAt = order.DeliveredAt!.Value,
                ProductRevenue = order.SubTotal - order.DiscountTotal,
                order.ShippingFee
            })
            .OrderBy(row => row.DeliveredAt)
            .ToListAsync(cancellationToken);

        var deliveredBreakdownRows = await (
                from item in _dbContext.OrderItems.AsNoTracking()
                join order in _dbContext.Orders.AsNoTracking() on item.OrderId equals order.Id
                join product in _dbContext.Products.AsNoTracking()
                    on item.ProductId equals (int?)product.Id into products
                from product in products.DefaultIfEmpty()
                where order.DeliveredAt.HasValue
                      && order.DeliveredAt.Value >= startUtc
                      && order.DeliveredAt.Value < endExclusiveUtc
                      && (order.OrderStatus == OrderStatus.Delivered || order.OrderStatus == OrderStatus.Completed)
                select new RevenueBreakdownRow(
                    item.OrderId,
                    item.ProductTypeId,
                    item.ProductTypeName,
                    item.CategoryId ?? (int?)product.CategoryId,
                    item.CategoryName,
                    item.LineTotal,
                    order.SubTotal == 0 ? 0 : item.LineTotal * (order.SubTotal - order.DiscountTotal) / order.SubTotal))
            .ToListAsync(cancellationToken);

        var refundOrderIds = refundRows.Select(row => row.OrderId).Distinct().ToList();
        var refundSourceRows = refundOrderIds.Count == 0
            ? []
            : await (
                    from item in _dbContext.OrderItems.AsNoTracking()
                    join product in _dbContext.Products.AsNoTracking()
                        on item.ProductId equals (int?)product.Id into products
                    from product in products.DefaultIfEmpty()
                    where refundOrderIds.Contains(item.OrderId)
                    select new RevenueBreakdownRow(
                        item.OrderId,
                        item.ProductTypeId,
                        item.ProductTypeName,
                        item.CategoryId ?? (int?)product.CategoryId,
                        item.CategoryName,
                        item.LineTotal,
                        0))
                .ToListAsync(cancellationToken);
        var breakdownRows = deliveredBreakdownRows.ToList();
        foreach (var refund in refundRows.Where(row => row.ProductRevenueAdjustment > 0))
        {
            var sources = refundSourceRows.Where(row => row.OrderId == refund.OrderId).ToList();
            var sourceTotal = sources.Sum(row => row.LineTotal);
            decimal allocated = 0;
            for (var index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                var amount = index == sources.Count - 1
                    ? refund.ProductRevenueAdjustment - allocated
                    : sourceTotal == 0 ? 0 : decimal.Round(
                        refund.ProductRevenueAdjustment * source.LineTotal / sourceTotal, 2,
                        MidpointRounding.AwayFromZero);
                allocated += amount;
                breakdownRows.Add(source with { Revenue = -amount });
            }
        }

        var productLineRows = breakdownRows
            .GroupBy(row => new { row.ProductTypeId, row.ProductTypeName })
            .Select(group => new ProductRevenueBreakdownDto(group.Key.ProductTypeId,
                group.Key.ProductTypeName ?? "Chưa phân loại", group.Sum(row => row.Revenue)))
            .ToList();
        var categoryRevenueRows = breakdownRows
            .GroupBy(row => new { row.CategoryId, row.CategoryName })
            .Select(group => new ProductRevenueBreakdownDto(group.Key.CategoryId,
                group.Key.CategoryName ?? string.Empty, group.Sum(row => row.Revenue)))
            .ToList();

        var categoryIds = categoryRevenueRows
            .Where(row => row.Id.HasValue)
            .Select(row => row.Id!.Value)
            .Distinct()
            .ToList();
        var categoryTranslations = await _dbContext.CategoryTranslations
            .AsNoTracking()
            .Where(translation => categoryIds.Contains(translation.CategoryId))
            .Select(translation => new { translation.CategoryId, translation.LanguageCode, translation.Name })
            .ToListAsync(cancellationToken);
        var categoryNames = categoryTranslations
            .GroupBy(translation => translation.CategoryId)
            .ToDictionary(
                group => group.Key,
                group => PreferredTranslationName(group.Select(item => (item.LanguageCode, item.Name))));

        var deliveredRevenueByDate = revenueRows.ToDictionary(
            row => DateOnly.FromDateTime(row.Date),
            row => new DailyRevenue(row.ProductRevenue, row.ShippingFee, row.OrderCount));
        var refundsByDate = refundRows
            .GroupBy(row => DateOnly.FromDateTime(row.PaidAt.Add(BusinessOffset)))
            .ToDictionary(group => group.Key, group => new
            {
                Product = group.Sum(row => row.ProductRevenueAdjustment),
                Shipping = group.Sum(row => row.ShippingRevenueAdjustment)
            });
        var revenueByDate = deliveredRevenueByDate.Keys.Concat(refundsByDate.Keys).Distinct().ToDictionary(
            date => date,
            date => new DailyRevenue(
                (deliveredRevenueByDate.GetValueOrDefault(date)?.ProductRevenue ?? 0) - (refundsByDate.GetValueOrDefault(date)?.Product ?? 0),
                (deliveredRevenueByDate.GetValueOrDefault(date)?.ShippingFee ?? 0) - (refundsByDate.GetValueOrDefault(date)?.Shipping ?? 0),
                deliveredRevenueByDate.GetValueOrDefault(date)?.Count ?? 0));
        var shippingExpenseByDate = shippingExpenseRows.ToDictionary(
            row => DateOnly.FromDateTime(row.Date),
            row => row.Amount);
        var expenseByDate = expenseRows.ToDictionary(
            row => row.Date,
            row => new DailyExpense(row.Expense, row.ExpenseCount));
        var points = period.GroupBy == "month"
            ? BuildMonthlyPoints(period.From, period.To, revenueByDate, shippingExpenseByDate, expenseByDate, query.IncludeShippingFeeInRevenue)
            : BuildDailyPoints(period.From, period.To, revenueByDate, shippingExpenseByDate, expenseByDate, query.IncludeShippingFeeInRevenue);
        var revenue = points.Sum(point => point.Revenue);
        var productRevenue = points.Sum(point => point.ProductRevenue);
        var shippingFee = points.Sum(point => point.ShippingFee);
        var totalShippingActual = points.Sum(point => point.ShippingFeeActual);
        var totalOtherExpense = points.Sum(point => point.OtherExpense);
        var totalExpense = totalShippingActual + totalOtherExpense;
        var recognizedRevenueForProfit = productRevenue + shippingFee;
        var financePeriod = await _dbContext.FinancePeriods.AsNoTracking()
            .SingleOrDefaultAsync(item => item.From == period.From && item.To == period.To, cancellationToken);
        var stocktakeRows = financePeriod is null
            ? []
            : await _dbContext.MaterialStocktakeLines.AsNoTracking()
                .Where(item => item.PeriodId == financePeriod.Id).ToListAsync(cancellationToken);
        var materialPurchases = await (
                from purchase in _dbContext.MaterialPurchases.AsNoTracking()
                join line in _dbContext.MaterialPurchaseLines.AsNoTracking() on purchase.Id equals line.PurchaseId
                where purchase.ReceivedDate >= period.From && purchase.ReceivedDate <= period.To
                group line by line.Group into groupLines
                select new { Group = groupLines.Key, Amount = groupLines.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Group, item => item.Amount, cancellationToken);
        var materialWaste = stocktakeRows.Sum(item => item.WasteValue);
        var materialProductCogs = stocktakeRows.Sum(item =>
            item.OpeningValue + materialPurchases.GetValueOrDefault(item.Group) + item.AdjustmentValue
            - item.EndingValue - item.WasteValue);
        var provisionalReasons = new List<string>();
        if (financePeriod is null) provisionalReasons.Add("Chưa tạo kỳ tài chính trùng với phạm vi báo cáo.");
        else if (financePeriod.StocktakeConfirmedVersion != financePeriod.DataVersion) provisionalReasons.Add("Kiểm kê kỳ chưa được xác nhận cho phiên bản dữ liệu hiện tại.");
        if (stocktakeRows.Count == 0) provisionalReasons.Add("Chưa có dữ liệu tồn đầu và tồn cuối.");
        if (stocktakeRows.Any(item => item.IsEstimated)) provisionalReasons.Add("Kiểm kê còn giá trị ước tính.");
        var profit = recognizedRevenueForProfit - materialProductCogs - materialWaste - totalExpense;
        decimal? margin = recognizedRevenueForProfit == 0
            ? null
            : decimal.Round(profit / recognizedRevenueForProfit * 100, 2, MidpointRounding.AwayFromZero);

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
            "Doanh thu gồm tiền hoa sau giảm giá và phí ship thu khách của đơn hoàn tất, trừ các giao dịch hoàn được xác nhận ảnh hưởng doanh thu. Giao hàng ghi theo ngày hoàn tất, hoàn doanh thu ghi theo ngày điều chỉnh tại múi giờ Việt Nam.",
            provisionalReasons.Count > 0
                ? "Lợi nhuận trước thuế bằng doanh thu sản phẩm và phí ship thu khách, trừ giá vốn nguyên liệu vào sản phẩm, hao hụt, phí ship thực tế và chi phí vận hành. Số liệu đang tạm tính do kỳ chưa đủ điều kiện xác nhận."
                : "Lợi nhuận trước thuế bằng doanh thu sản phẩm và phí ship thu khách, trừ giá vốn nguyên liệu vào sản phẩm, hao hụt, phí ship thực tế và chi phí vận hành.")
        {
            ProductRevenue = productRevenue,
            ShippingFee = shippingFee,
            IncludeShippingFeeInRevenue = query.IncludeShippingFeeInRevenue,
            ShippingFeeActual = totalShippingActual,
            OtherExpense = totalOtherExpense,
            TemporaryDifference = profit,
            IncompleteExpenseHistoryCount = await _dbContext.Orders.AsNoTracking().CountAsync(
                order => order.ShippingFeeActual.HasValue && !order.ShippingFeeIncurredAt.HasValue,
                cancellationToken),
            UnreconciledShippingCount = await _dbContext.Orders.AsNoTracking().CountAsync(
                order => order.DeliveredAt.HasValue
                    && order.DeliveredAt.Value >= startUtc
                    && order.DeliveredAt.Value < endExclusiveUtc
                    && (order.OrderStatus == OrderStatus.Delivered || order.OrderStatus == OrderStatus.Completed)
                    && !order.ShippingFeeActual.HasValue,
                cancellationToken),
            MaterialProductCogs = materialProductCogs,
            MaterialWaste = materialWaste,
            IsProfitProvisional = provisionalReasons.Count > 0,
            ProfitProvisionalReasons = provisionalReasons,
            RevenueByProductLine = productLineRows
                .GroupBy(row => row.Id)
                .Select(group => new ProductRevenueBreakdownDto(
                    group.Key,
                    group.Select(row => row.Name).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Chưa phân loại",
                    group.Sum(row => row.Revenue)))
                .OrderByDescending(row => row.Revenue)
                .ThenBy(row => row.Name)
                .ToList(),
            RevenueByCategory = categoryRevenueRows
                .GroupBy(row => row.Id)
                .Select(group =>
                {
                    var snapshotName = group.Select(row => row.Name)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                    var name = snapshotName
                        ?? (group.Key.HasValue && categoryNames.TryGetValue(group.Key.Value, out var categoryName)
                            ? categoryName
                            : "Chưa phân loại");
                    return new ProductRevenueBreakdownDto(group.Key, name, group.Sum(row => row.Revenue));
                })
                .OrderByDescending(row => row.Revenue)
                .ThenBy(row => row.Name)
                .ToList(),
            DailyOrders = dailyOrderRows.Select(row =>
            {
                var deliveredAt = ToOffset(row.DeliveredAt);
                var deliveryDate = DateOnly.FromDateTime(deliveredAt.ToOffset(BusinessOffset).DateTime);
                return new DailyRevenueOrderDto(
                    row.Id,
                    row.OrderCode,
                    deliveryDate,
                    deliveredAt,
                    row.ProductRevenue,
                    row.ShippingFee,
                    row.ProductRevenue + row.ShippingFee);
            }).ToList(),
            DailyRevenueAdjustments = refundRows.Select(row => new DailyRevenueAdjustmentDto(
                row.Id,
                row.OrderCode,
                DateOnly.FromDateTime(row.PaidAt.Add(BusinessOffset)),
                ToOffset(row.PaidAt),
                row.ProductRevenueAdjustment,
                row.ShippingRevenueAdjustment,
                row.ProductRevenueAdjustment + row.ShippingRevenueAdjustment,
                row.Note)).ToList()
        };
    }

    public async Task<CashFlowReportDto> GetCashFlowAsync(
        FinancialReportQuery query,
        CancellationToken cancellationToken)
    {
        var generatedAt = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(generatedAt.ToOffset(BusinessOffset).DateTime);
        var period = ResolveRange(query, today);
        // The reconciliation block is always about the current Vietnam business day,
        // even when the selected report range does not include today.
        var queryFrom = period.From < today ? period.From : today;
        var queryTo = period.To > today ? period.To : today;
        var startUtc = new DateTimeOffset(queryFrom.ToDateTime(TimeOnly.MinValue), BusinessOffset).UtcDateTime;
        var endExclusiveUtc = new DateTimeOffset(queryTo.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).UtcDateTime;

        var payments = await (
                from payment in _dbContext.OrderPayments.AsNoTracking()
                join order in _dbContext.Orders.AsNoTracking() on payment.OrderId equals order.Id
                where payment.PaidAt >= startUtc
                      && payment.PaidAt < endExclusiveUtc
                      && payment.Status == PaymentTransactionStatus.Succeeded
                select new { Payment = payment, Order = order })
            .ToListAsync(cancellationToken);
        var shippingPayments = await _dbContext.Orders.AsNoTracking()
            .Where(order => order.ShippingFeeActual.HasValue
                && order.ShippingFeePaidAt.HasValue
                && order.ShippingFeePaidAt.Value >= startUtc
                && order.ShippingFeePaidAt.Value < endExclusiveUtc
                && order.OrderStatus != OrderStatus.Cancelled)
            .ToListAsync(cancellationToken);
        var legacyExpensePayments = await (
                from expense in _dbContext.Expenses.AsNoTracking()
                join category in _dbContext.ExpenseCategories.AsNoTracking()
                    on expense.ExpenseCategoryId equals category.Id
                where expense.PaidAt.HasValue
                      && expense.PaidAt.Value >= startUtc
                      && expense.PaidAt.Value < endExclusiveUtc
                      && !_dbContext.ExpensePayments.Any(payment => payment.ExpenseId == expense.Id)
                select new { Expense = expense, CategoryName = category.Name })
            .ToListAsync(cancellationToken);
        var expensePayments = await (
                from payment in _dbContext.ExpensePayments.AsNoTracking()
                join expense in _dbContext.Expenses.AsNoTracking() on payment.ExpenseId equals expense.Id
                join category in _dbContext.ExpenseCategories.AsNoTracking() on expense.ExpenseCategoryId equals category.Id
                where payment.PaidAt >= startUtc && payment.PaidAt < endExclusiveUtc
                      && payment.Status == PaymentTransactionStatus.Succeeded
                select new { Payment = payment, Expense = expense, CategoryName = category.Name })
            .ToListAsync(cancellationToken);
        var purchasePayments = await (
                from payment in _dbContext.MaterialPurchasePayments.AsNoTracking()
                join purchase in _dbContext.MaterialPurchases.AsNoTracking() on payment.PurchaseId equals purchase.Id
                where payment.PaidAt >= startUtc && payment.PaidAt < endExclusiveUtc
                      && payment.Status == PaymentTransactionStatus.Succeeded
                select new { Payment = payment, Purchase = purchase })
            .ToListAsync(cancellationToken);

        var allTransactions = payments.Select(row => new CashTransactionDto(
                row.Payment.Id.ToString(),
                ToOffset(row.Payment.PaidAt),
                row.Payment.Kind == OrderPaymentKind.Refund ? "out" : "in",
                row.Payment.Kind.ToString(),
                row.Order.OrderCode,
                DateOnly.FromDateTime((row.Order.DeliveredAt ?? row.Order.DeliveryAt).AddHours(BusinessOffset.TotalHours)),
                row.Payment.Method.ToString(),
                row.Payment.Amount,
                row.Payment.Note))
            .Concat(shippingPayments.Select(order => new CashTransactionDto(
                $"shipping-{order.Id}",
                ToOffset(order.ShippingFeePaidAt!.Value),
                "out",
                "Shipping",
                order.OrderCode,
                DateOnly.FromDateTime((order.DeliveredAt ?? order.DeliveryAt).AddHours(BusinessOffset.TotalHours)),
                order.ShippingFeePaymentMethod?.ToString() ?? "Other",
                order.ShippingFeeActual!.Value,
                "Phí ship thực tế")))
            .Concat(legacyExpensePayments.Select(row => new CashTransactionDto(
                $"expense-{row.Expense.Id}",
                ToOffset(row.Expense.PaidAt!.Value),
                "out",
                "Expense",
                row.Expense.Description,
                null,
                row.Expense.PaymentMethod?.ToString() ?? "Other",
                row.Expense.Amount,
                row.CategoryName,
                row.Expense.PaymentHistoryIncomplete)))
            .Concat(expensePayments.Select(row => new CashTransactionDto(
                row.Payment.Id.ToString(), ToOffset(row.Payment.PaidAt), "out", "Expense",
                row.Expense.Description, null, row.Payment.Method.ToString(), row.Payment.Amount,
                row.Payment.Note ?? row.CategoryName)))
            .Concat(purchasePayments.Select(row => new CashTransactionDto(
                row.Payment.Id.ToString(), ToOffset(row.Payment.PaidAt), "out", "MaterialPurchase",
                row.Purchase.VendorName, row.Purchase.ReceivedDate, row.Payment.Method.ToString(),
                row.Payment.Amount, row.Payment.Note)))
            .OrderByDescending(transaction => transaction.OccurredAt)
            .ThenByDescending(transaction => transaction.Id)
            .ToList();

        var transactions = allTransactions.Where(transaction =>
        {
            var localDate = DateOnly.FromDateTime(transaction.OccurredAt.ToOffset(BusinessOffset).DateTime);
            return localDate >= period.From && localDate <= period.To;
        }).ToList();

        var points = new List<CashFlowPointDto>();
        for (var date = period.From; date <= period.To; date = date.AddDays(1))
        {
            var daily = transactions.Where(transaction =>
                DateOnly.FromDateTime(transaction.OccurredAt.ToOffset(BusinessOffset).DateTime) == date).ToList();
            var cashIn = daily.Where(transaction => transaction.Direction == "in").Sum(transaction => transaction.Amount);
            var cashOut = daily.Where(transaction => transaction.Direction == "out").Sum(transaction => transaction.Amount);
            points.Add(new CashFlowPointDto(date, date.ToString("dd/MM", CultureInfo.InvariantCulture), cashIn, cashOut, cashIn - cashOut));
        }

        var cashInTotal = transactions.Where(transaction => transaction.Direction == "in").Sum(transaction => transaction.Amount);
        var cashOutTotal = transactions.Where(transaction => transaction.Direction == "out").Sum(transaction => transaction.Amount);
        var todayTransactions = allTransactions.Where(transaction =>
            DateOnly.FromDateTime(transaction.OccurredAt.ToOffset(BusinessOffset).DateTime) == today).ToList();
        var depositsToday = todayTransactions.Where(transaction => transaction.Direction == "in"
            && (transaction.Kind == nameof(OrderPaymentKind.Deposit) || transaction.Kind == nameof(OrderPaymentKind.Full)))
            .Sum(transaction => transaction.Amount);
        var remaindersToday = todayTransactions.Where(transaction => transaction.Direction == "in"
            && transaction.Kind == nameof(OrderPaymentKind.Remainder)).Sum(transaction => transaction.Amount);
        var shippingToday = todayTransactions.Where(transaction => transaction.Kind == "Shipping").Sum(transaction => transaction.Amount);
        var otherToday = todayTransactions.Where(transaction => transaction.Kind is "Expense" or "MaterialPurchase").Sum(transaction => transaction.Amount);

        var orderHistoryMissing = await _dbContext.Orders.AsNoTracking()
            .CountAsync(order => order.PaymentStatus != PaymentStatus.Unpaid
                && !_dbContext.OrderPayments.Any(payment => payment.OrderId == order.Id), cancellationToken);
        var expenseHistoryMissing = await _dbContext.Expenses.AsNoTracking()
            .CountAsync(expense => expense.PaymentHistoryIncomplete, cancellationToken);
        var shippingHistoryMissing = await _dbContext.Orders.AsNoTracking()
            .CountAsync(order => order.ShippingFeeActual.HasValue
                && !order.ShippingFeePaidAt.HasValue
                && order.ShippingFeePaymentIdempotencyKey != null, cancellationToken);

        return new CashFlowReportDto(
            period,
            generatedAt,
            cashInTotal,
            cashOutTotal,
            cashInTotal - cashOutTotal,
            points,
            transactions,
            new CashFlowTodayDto(
                today,
                depositsToday,
                remaindersToday,
                shippingToday,
                otherToday,
                depositsToday + remaindersToday - shippingToday - otherToday),
            orderHistoryMissing + expenseHistoryMissing + shippingHistoryMissing);
    }

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

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
        orderStatus is OrderStatus.Delivered or OrderStatus.Completed;

    private static string PreferredTranslationName(IEnumerable<(string LanguageCode, string Name)> translations)
    {
        var values = translations.ToList();
        return values.FirstOrDefault(item => item.LanguageCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase)).Name
               ?? values.FirstOrDefault(item => item.LanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)).Name
               ?? values.FirstOrDefault().Name
               ?? "Chưa phân loại";
    }

    private static IReadOnlyList<FinancialReportPointDto> BuildDailyPoints(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyDictionary<DateOnly, DailyRevenue> revenueByDate,
        IReadOnlyDictionary<DateOnly, decimal> shippingExpenseByDate,
        IReadOnlyDictionary<DateOnly, DailyExpense> expenseByDate,
        bool includeShippingFeeInRevenue)
    {
        var points = new List<FinancialReportPointDto>();
        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            revenueByDate.TryGetValue(date, out var revenue);
            shippingExpenseByDate.TryGetValue(date, out var shippingActual);
            expenseByDate.TryGetValue(date, out var expense);
            points.Add(CreatePoint(
                date,
                date,
                date.ToString("dd/MM", CultureInfo.InvariantCulture),
                revenue,
                shippingActual,
                expense,
                includeShippingFeeInRevenue));
        }

        return points;
    }

    private static IReadOnlyList<FinancialReportPointDto> BuildMonthlyPoints(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyDictionary<DateOnly, DailyRevenue> revenueByDate,
        IReadOnlyDictionary<DateOnly, decimal> shippingExpenseByDate,
        IReadOnlyDictionary<DateOnly, DailyExpense> expenseByDate,
        bool includeShippingFeeInRevenue)
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
                    .Sum(item => item.Value.ProductRevenue),
                revenueByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.ShippingFee),
                revenueByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Count));
            var expense = new DailyExpense(
                expenseByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Amount),
                expenseByDate.Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                    .Sum(item => item.Value.Count));
            var shippingActual = shippingExpenseByDate
                .Where(item => item.Key >= bucketStart && item.Key <= bucketEnd)
                .Sum(item => item.Value);
            points.Add(CreatePoint(
                bucketStart,
                bucketEnd,
                month.ToString("MM/yyyy", CultureInfo.InvariantCulture),
                revenue,
                shippingActual,
                expense,
                includeShippingFeeInRevenue));
        }

        return points;
    }

    private static FinancialReportPointDto CreatePoint(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        string label,
        DailyRevenue? revenue,
        decimal shippingActual,
        DailyExpense? expense,
        bool includeShippingFeeInRevenue)
    {
        var productRevenue = revenue?.ProductRevenue ?? 0;
        var shippingFee = revenue?.ShippingFee ?? 0;
        var revenueAmount = productRevenue + (includeShippingFeeInRevenue ? shippingFee : 0);
        var otherExpense = expense?.Amount ?? 0;
        var expenseAmount = shippingActual + otherExpense;
        return new FinancialReportPointDto(
            rangeStart,
            rangeEnd,
            label,
            revenueAmount,
            expenseAmount,
            productRevenue + shippingFee - expenseAmount,
            revenue?.Count ?? 0,
            expense?.Count ?? 0)
        {
            ProductRevenue = productRevenue,
            ShippingFee = shippingFee,
            ShippingFeeActual = shippingActual,
            OtherExpense = otherExpense
        };
    }

    private sealed record DailyRevenue(decimal ProductRevenue, decimal ShippingFee, int Count);
    private sealed record DailyExpense(decimal Amount, int Count);
    private sealed record RevenueBreakdownRow(Guid OrderId, int? ProductTypeId, string? ProductTypeName,
        int? CategoryId, string? CategoryName, decimal LineTotal, decimal Revenue);
}
