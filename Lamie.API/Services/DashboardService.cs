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

    public async Task<DashboardDto> GetAsync(string period, CancellationToken cancellationToken)
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
                order.OrderStatus == OrderStatus.Producing ||
                order.OrderStatus == OrderStatus.Shipping)
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
            .Where(order => order.OrderStatus == OrderStatus.Created || order.OrderStatus == OrderStatus.Producing)
            .OrderBy(order => order.OrderStatus == OrderStatus.Created ? 0 : 1)
            .ThenBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .Take(MaximumListItems)
            .ToListAsync(cancellationToken);

        var riskQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                (order.OrderStatus == OrderStatus.Created ||
                 order.OrderStatus == OrderStatus.Producing ||
                 order.OrderStatus == OrderStatus.Shipping) &&
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
                order.CreatedAt >= previousStartUtc &&
                order.CreatedAt <= currentEndUtc &&
                order.PaymentStatus == PaymentStatus.Paid &&
                order.OrderStatus != OrderStatus.Cancelled)
            .GroupBy(order => order.CreatedAt.AddHours(LocalUtcOffsetHours).Date)
            .Select(group => new
            {
                Date = group.Key,
                Revenue = group.Sum(order => order.TotalAmount),
                OrderCount = group.Count()
            })
            .ToListAsync(cancellationToken);
        var revenue = BuildRevenue(range, revenueByDay.Select(item =>
            new DailyRevenue(item.Date, item.Revenue, item.OrderCount)).ToList());

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
            attentionOrders.Select(OrderService.ToListDto).ToList(),
            deliveryRisks);
        var inventory = new DashboardInventoryDto(
            inventoryStats?.Low ?? 0,
            inventoryStats?.Out ?? 0,
            productsAtRisk);

        return new DashboardDto(range, generatedAt, newOrdersCount, active, revenue, inventory, []);
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
        paymentStatus == PaymentStatus.Paid && orderStatus != OrderStatus.Cancelled;

    private static DashboardRevenueDto BuildRevenue(
        DashboardPeriodRangeDto range,
        IReadOnlyCollection<DailyRevenue> dailyRows)
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
            points.Add(new RevenuePointDto(
                key,
                bucketSize == 1 ? startLabel : $"{startLabel} đến {endLabel}",
                startLabel,
                matching.Sum(row => row.Revenue),
                matching.Sum(row => row.OrderCount)));
        }

        return new DashboardRevenueDto(
            currentRows.Sum(row => row.Revenue),
            previousRows.Sum(row => row.Revenue),
            currentRows.Sum(row => row.OrderCount),
            points);
    }

    private static DashboardDeliveryRiskDto ToDeliveryRisk(OrderListItemDto order, string state) => new(
        order.Id,
        order.OrderCode,
        order.OrdererName,
        order.OrdererPhone,
        order.ChannelId,
        order.RecipientName,
        order.RecipientPhone,
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

    private sealed record DailyRevenue(DateTime Date, decimal Revenue, int OrderCount);
}
