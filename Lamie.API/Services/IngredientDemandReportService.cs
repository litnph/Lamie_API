using Lamie.Application.Common.Exceptions;
using Lamie.Application.Reports;
using Lamie.Domain.Entities;
using Lamie.Domain.Ingredients;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class IngredientDemandReportService : IIngredientDemandReportService
{
    private const int MaximumRangeDays = 366;
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);
    private static readonly OrderStatus[] DefaultStatuses = [OrderStatus.Created, OrderStatus.Producing];

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public IngredientDemandReportService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IngredientDemandReportDto> GetAsync(
        IngredientDemandQuery query,
        CancellationToken cancellationToken)
    {
        var generatedAt = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(generatedAt.ToOffset(BusinessOffset).DateTime);
        var period = ResolvePeriod(query, today);
        var statuses = ResolveStatuses(query.Statuses);
        ValidatePaging(query);
        var (startUtc, endExclusiveUtc) = BusinessRangeUtc(period.From, period.To);

        var orders = await _dbContext.Orders.AsNoTracking()
            .Where(order =>
                order.DeliveryAt >= startUtc
                && order.DeliveryAt < endExclusiveUtc
                && order.OrderStatus != OrderStatus.Cancelled
                && statuses.Contains(order.OrderStatus))
            .Include(order => order.Items)
                .ThenInclude(item => item.IngredientSnapshots)
            .OrderBy(order => order.DeliveryAt)
            .ThenBy(order => order.OrderCode)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var resolvedOrders = orders.Select(ResolveOrder).ToList();
        var summary = await BuildSummaryAsync(resolvedOrders, cancellationToken);

        var totalCount = resolvedOrders.Count;
        var pageItems = resolvedOrders
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(MapOrder)
            .ToList();
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)query.PageSize);
        var hasLegacyFallback = resolvedOrders.Any(order =>
            order.Items.Any(item => item.Source == IngredientDemandSource.LegacyCurrentRecipeFallback));

        return new IngredientDemandReportDto(
            period,
            generatedAt.ToUniversalTime(),
            statuses,
            summary,
            new PagedIngredientDemandOrdersDto(
                pageItems,
                totalCount,
                query.Page,
                query.PageSize,
                totalPages,
                query.Page < totalPages,
                query.Page > 1),
            hasLegacyFallback);
    }

    public static IngredientDemandPeriodDto ResolvePeriod(IngredientDemandQuery query, DateOnly today)
    {
        DateOnly from;
        DateOnly to;
        var errors = new Dictionary<string, string[]>();
        if (!query.From.HasValue && !query.To.HasValue)
        {
            from = new DateOnly(today.Year, today.Month, 1);
            to = today;
        }
        else
        {
            if (!query.From.HasValue)
                errors[nameof(query.From)] = ["From date is required when to date is provided."];
            if (!query.To.HasValue)
                errors[nameof(query.To)] = ["To date is required when from date is provided."];
            if (errors.Count > 0)
                throw new ValidationException(errors);
            from = query.From!.Value;
            to = query.To!.Value;
        }

        var days = to.DayNumber - from.DayNumber + 1;
        if (days <= 0)
            errors[nameof(query.From)] = ["From date cannot be after to date."];
        if (days > MaximumRangeDays)
            errors[nameof(query.To)] = [$"Report range cannot exceed {MaximumRangeDays} days."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
        return new IngredientDemandPeriodDto(from, to, "Asia/Ho_Chi_Minh", "DeliveryAt");
    }

    public static IReadOnlyList<OrderStatus> ResolveStatuses(string? rawStatuses)
    {
        if (string.IsNullOrWhiteSpace(rawStatuses))
            return DefaultStatuses;

        var errors = new Dictionary<string, string[]>();
        var statuses = new List<OrderStatus>();
        foreach (var token in rawStatuses.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<OrderStatus>(token, true, out var status) || !Enum.IsDefined(status))
            {
                errors[nameof(IngredientDemandQuery.Statuses)] = [$"Unknown order status '{token}'."];
                continue;
            }
            if (status == OrderStatus.Cancelled)
            {
                errors[nameof(IngredientDemandQuery.Statuses)] = ["Cancelled orders cannot be included in ingredient demand."];
                continue;
            }
            statuses.Add(status);
        }
        if (statuses.Count == 0 && errors.Count == 0)
            errors[nameof(IngredientDemandQuery.Statuses)] = ["At least one order status is required."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
        return statuses.Distinct().OrderBy(status => (int)status).ToArray();
    }

    public static (DateTime StartUtc, DateTime EndExclusiveUtc) BusinessRangeUtc(
        DateOnly from,
        DateOnly to) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), BusinessOffset).UtcDateTime,
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).UtcDateTime);

    private static ResolvedOrder ResolveOrder(Order order)
    {
        var items = order.Items
            .OrderBy(item => item.Id)
            .Select(ResolveItem)
            .ToList();
        return new ResolvedOrder(order, items);
    }

    private static ResolvedItem ResolveItem(OrderItem item)
    {
        if (item.IngredientSnapshotCapturedAtUtc.HasValue)
        {
            return new ResolvedItem(
                item,
                IngredientDemandSource.CapturedSnapshot,
                item.IngredientSnapshots
                    .OrderBy(snapshot => snapshot.SortOrder)
                    .ThenBy(snapshot => snapshot.Id)
                    .Select(snapshot => new ResolvedIngredient(
                        snapshot.IngredientId,
                        snapshot.IngredientCode,
                        snapshot.IngredientName,
                        snapshot.BaseUnitCode,
                        snapshot.BaseUnitName,
                        snapshot.BaseUnitSymbol,
                        snapshot.PerProductBaseQuantity,
                        snapshot.ProductQuantity,
                        snapshot.TotalBaseQuantity,
                        snapshot.Note,
                        snapshot.SortOrder))
                    .ToList());
        }

        return new ResolvedItem(
            item,
            IngredientDemandSource.LegacyWithoutSnapshot,
            []);
    }

    private async Task<IReadOnlyList<IngredientDemandSummaryDto>> BuildSummaryAsync(
        IReadOnlyCollection<ResolvedOrder> orders,
        CancellationToken cancellationToken)
    {
        var groups = orders
            .SelectMany(order => order.Items)
            .SelectMany(item => item.Ingredients)
            .GroupBy(Key)
            .Select(group => Aggregate(group))
            .OrderBy(item => item.IngredientName)
            .ThenBy(item => item.IngredientCode)
            .ToList();
        var ingredientIds = groups.Where(item => item.IngredientId.HasValue)
            .Select(item => item.IngredientId!.Value)
            .Distinct()
            .ToArray();
        var options = await (
                from conversion in _dbContext.IngredientConversions.AsNoTracking()
                join unit in _dbContext.MeasurementUnits.AsNoTracking()
                    on conversion.UnitId equals unit.Id
                where ingredientIds.Contains(conversion.IngredientId)
                      && conversion.IsActive
                      && unit.IsActive
                orderby conversion.IngredientId, conversion.SortOrder, conversion.Id
                select new
                {
                    conversion.IngredientId,
                    Option = new IngredientPackOption(
                        conversion.Id,
                        conversion.Code,
                        conversion.Name,
                        unit.Name,
                        unit.Symbol,
                        conversion.FactorToBase)
                })
            .ToListAsync(cancellationToken);
        var optionsByIngredient = options
            .GroupBy(item => item.IngredientId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Option).ToArray());

        return groups.Select(group =>
        {
            var packs = group.IngredientId.HasValue
                        && optionsByIngredient.TryGetValue(group.IngredientId.Value, out var configured)
                ? configured
                : [];
            var breakdown = IngredientConversionOptimizer.Optimize(group.TotalBaseQuantity, packs);
            return new IngredientDemandSummaryDto(
                group.IngredientId,
                group.IngredientCode,
                group.IngredientName,
                group.BaseUnitCode,
                group.BaseUnitName,
                group.BaseUnitSymbol,
                group.TotalBaseQuantity,
                new IngredientDemandBreakdownDto(
                    breakdown.TotalBaseQuantity,
                    breakdown.Packs.Select(pack => new IngredientDemandPackDto(
                        pack.ConversionId,
                        pack.Code,
                        pack.Name,
                        pack.UnitName,
                        pack.UnitSymbol,
                        pack.FactorToBase,
                        pack.Count)).ToList(),
                    breakdown.BaseRemainder));
        }).ToList();
    }

    private static IngredientDemandOrderDto MapOrder(ResolvedOrder resolved)
    {
        var totals = resolved.Items
            .SelectMany(item => item.Ingredients)
            .GroupBy(Key)
            .Select(Aggregate)
            .OrderBy(item => item.IngredientName)
            .ThenBy(item => item.IngredientCode)
            .Select(item => new IngredientDemandOrderTotalDto(
                item.IngredientId,
                item.IngredientCode,
                item.IngredientName,
                item.BaseUnitCode,
                item.BaseUnitName,
                item.BaseUnitSymbol,
                item.TotalBaseQuantity))
            .ToList();
        return new IngredientDemandOrderDto(
            resolved.Order.Id,
            resolved.Order.OrderCode,
            AsOffset(resolved.Order.DeliveryAt),
            resolved.Order.OrderStatus,
            resolved.Items.Any(item => item.Source == IngredientDemandSource.LegacyCurrentRecipeFallback),
            resolved.Items.Select(item => new IngredientDemandOrderItemDto(
                item.Item.Id,
                item.Item.ProductId,
                item.Item.ProductSku,
                item.Item.ProductName,
                item.Item.Quantity,
                item.Source,
                item.Ingredients.Select(ingredient => new IngredientDemandLineDto(
                    ingredient.IngredientId,
                    ingredient.IngredientCode,
                    ingredient.IngredientName,
                    ingredient.BaseUnitCode,
                    ingredient.BaseUnitName,
                    ingredient.BaseUnitSymbol,
                    ingredient.PerProductBaseQuantity,
                    ingredient.ProductQuantity,
                    ingredient.TotalBaseQuantity,
                    ingredient.Note)).ToList())).ToList(),
            totals);
    }

    private static AggregatedIngredient Aggregate(IEnumerable<ResolvedIngredient> source)
    {
        var ingredients = source.ToList();
        var first = ingredients[0];
        var distinctIds = ingredients.Where(item => item.IngredientId.HasValue)
            .Select(item => item.IngredientId!.Value)
            .Distinct()
            .Take(2)
            .ToArray();
        return new AggregatedIngredient(
            distinctIds.Length == 1 ? distinctIds[0] : null,
            first.IngredientCode,
            first.IngredientName,
            first.BaseUnitCode,
            first.BaseUnitName,
            first.BaseUnitSymbol,
            Normalize(ingredients.Sum(item => item.TotalBaseQuantity)));
    }

    private static void ValidatePaging(IngredientDemandQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Page < 1)
            errors[nameof(query.Page)] = ["Page must be at least 1."];
        if (query.PageSize is < 1 or > 100)
            errors[nameof(query.PageSize)] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static string Key(ResolvedIngredient ingredient)
    {
        var unit = ingredient.BaseUnitCode.Trim().ToUpperInvariant();
        return ingredient.IngredientId.HasValue
            ? $"ID:{ingredient.IngredientId.Value}|UNIT:{unit}"
            : $"CODE:{ingredient.IngredientCode.Trim().ToUpperInvariant()}|UNIT:{unit}";
    }
    private static decimal Normalize(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record ResolvedOrder(Order Order, IReadOnlyList<ResolvedItem> Items);
    private sealed record ResolvedItem(
        OrderItem Item,
        IngredientDemandSource Source,
        IReadOnlyList<ResolvedIngredient> Ingredients);
    private sealed record ResolvedIngredient(
        int? IngredientId,
        string IngredientCode,
        string IngredientName,
        string BaseUnitCode,
        string BaseUnitName,
        string? BaseUnitSymbol,
        decimal PerProductBaseQuantity,
        int ProductQuantity,
        decimal TotalBaseQuantity,
        string? Note,
        int SortOrder);
    private sealed record AggregatedIngredient(
        int? IngredientId,
        string IngredientCode,
        string IngredientName,
        string BaseUnitCode,
        string BaseUnitName,
        string? BaseUnitSymbol,
        decimal TotalBaseQuantity);
}
