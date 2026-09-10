namespace Lamie.Application.Ingredients;

public sealed class CatalogListQuery
{
    public string? Search { get; init; }
    public bool IncludeInactive { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record MeasurementUnitDto(
    int Id,
    string Code,
    string Name,
    string? Symbol,
    bool AllowsFractional,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? ReferenceCount = null,
    bool? CanDelete = null);

public sealed record SaveMeasurementUnitRequest(
    string Code,
    string Name,
    string? Symbol,
    bool AllowsFractional,
    bool IsActive);

public sealed record IngredientConversionInputDto(
    int? Id,
    string Code,
    string Name,
    int UnitId,
    decimal FactorToBase,
    int SortOrder,
    bool IsActive);

public sealed record IngredientConversionDto(
    int Id,
    string Code,
    string Name,
    MeasurementUnitDto Unit,
    decimal FactorToBase,
    int SortOrder,
    bool IsActive);

public sealed record IngredientDto(
    int Id,
    string Code,
    string Name,
    MeasurementUnitDto BaseUnit,
    string? Note,
    bool IsActive,
    IReadOnlyList<IngredientConversionDto> Conversions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ProductCount,
    int SnapshotCount,
    bool CanDelete,
    int InventoryUnitCount = 0);

public sealed record SaveIngredientRequest(
    string Code,
    string Name,
    int BaseUnitId,
    string? Note,
    bool IsActive,
    IReadOnlyList<IngredientConversionInputDto> Conversions);

public sealed record PagedMeasurementUnitsDto(
    IReadOnlyList<MeasurementUnitDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record PagedIngredientsDto(
    IReadOnlyList<IngredientDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public interface IIngredientCatalogService
{
    Task<PagedMeasurementUnitsDto> ListUnitsAsync(CatalogListQuery query, CancellationToken cancellationToken);
    Task<MeasurementUnitDto> GetUnitAsync(int id, CancellationToken cancellationToken);
    Task<int> CreateUnitAsync(SaveMeasurementUnitRequest request, CancellationToken cancellationToken);
    Task UpdateUnitAsync(int id, SaveMeasurementUnitRequest request, CancellationToken cancellationToken);
    Task DeleteUnitAsync(int id, CancellationToken cancellationToken);

    Task<PagedIngredientsDto> ListIngredientsAsync(CatalogListQuery query, CancellationToken cancellationToken);
    Task<IngredientDto> GetIngredientAsync(int id, CancellationToken cancellationToken);
    Task<int> CreateIngredientAsync(SaveIngredientRequest request, CancellationToken cancellationToken);
    Task UpdateIngredientAsync(int id, SaveIngredientRequest request, CancellationToken cancellationToken);
    Task DeleteIngredientAsync(int id, CancellationToken cancellationToken);
}
