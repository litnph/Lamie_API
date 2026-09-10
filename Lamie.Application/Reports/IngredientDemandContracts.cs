using Lamie.Domain.Entities;

namespace Lamie.Application.Reports;

public sealed class IngredientDemandQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Statuses { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public enum IngredientDemandSource
{
    CapturedSnapshot = 1,
    LegacyCurrentRecipeFallback = 2,
    LegacyWithoutSnapshot = 3
}

public sealed record IngredientDemandPeriodDto(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    string DateBasis);

public sealed record IngredientDemandPackDto(
    int ConversionId,
    string Code,
    string Name,
    string UnitName,
    string? UnitSymbol,
    decimal FactorToBase,
    long Count);

public sealed record IngredientDemandBreakdownDto(
    decimal TotalBaseQuantity,
    IReadOnlyList<IngredientDemandPackDto> Packs,
    decimal BaseRemainder);

public sealed record IngredientDemandSummaryDto(
    int? IngredientId,
    string IngredientCode,
    string IngredientName,
    string BaseUnitCode,
    string BaseUnitName,
    string? BaseUnitSymbol,
    decimal TotalBaseQuantity,
    IngredientDemandBreakdownDto Breakdown);

public sealed record IngredientDemandLineDto(
    int? IngredientId,
    string IngredientCode,
    string IngredientName,
    string BaseUnitCode,
    string BaseUnitName,
    string? BaseUnitSymbol,
    decimal PerProductBaseQuantity,
    int ProductQuantity,
    decimal TotalBaseQuantity,
    string? Note);

public sealed record IngredientDemandOrderItemDto(
    Guid OrderItemId,
    int? ProductId,
    string? ProductSku,
    string ProductName,
    int Quantity,
    IngredientDemandSource Source,
    IReadOnlyList<IngredientDemandLineDto> Ingredients);

public sealed record IngredientDemandOrderTotalDto(
    int? IngredientId,
    string IngredientCode,
    string IngredientName,
    string BaseUnitCode,
    string BaseUnitName,
    string? BaseUnitSymbol,
    decimal TotalBaseQuantity);

public sealed record IngredientDemandOrderDto(
    Guid OrderId,
    string OrderCode,
    DateTimeOffset DeliveryAt,
    OrderStatus Status,
    bool UsesLegacyRecipeFallback,
    IReadOnlyList<IngredientDemandOrderItemDto> Items,
    IReadOnlyList<IngredientDemandOrderTotalDto> Totals);

public sealed record PagedIngredientDemandOrdersDto(
    IReadOnlyList<IngredientDemandOrderDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record IngredientDemandReportDto(
    IngredientDemandPeriodDto Period,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<OrderStatus> Statuses,
    IReadOnlyList<IngredientDemandSummaryDto> Summary,
    PagedIngredientDemandOrdersDto Details,
    bool HasLegacyRecipeFallback);

public interface IIngredientDemandReportService
{
    Task<IngredientDemandReportDto> GetAsync(
        IngredientDemandQuery query,
        CancellationToken cancellationToken);
}
