using System.Globalization;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Dashboard;
using Lamie.Application.Orders;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class DashboardService : IDashboardService
{
    private const int LowStockThreshold = 5;
    private const int UpcomingDeliveryHours = 24;
    private const int LocalUtcOffsetHours = 7;
    private const int MaximumListItems = 5;
    private static readonly IReadOnlyDictionary<string, (int Days, string Label)> Periods =
        new Dictionary<string, (int Days, string Label)>(StringComparer.OrdinalIgnoreCase)
        {
            ["7d"] = (7, "7 ngày gần đây"),
            ["30d"] = (30, "30 ngày gần đây"),
            ["90d"] = (90, "90 ngày gần đây")
        };

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public DashboardService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<DashboardDto> GetAsync(
        string period,
        bool includeShippingFeeInRevenue,
        CancellationToken cancellationToken)
    {
        var generatedAt = _timeProvider.GetUtcNow();
        var range = CreatePeriod(period, generatedAt);
        var currentStartUtc = range.CurrentStart.UtcDateTime;
        var currentEndUtc = range.CurrentEnd.UtcDateTime;
        var previousStartUtc = range.PreviousStart.UtcDateTime;
        var nowUtc = generatedAt.UtcDateTime;
        var upcomingLimitUtc = nowUtc.AddHours(UpcomingDeliveryHours);

        var newOrdersCount = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(order => order.CreatedAt >= currentStartUtc && order.CreatedAt <= currentEndUtc, cancellationToken);

        var activeStats = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.OrderStatus == OrderStatus.Created ||
                order.OrderStatus == OrderStatus.Confirmed ||
                order.OrderStatus == OrderStatus.Producing ||
                order.OrderStatus == OrderStatus.Ready ||
                order.OrderStatus == OrderStatus.Shipping ||
                order.OrderStatus == OrderStatus.DeliveryFailed ||
                order.OrderStatus == OrderStatus.Delivered)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Awaiting = group.Count(order => order.OrderStatus == OrderStatus.Created),
                Preparing = group.Count(order => order.OrderStatus == OrderStatus.Producing),
                Shipping = group.Count(order => order.OrderStatus == OrderStatus.Shipping)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var attentionOrders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.OrderStatus == OrderStatus.Created || order.OrderStatus == OrderStatus.Confirmed || order.OrderStatus == OrderStatus.Producing)
            .OrderBy(order => order.OrderStatus == OrderStatus.Created ? 0 : 1)
            .ThenBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Take(MaximumListItems)
            .ToListAsync(cancellationToken);

        var riskQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                (order.OrderStatus == OrderStatus.Created ||
                 order.OrderStatus == OrderStatus.Confirmed ||
                 order.OrderStatus == OrderStatus.Producing ||
                 order.OrderStatus == OrderStatus.Ready ||
                 order.OrderStatus == OrderStatus.Shipping ||
                 order.OrderStatus == OrderStatus.DeliveryFailed) &&
                order.DeliveryAt <= upcomingLimitUtc);
        var riskStats = await riskQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Late = group.Count(order => order.DeliveryAt < nowUtc),
                Upcoming = group.Count(order => order.DeliveryAt >= nowUtc)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var riskOrders = await riskQuery
            .OrderBy(order => order.DeliveryAt < nowUtc ? 0 : 1)
            .ThenBy(order => order.DeliveryAt)
            .ThenBy(order => order.Id)
            .Take(MaximumListItems)
            .ToListAsync(cancellationToken);

        var revenueByDay = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.DeliveredAt.HasValue &&
                order.DeliveredAt.Value >= previousStartUtc &&
                order.DeliveredAt.Value <= currentEndUtc &&
                (order.OrderStatus == OrderStatus.Delivered || order.OrderStatus == OrderStatus.Completed))
            .GroupBy(order => order.DeliveredAt!.Value.AddHours(LocalUtcOffsetHours).Date)
            .Select(group => new
            {
                Date = group.Key,
                ProductRevenue = group.Sum(order => order.SubTotal - order.DiscountTotal),
                ShippingFee = group.Sum(order => (decimal?)order.ShippingFee) ?? 0,
                OrderCount = group.Count()
            })
            .ToListAsync(cancellationToken);
        var revenue = BuildRevenue(
            range,
            revenueByDay.Select(item =>
                new DailyRevenue(item.Date, item.ProductRevenue, item.ShippingFee, item.OrderCount)).ToList(),
            includeShippingFeeInRevenue);

        var inventoryStats = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.TracksInventory && product.Stock <= LowStockThreshold)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Low = group.Count(product => product.Stock > 0),
                Out = group.Count(product => product.Stock <= 0)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var productsAtRisk = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.TracksInventory && product.Stock <= LowStockThreshold)
            .Select(product => new
            {
                product.Id,
                product.Sku,
                product.Stock,
                Name = _dbContext.ProductTranslations
                    .Where(translation => translation.ProductId == product.Id && translation.LanguageCode.StartsWith("vi"))
                    .OrderBy(translation => translation.Id)
                    .Select(translation => translation.Name)
                    .FirstOrDefault() ??
                    _dbContext.ProductTranslations
                        .Where(translation => translation.ProductId == product.Id)
                        .OrderBy(translation => translation.Id)
                        .Select(translation => translation.Name)
                        .FirstOrDefault() ?? product.Sku,
                ThumbnailUrl = product.ThumbnailUrl ?? _dbContext.ProductImages
                    .Where(image => image.ProductId == product.Id && image.IsActive)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .OrderBy(product => product.Stock)
            .ThenBy(product => product.Name)
            .Take(MaximumListItems)
            .Select(product => new DashboardStockProductDto(
                product.Id,
                product.Sku,
                product.Name,
                product.Stock,
                product.ThumbnailUrl))
            .ToListAsync(cancellationToken);

        var deliveryRisks = riskOrders.Select(order => ToDeliveryRisk(
            OrderService.ToListDto(order),
            order.DeliveryAt < nowUtc ? "late" : "upcoming")).ToList();
        var active = new DashboardActiveOrdersDto(
            activeStats?.Awaiting ?? 0,
            activeStats?.Preparing ?? 0,
            activeStats?.Shipping ?? 0,
            (activeStats?.Awaiting ?? 0) + (activeStats?.Preparing ?? 0),
            riskStats?.Late ?? 0,
            riskStats?.Upcoming ?? 0,
            attentionOrders.Select(order => OrderService.ToListDto(order)).ToList(),
            deliveryRisks);
        var inventory = new DashboardInventoryDto(
            inventoryStats?.Low ?? 0,
            inventoryStats?.Out ?? 0,
            productsAtRisk);

        var todayLocal = DateOnly.FromDateTime(generatedAt.ToOffset(TimeSpan.FromHours(LocalUtcOffsetHours)).DateTime);
        var todayStart = new DateTimeOffset(todayLocal.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(LocalUtcOffsetHours)).UtcDateTime;
        var tomorrowStart = new DateTimeOffset(todayLocal.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(LocalUtcOffsetHours)).UtcDateTime;
        var upcomingEnd = new DateTimeOffset(todayLocal.AddDays(7).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(LocalUtcOffsetHours)).UtcDateTime;
        var upcomingOrders = await _dbContext.Orders.AsNoTracking()
            .Where(order => order.DeliveryAt >= todayStart
                && order.DeliveryAt < upcomingEnd
                && (order.OrderStatus == OrderStatus.Created
                    || order.OrderStatus == OrderStatus.Confirmed
                    || order.OrderStatus == OrderStatus.Producing
                    || order.OrderStatus == OrderStatus.Ready
                    || order.OrderStatus == OrderStatus.Shipping
                    || order.OrderStatus == OrderStatus.DeliveryFailed))
            .OrderBy(order => order.DeliveryAt)
            .ThenBy(order => order.OrderCode)
            .Include(order => order.Items)
            .Include(order => order.Images)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var upcomingIds = upcomingOrders.Select(order => order.Id).ToArray();
        var paymentRows = await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => upcomingIds.Contains(payment.OrderId)
                && payment.Status == PaymentTransactionStatus.Succeeded)
            .GroupBy(payment => payment.OrderId)
            .Select(group => new
            {
                OrderId = group.Key,
                Amount = group.Sum(payment => payment.Kind == OrderPaymentKind.Refund ? -payment.Amount : payment.Amount),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);
        var paymentByOrder = paymentRows.ToDictionary(row => row.OrderId, row => (row.Amount, row.Count));
        var upcomingDtos = upcomingOrders.Select(order =>
        {
            var payment = paymentByOrder.GetValueOrDefault(order.Id);
            return OrderService.ToListDto(order, payment.Amount, payment.Count);
        }).ToList();
        var deliveryDays = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = todayLocal.AddDays(offset);
            var count = upcomingOrders.Count(order =>
                DateOnly.FromDateTime(order.DeliveryAt.AddHours(LocalUtcOffsetHours)) == date);
            return new DashboardDeliveryDayDto(date, count);
        }).ToList();
        var overdueBeforeToday = await _dbContext.Orders.AsNoTracking().CountAsync(order =>
            order.DeliveryAt < todayStart
            && (order.OrderStatus == OrderStatus.Created
                || order.OrderStatus == OrderStatus.Confirmed
                || order.OrderStatus == OrderStatus.Producing
                || order.OrderStatus == OrderStatus.Ready
                || order.OrderStatus == OrderStatus.Shipping
                || order.OrderStatus == OrderStatus.DeliveryFailed), cancellationToken);

        var todayCompleted = await _dbContext.Orders.AsNoTracking()
            .Where(order => order.DeliveredAt.HasValue
                && order.DeliveredAt.Value >= todayStart
                && order.DeliveredAt.Value < tomorrowStart
                && (order.OrderStatus == OrderStatus.Delivered || order.OrderStatus == OrderStatus.Completed))
            .ToListAsync(cancellationToken);
        var todayPayments = await _dbContext.OrderPayments.AsNoTracking()
            .Where(payment => payment.PaidAt >= todayStart
                && payment.PaidAt < tomorrowStart
                && payment.Status == PaymentTransactionStatus.Succeeded)
            .ToListAsync(cancellationToken);
        var todayShippingPaid = await _dbContext.Orders.AsNoTracking()
            .Where(order => order.ShippingFeeActual.HasValue
                && order.ShippingFeePaidAt.HasValue
                && order.ShippingFeePaidAt.Value >= todayStart
                && order.ShippingFeePaidAt.Value < tomorrowStart)
            .SumAsync(order => order.ShippingFeeActual ?? 0, cancellationToken);
        var todayOtherPaid = await _dbContext.Expenses.AsNoTracking()
            .Where(expense => expense.PaidAt.HasValue
                && expense.PaidAt.Value >= todayStart
                && expense.PaidAt.Value < tomorrowStart)
            .SumAsync(expense => expense.Amount, cancellationToken);
        var deposits = todayPayments.Where(payment => payment.Kind is OrderPaymentKind.Deposit or OrderPaymentKind.Full)
            .Sum(payment => payment.Amount);
        var remainders = todayPayments.Where(payment => payment.Kind == OrderPaymentKind.Remainder)
            .Sum(payment => payment.Amount);
        var refunds = todayPayments.Where(payment => payment.Kind == OrderPaymentKind.Refund)
            .Sum(payment => payment.Amount);
        var cashIn = deposits + remainders;
        var cashOut = refunds + todayShippingPaid + todayOtherPaid;
        var productRevenueToday = todayCompleted.Sum(order => order.SubTotal - order.DiscountTotal);
        var shippingRevenueToday = todayCompleted.Sum(order => order.ShippingFee);
        var missingHistory = await _dbContext.Orders.AsNoTracking().CountAsync(order =>
                order.PaymentStatus != PaymentStatus.Unpaid
                && !_dbContext.OrderPayments.Any(payment => payment.OrderId == order.Id), cancellationToken)
            + await _dbContext.Expenses.AsNoTracking().CountAsync(expense => expense.PaymentHistoryIncomplete, cancellationToken);

        var v2 = new DashboardV2Dto(
            new DashboardUpcomingDto(
                todayLocal,
                todayLocal.AddDays(6),
                upcomingDtos.Count,
                upcomingOrders.Count(order => order.DeliveryAt < nowUtc || order.OrderStatus == OrderStatus.Created),
                upcomingDtos.Sum(order => order.TotalAmount),
                upcomingDtos.Sum(order => order.PaidAmount),
                upcomingDtos.Sum(order => order.RemainingAmount),
                overdueBeforeToday,
                deliveryDays,
                upcomingDtos.Where(order => order.DeliveryAt >= todayStart && order.DeliveryAt < tomorrowStart).Take(6).ToList()),
            new DashboardTodayFinanceDto(
                todayLocal,
                todayCompleted.Count,
                productRevenueToday,
                shippingRevenueToday,
                productRevenueToday + shippingRevenueToday,
                cashIn,
                cashOut,
                cashIn - cashOut,
                deposits,
                remainders,
                todayShippingPaid,
                todayOtherPaid + refunds,
                missingHistory));

        return new DashboardDto(range, generatedAt, newOrdersCount, active, revenue, inventory, []) { V2 = v2 };
    }

    public static DashboardPeriodRangeDto CreatePeriod(string period, DateTimeOffset generatedAt)
    {
        var normalizedPeriod = period?.Trim() ?? string.Empty;
        if (!Periods.TryGetValue(normalizedPeriod, out var option))
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(period)] = ["Period must be one of: 7d, 30d, 90d."]
            });

        var localNow = generatedAt.ToOffset(TimeSpan.FromHours(LocalUtcOffsetHours));
        var currentStartLocal = new DateTimeOffset(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            0,
            0,
            0,
            localNow.Offset).AddDays(-(option.Days - 1));
        var currentStart = currentStartLocal.ToUniversalTime();
        var previousStart = currentStartLocal.AddDays(-option.Days).ToUniversalTime();

        return new DashboardPeriodRangeDto(
            normalizedPeriod.ToLowerInvariant(),
            option.Label,
            option.Days,
            currentStart,
            generatedAt.ToUniversalTime(),
            previousStart,
            currentStart.AddTicks(-1));
    }

    public static bool CountsAsRevenue(PaymentStatus paymentStatus, OrderStatus orderStatus) =>
        orderStatus is OrderStatus.Delivered or OrderStatus.Completed;

    private static DashboardRevenueDto BuildRevenue(
        DashboardPeriodRangeDto range,
        IReadOnlyCollection<DailyRevenue> dailyRows,
        bool includeShippingFeeInRevenue)
    {
        var offset = TimeSpan.FromHours(LocalUtcOffsetHours);
        var currentStartDate = range.CurrentStart.ToOffset(offset).Date;
        var previousStartDate = range.PreviousStart.ToOffset(offset).Date;
        var currentEndDate = range.CurrentEnd.ToOffset(offset).Date;
        var previousEndDate = range.PreviousEnd.ToOffset(offset).Date;
        var currentRows = dailyRows.Where(row => row.Date >= currentStartDate && row.Date <= currentEndDate).ToList();
        var previousRows = dailyRows.Where(row => row.Date >= previousStartDate && row.Date <= previousEndDate).ToList();
        var bucketSize = Math.Max(1, (int)Math.Ceiling(range.Days / 12d));
        var bucketCount = (int)Math.Ceiling(range.Days / (double)bucketSize);
        var points = new List<RevenuePointDto>(bucketCount);

        for (var index = 0; index < bucketCount; index++)
        {
            var bucketStart = currentStartDate.AddDays(index * bucketSize);
            var bucketEndExclusive = bucketStart.AddDays(bucketSize);
            var effectiveEndExclusive = bucketEndExclusive > currentEndDate.AddDays(1)
                ? currentEndDate.AddDays(1)
                : bucketEndExclusive;
            var matching = currentRows.Where(row => row.Date >= bucketStart && row.Date < effectiveEndExclusive).ToList();
            var endLabelDate = effectiveEndExclusive.AddDays(-1);
            var startLabel = bucketStart.ToString("dd/MM", CultureInfo.InvariantCulture);
            var endLabel = endLabelDate.ToString("dd/MM", CultureInfo.InvariantCulture);
            var key = new DateTimeOffset(bucketStart, offset).ToUniversalTime();
            var productRevenue = matching.Sum(row => row.ProductRevenue);
            var shippingFee = matching.Sum(row => row.ShippingFee);
            points.Add(new RevenuePointDto(
                key,
                bucketSize == 1 ? startLabel : $"{startLabel} đến {endLabel}",
                startLabel,
                productRevenue + (includeShippingFeeInRevenue ? shippingFee : 0),
                matching.Sum(row => row.OrderCount))
            {
                ProductRevenue = productRevenue,
                ShippingFee = shippingFee
            });
        }

        var currentProductRevenue = currentRows.Sum(row => row.ProductRevenue);
        var currentShippingFee = currentRows.Sum(row => row.ShippingFee);
        var previousProductRevenue = previousRows.Sum(row => row.ProductRevenue);
        var previousShippingFee = previousRows.Sum(row => row.ShippingFee);
        return new DashboardRevenueDto(
            currentProductRevenue + (includeShippingFeeInRevenue ? currentShippingFee : 0),
            previousProductRevenue + (includeShippingFeeInRevenue ? previousShippingFee : 0),
            currentRows.Sum(row => row.OrderCount),
            points)
        {
            CurrentProductRevenue = currentProductRevenue,
            CurrentShippingFee = currentShippingFee,
            PreviousProductRevenue = previousProductRevenue,
            PreviousShippingFee = previousShippingFee,
            IncludeShippingFeeInRevenue = includeShippingFeeInRevenue
        };
    }

    private static DashboardDeliveryRiskDto ToDeliveryRisk(OrderListItemDto order, string state) => new(
        order.Id,
        order.OrderCode,
        order.OrdererName,
        order.OrdererPhone,
        order.ChannelId,
        order.RecipientName ?? string.Empty,
        order.RecipientPhone ?? string.Empty,
        order.PickupAtShop,
        order.DeliveryAddress,
        order.DeliveryAt,
        order.DepositAmount,
        order.ShippingFee,
        order.ShippingFeeActual,
        order.SubTotal,
        order.TotalAmount,
        order.PaymentStatus,
        order.OrderStatus,
        order.CreatedAt,
        state);

    private sealed record DailyRevenue(
        DateTime Date,
        decimal ProductRevenue,
        decimal ShippingFee,
        int OrderCount);
}
