using Lamie.Application.Common.Exceptions;
using Lamie.Application.Ingredients;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lamie.API.Services;

public sealed class IngredientCatalogService : IIngredientCatalogService
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public IngredientCatalogService(AppDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<PagedMeasurementUnitsDto> ListUnitsAsync(
        CatalogListQuery query,
        CancellationToken cancellationToken)
    {
        ValidatePage(query);
        var units = _dbContext.MeasurementUnits.AsNoTracking();
        if (!query.IncludeInactive)
            units = units.Where(item => item.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            units = units.Where(item => item.Code.Contains(search) || item.Name.Contains(search));
        }

        var totalCount = await units.CountAsync(cancellationToken);
        var entities = await units
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var usage = await GetUnitUsageCountsAsync(entities.Select(item => item.Id), cancellationToken);
        return new PagedMeasurementUnitsDto(
            entities.Select(item => MapUnitWithUsage(item, usage.GetValueOrDefault(item.Id))).ToList(),
            totalCount,
            query.Page,
            query.PageSize,
            TotalPages(totalCount, query.PageSize),
            query.Page * query.PageSize < totalCount,
            query.Page > 1);
    }

    public async Task<MeasurementUnitDto> GetUnitAsync(int id, CancellationToken cancellationToken)
    {
        var unit = await _dbContext.MeasurementUnits.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(MeasurementUnit), id);
        var usage = await GetUnitUsageCountsAsync([id], cancellationToken);
        return MapUnitWithUsage(unit, usage.GetValueOrDefault(id));
    }

    public async Task<int> CreateUnitAsync(
        SaveMeasurementUnitRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        await EnsureUnitCodeAvailableAsync(code, null, cancellationToken);
        var unit = new MeasurementUnit(
            code,
            request.Name,
            request.Symbol,
            request.AllowsFractional,
            request.IsActive,
            UtcNow());
        _dbContext.MeasurementUnits.Add(unit);
        await SaveCatalogAsync("Measurement unit code already exists.", cancellationToken);
        return unit.Id;
    }

    public async Task UpdateUnitAsync(
        int id,
        SaveMeasurementUnitRequest request,
        CancellationToken cancellationToken)
    {
        var unit = await _dbContext.MeasurementUnits.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(MeasurementUnit), id);
        var code = NormalizeCode(request.Code);
        await EnsureUnitCodeAvailableAsync(code, id, cancellationToken);

        if (unit.AllowsFractional && !request.AllowsFractional)
        {
            var hasFractionalRecipe = await (
                    from recipe in _dbContext.ProductIngredients.AsNoTracking()
                    join ingredient in _dbContext.Ingredients.AsNoTracking()
                        on recipe.IngredientId equals ingredient.Id
                    where ingredient.BaseUnitId == id
                    select recipe.BaseQuantity)
                .AnyAsync(quantity => quantity != decimal.Truncate(quantity), cancellationToken);
            if (hasFractionalRecipe)
            {
                throw new ConflictException(
                    "The unit cannot be changed to whole numbers while fractional product recipes use it.");
            }
        }

        if (unit.IsActive && !request.IsActive)
        {
            var usedByActiveData = await _dbContext.Ingredients.AsNoTracking()
                    .AnyAsync(item => item.BaseUnitId == id && item.IsActive, cancellationToken)
                || await _dbContext.IngredientConversions.AsNoTracking()
                    .AnyAsync(item => item.UnitId == id && item.IsActive, cancellationToken);
            if (usedByActiveData)
            {
                throw new ConflictException(
                    "The unit is used by active ingredients or conversions. Deactivate those records first.");
            }
        }

        unit.Update(
            code,
            request.Name,
            request.Symbol,
            request.AllowsFractional,
            request.IsActive,
            UtcNow());
        await SaveCatalogAsync("Measurement unit code already exists.", cancellationToken);
    }

    public async Task DeleteUnitAsync(int id, CancellationToken cancellationToken)
    {
        var unit = await _dbContext.MeasurementUnits.FindAsync([id], cancellationToken)
            ?? throw new NotFoundException(nameof(MeasurementUnit), id);
        var inUse = await _dbContext.Ingredients.AsNoTracking()
                .AnyAsync(item => item.BaseUnitId == id, cancellationToken)
            || await _dbContext.IngredientConversions.AsNoTracking()
                .AnyAsync(item => item.UnitId == id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException(
                "Measurement unit is in use and cannot be deleted. Deactivate it instead.");
        }

        _dbContext.MeasurementUnits.Remove(unit);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedIngredientsDto> ListIngredientsAsync(
        CatalogListQuery query,
        CancellationToken cancellationToken)
    {
        ValidatePage(query);
        var ingredients = _dbContext.Ingredients.AsNoTracking();
        if (!query.IncludeInactive)
            ingredients = ingredients.Where(item => item.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            ingredients = ingredients.Where(item => item.Code.Contains(search) || item.Name.Contains(search));
        }

        var totalCount = await ingredients.CountAsync(cancellationToken);
        var entities = await ingredients
            .Include(item => item.Conversions)
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var units = await GetUnitsAsync(entities, cancellationToken);
        var usage = await GetIngredientUsageCountsAsync(entities.Select(item => item.Id), cancellationToken);
        return new PagedIngredientsDto(
            entities.Select(item => MapIngredient(item, units, usage.GetValueOrDefault(item.Id) ?? IngredientUsage.Empty)).ToList(),
            totalCount,
            query.Page,
            query.PageSize,
            TotalPages(totalCount, query.PageSize),
            query.Page * query.PageSize < totalCount,
            query.Page > 1);
    }

    public async Task<IngredientDto> GetIngredientAsync(int id, CancellationToken cancellationToken)
    {
        var ingredient = await _dbContext.Ingredients.AsNoTracking()
            .Include(item => item.Conversions)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ingredient), id);
        var units = await GetUnitsAsync([ingredient], cancellationToken);
        var usage = await GetIngredientUsageCountsAsync([ingredient.Id], cancellationToken);
        return MapIngredient(ingredient, units, usage.GetValueOrDefault(ingredient.Id) ?? IngredientUsage.Empty);
    }

    public async Task<int> CreateIngredientAsync(
        SaveIngredientRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        await EnsureIngredientCodeAvailableAsync(code, null, cancellationToken);
        var conversions = request.Conversions ?? [];
        await ValidateIngredientReferencesAsync(request.BaseUnitId, conversions, null, cancellationToken);

        var now = UtcNow();
        var ingredient = new Ingredient(
            code,
            request.Name,
            request.BaseUnitId,
            request.Note,
            request.IsActive,
            now);
        ingredient.ReplaceConversions(ToDefinitions(conversions), now);
        _dbContext.Ingredients.Add(ingredient);
        await SaveCatalogAsync("Ingredient code or conversion values already exist.", cancellationToken);
        return ingredient.Id;
    }

    public async Task UpdateIngredientAsync(
        int id,
        SaveIngredientRequest request,
        CancellationToken cancellationToken)
    {
        var ingredient = await _dbContext.Ingredients
            .Include(item => item.Conversions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ingredient), id);
        var code = NormalizeCode(request.Code);
        await EnsureIngredientCodeAvailableAsync(code, id, cancellationToken);
        var conversions = request.Conversions ?? [];
        await ValidateIngredientReferencesAsync(request.BaseUnitId, conversions, ingredient, cancellationToken);

        if (ingredient.BaseUnitId != request.BaseUnitId)
        {
            var inUse = await _dbContext.ProductIngredients.AsNoTracking()
                    .AnyAsync(item => item.IngredientId == id, cancellationToken)
                || await _dbContext.OrderItemIngredientSnapshots.AsNoTracking()
                    .AnyAsync(item => item.IngredientId == id, cancellationToken);
            if (inUse)
            {
                throw new ConflictException(
                    "The base unit cannot be changed after the ingredient is used. Create a new ingredient or migrate existing data explicitly.");
            }
        }

        var now = UtcNow();
        ingredient.Update(
            code,
            request.Name,
            request.BaseUnitId,
            request.Note,
            request.IsActive,
            now);
        ingredient.ReplaceConversions(ToDefinitions(conversions), now);
        await SaveCatalogAsync("Ingredient code or conversion values already exist.", cancellationToken);
    }

    public async Task DeleteIngredientAsync(int id, CancellationToken cancellationToken)
    {
        var ingredient = await _dbContext.Ingredients
            .Include(item => item.Conversions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Ingredient), id);
        var inUse = await _dbContext.ProductIngredients.AsNoTracking()
                .AnyAsync(item => item.IngredientId == id, cancellationToken)
            || await _dbContext.OrderItemIngredientSnapshots.AsNoTracking()
                .AnyAsync(item => item.IngredientId == id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException(
                "Ingredient has recipe or order history and cannot be deleted. Deactivate it instead.");
        }

        _dbContext.IngredientConversions.RemoveRange(ingredient.Conversions);
        _dbContext.Ingredients.Remove(ingredient);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateIngredientReferencesAsync(
        int baseUnitId,
        IReadOnlyCollection<IngredientConversionInputDto> conversions,
        Ingredient? existingIngredient,
        CancellationToken cancellationToken)
    {
        var requestedIds = conversions.Select(item => item.UnitId).Append(baseUnitId).Distinct().ToArray();
        var units = await _dbContext.MeasurementUnits.AsNoTracking()
            .Where(item => requestedIds.Contains(item.Id))
            .Select(item => new { item.Id, item.IsActive })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var errors = new Dictionary<string, string[]>();
        var baseUnitIsRetained = existingIngredient?.BaseUnitId == baseUnitId;
        if (baseUnitId <= 0
            || !units.TryGetValue(baseUnitId, out var baseUnit)
            || (!baseUnit.IsActive && !baseUnitIsRetained))
        {
            errors[nameof(SaveIngredientRequest.BaseUnitId)] =
                ["Base unit must be active unless it is the unchanged unit of this ingredient."];
        }
        var existingConversions = existingIngredient?.Conversions
            .ToDictionary(item => item.Id, item => item.UnitId)
            ?? new Dictionary<int, int>();
        var invalidConversionUnits = conversions
            .Where(item => item.UnitId == baseUnitId
                || !units.TryGetValue(item.UnitId, out var unit)
                || (!unit.IsActive
                    && (!item.Id.HasValue
                        || !existingConversions.TryGetValue(item.Id.Value, out var existingUnitId)
                        || existingUnitId != item.UnitId)))
            .Select(item => item.UnitId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        if (invalidConversionUnits.Length > 0)
        {
            errors[nameof(SaveIngredientRequest.Conversions)] =
                [$"Conversion units must be active and different from the base unit. Invalid ids: {string.Join(", ", invalidConversionUnits)}"];
        }
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private async Task<Dictionary<int, MeasurementUnit>> GetUnitsAsync(
        IEnumerable<Ingredient> ingredients,
        CancellationToken cancellationToken)
    {
        var ids = ingredients
            .SelectMany(item => item.Conversions.Select(conversion => conversion.UnitId).Append(item.BaseUnitId))
            .Distinct()
            .ToArray();
        return await _dbContext.MeasurementUnits.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
    }

    private static IngredientDto MapIngredient(
        Ingredient ingredient,
        IReadOnlyDictionary<int, MeasurementUnit> units,
        IngredientUsage usage)
    {
        if (!units.TryGetValue(ingredient.BaseUnitId, out var baseUnit))
            throw new InvalidOperationException($"Ingredient {ingredient.Id} references missing base unit {ingredient.BaseUnitId}.");
        return new IngredientDto(
            ingredient.Id,
            ingredient.Code,
            ingredient.Name,
            MapUnit(baseUnit),
            ingredient.Note,
            ingredient.IsActive,
            ingredient.Conversions
                .OrderBy(item => item.SortOrder)
                .ThenByDescending(item => item.FactorToBase)
                .Select(item => new IngredientConversionDto(
                    item.Id,
                    item.Code,
                    item.Name,
                    MapUnit(units[item.UnitId]),
                    item.FactorToBase,
                    item.SortOrder,
                    item.IsActive))
                .ToList(),
            AsOffset(ingredient.CreatedAt),
            AsOffset(ingredient.UpdatedAt),
            usage.ProductCount,
            usage.SnapshotCount,
            usage.ProductCount == 0 && usage.SnapshotCount == 0 && usage.InventoryUnitCount == 0,
            usage.InventoryUnitCount);
    }

    private static MeasurementUnitDto MapUnit(MeasurementUnit item) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Symbol,
        item.AllowsFractional,
        item.IsActive,
        AsOffset(item.CreatedAt),
        AsOffset(item.UpdatedAt));

    private static MeasurementUnitDto MapUnitWithUsage(MeasurementUnit item, int referenceCount) => new(
        item.Id,
        item.Code,
        item.Name,
        item.Symbol,
        item.AllowsFractional,
        item.IsActive,
        AsOffset(item.CreatedAt),
        AsOffset(item.UpdatedAt),
        referenceCount,
        referenceCount == 0);

    private async Task<Dictionary<int, int>> GetUnitUsageCountsAsync(
        IEnumerable<int> unitIds,
        CancellationToken cancellationToken)
    {
        var ids = unitIds.Distinct().ToArray();
        var baseCounts = await _dbContext.Ingredients.AsNoTracking()
            .Where(item => ids.Contains(item.BaseUnitId))
            .GroupBy(item => item.BaseUnitId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
        var conversionCounts = await _dbContext.IngredientConversions.AsNoTracking()
            .Where(item => ids.Contains(item.UnitId))
            .GroupBy(item => item.UnitId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
        return ids.ToDictionary(
            id => id,
            id => baseCounts.GetValueOrDefault(id) + conversionCounts.GetValueOrDefault(id));
    }

    private async Task<Dictionary<int, IngredientUsage>> GetIngredientUsageCountsAsync(
        IEnumerable<int> ingredientIds,
        CancellationToken cancellationToken)
    {
        var ids = ingredientIds.Distinct().ToArray();
        var productCounts = await _dbContext.ProductIngredients.AsNoTracking()
            .Where(item => ids.Contains(item.IngredientId))
            .GroupBy(item => item.IngredientId)
            .Select(group => new { Id = group.Key, Count = group.Select(item => item.ProductId).Distinct().Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
        var snapshotCounts = await _dbContext.OrderItemIngredientSnapshots.AsNoTracking()
            .Where(item => item.IngredientId.HasValue && ids.Contains(item.IngredientId.Value))
            .GroupBy(item => item.IngredientId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Id, item => item.Count, cancellationToken);
        return ids.ToDictionary(
            id => id,
            id => new IngredientUsage(
                productCounts.GetValueOrDefault(id),
                snapshotCounts.GetValueOrDefault(id),
                0));
    }

    private static IEnumerable<IngredientConversionDefinition> ToDefinitions(
        IEnumerable<IngredientConversionInputDto> conversions) =>
        conversions.Select(item => new IngredientConversionDefinition(
            item.Id,
            item.Code,
            item.Name,
            item.UnitId,
            item.FactorToBase,
            item.SortOrder,
            item.IsActive));

    private async Task EnsureUnitCodeAvailableAsync(string code, int? exceptId, CancellationToken cancellationToken)
    {
        if (await _dbContext.MeasurementUnits.AsNoTracking()
            .AnyAsync(item => item.Code == code && (!exceptId.HasValue || item.Id != exceptId), cancellationToken))
            throw new ConflictException($"Measurement unit code '{code}' already exists.");
    }

    private async Task EnsureIngredientCodeAvailableAsync(string code, int? exceptId, CancellationToken cancellationToken)
    {
        if (await _dbContext.Ingredients.AsNoTracking()
            .AnyAsync(item => item.Code == code && (!exceptId.HasValue || item.Id != exceptId), cancellationToken))
            throw new ConflictException($"Ingredient code '{code}' already exists.");
    }

    private async Task SaveCatalogAsync(string conflictMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException(conflictMessage);
        }
    }

    private static void ValidatePage(CatalogListQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Page < 1)
            errors[nameof(query.Page)] = ["Page must be at least 1."];
        if (query.PageSize is < 1 or > 100)
            errors[nameof(query.PageSize)] = ["Page size must be between 1 and 100."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static int TotalPages(int count, int pageSize) =>
        count == 0 ? 0 : (int)Math.Ceiling(count / (double)pageSize);

    private static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed record IngredientUsage(int ProductCount, int SnapshotCount, int InventoryUnitCount)
    {
        public static readonly IngredientUsage Empty = new(0, 0, 0);
    }
}
