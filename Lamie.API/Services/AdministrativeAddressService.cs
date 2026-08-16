using Lamie.Application.Addresses;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public interface IAdministrativeAddressService
{
    Task<IReadOnlyList<AdministrativeUnitDto>> GetProvincesAsync(AdministrativeScheme scheme, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdministrativeUnitDto>> GetChildrenAsync(string code, AdministrativeScheme scheme, string? query, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdministrativeUnitDto>> SearchAsync(string query, AdministrativeScheme? scheme, int? hierarchyLevel, string? parentCode, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdministrativeTransitionDto>> GetTransitionsAsync(string legacyCode, CancellationToken cancellationToken);
    Task<AddressResolutionDto> ResolveAsync(ResolveAddressRequest request, CancellationToken cancellationToken);
}

public sealed class AdministrativeAddressService : IAdministrativeAddressService
{
    private const int MaximumResultCount = 100;
    private const string ResolverUnitsCacheKey = "administrative-address-resolver-units-v1";
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;
    private readonly AdministrativeAddressResolutionOptions _resolutionOptions;

    public AdministrativeAddressService(
        AppDbContext dbContext,
        IMemoryCache memoryCache,
        IOptions<AdministrativeAddressResolutionOptions> resolutionOptions)
    {
        _dbContext = dbContext;
        _memoryCache = memoryCache;
        _resolutionOptions = resolutionOptions.Value;
    }

    public async Task<IReadOnlyList<AdministrativeUnitDto>> GetProvincesAsync(
        AdministrativeScheme scheme,
        CancellationToken cancellationToken)
    {
        ValidateScheme(scheme);
        var units = await _dbContext.AdministrativeUnits.AsNoTracking()
            .Where(unit => unit.Scheme == scheme && unit.HierarchyLevel == 1 && unit.IsActive)
            .OrderBy(unit => unit.SortOrder)
            .ThenBy(unit => unit.FullName)
            .ToListAsync(cancellationToken);
        return units.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<AdministrativeUnitDto>> GetChildrenAsync(
        string code,
        AdministrativeScheme scheme,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateScheme(scheme);
        ValidateLimit(limit);
        var parentCode = code.Trim();
        var parentExists = await _dbContext.AdministrativeUnits.AsNoTracking()
            .AnyAsync(unit => unit.Scheme == scheme && unit.Code == parentCode && unit.IsActive, cancellationToken);
        if (!parentExists)
            throw new NotFoundException(nameof(AdministrativeUnit), $"{scheme}:{parentCode}");

        var normalizedQuery = string.IsNullOrWhiteSpace(query)
            ? null
            : VietnameseTextNormalizer.Search(query);
        var unitsQuery = _dbContext.AdministrativeUnits.AsNoTracking()
            .Where(unit => unit.Scheme == scheme && unit.ParentCode == parentCode && unit.IsActive);
        if (normalizedQuery is not null)
            unitsQuery = unitsQuery.Where(unit => unit.Code.Contains(normalizedQuery) || unit.NormalizedName.Contains(normalizedQuery));
        var units = await unitsQuery
            .OrderBy(unit => unit.SortOrder)
            .ThenBy(unit => unit.FullName)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return units.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<AdministrativeUnitDto>> SearchAsync(
        string query,
        AdministrativeScheme? scheme,
        int? hierarchyLevel,
        string? parentCode,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw Validation("query", "Search query is required.");
        if (scheme.HasValue)
            ValidateScheme(scheme.Value);
        if (hierarchyLevel is < 1 or > 3)
            throw Validation("hierarchyLevel", "Hierarchy level must be between 1 and 3.");
        ValidateLimit(limit);

        var normalized = VietnameseTextNormalizer.Search(query);
        var trimmedParent = string.IsNullOrWhiteSpace(parentCode) ? null : parentCode.Trim();
        var unitsQuery = _dbContext.AdministrativeUnits.AsNoTracking().Where(unit => unit.IsActive);
        if (scheme.HasValue)
            unitsQuery = unitsQuery.Where(unit => unit.Scheme == scheme.Value);
        if (hierarchyLevel.HasValue)
            unitsQuery = unitsQuery.Where(unit => unit.HierarchyLevel == hierarchyLevel.Value);
        if (trimmedParent is not null)
            unitsQuery = unitsQuery.Where(unit => unit.ParentCode == trimmedParent);

        var units = await unitsQuery
            .Where(unit => unit.Code.Contains(normalized) || unit.NormalizedName.Contains(normalized))
            .OrderBy(unit => unit.NormalizedName == normalized ? 0 : 1)
            .ThenBy(unit => unit.HierarchyLevel)
            .ThenBy(unit => unit.SortOrder)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return units.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<AdministrativeTransitionDto>> GetTransitionsAsync(
        string legacyCode,
        CancellationToken cancellationToken)
    {
        var normalizedCode = legacyCode.Trim();
        var transitions = await _dbContext.AdministrativeUnitTransitions.AsNoTracking()
            .Where(transition => transition.LegacyUnitCode == normalizedCode)
            .OrderBy(transition => transition.TransitionType)
            .ThenBy(transition => transition.CurrentUnitCode)
            .ToListAsync(cancellationToken);
        if (transitions.Count == 0)
            return [];

        var currentCodes = transitions.Select(item => item.CurrentUnitCode).Distinct().ToArray();
        var currentUnits = await _dbContext.AdministrativeUnits.AsNoTracking()
            .Where(unit => unit.Scheme == AdministrativeScheme.Current && currentCodes.Contains(unit.Code))
            .ToDictionaryAsync(unit => unit.Code, cancellationToken);
        var provinceCodes = currentUnits.Values.Select(unit => unit.ParentCode).Where(code => code is not null).Distinct().ToArray();
        var provinces = await _dbContext.AdministrativeUnits.AsNoTracking()
            .Where(unit => unit.Scheme == AdministrativeScheme.Current && provinceCodes.Contains(unit.Code))
            .ToDictionaryAsync(unit => unit.Code, unit => unit.FullName, cancellationToken);
        var ambiguous = transitions.Count != 1;

        return transitions.Where(item => currentUnits.ContainsKey(item.CurrentUnitCode)).Select(item =>
        {
            var unit = currentUnits[item.CurrentUnitCode];
            var suggestionOnly = ambiguous || item.TransitionType is AdministrativeTransitionType.Partial
                or AdministrativeTransitionType.SplitInto or AdministrativeTransitionType.Other;
            return new AdministrativeTransitionDto(
                item.LegacyUnitCode,
                item.CurrentUnitCode,
                item.TransitionType,
                unit.FullName,
                unit.ParentCode is not null && provinces.TryGetValue(unit.ParentCode, out var provinceName) ? provinceName : null,
                item.SourceDocument,
                item.SourceReference,
                item.Note,
                suggestionOnly);
        }).ToList();
    }

    public async Task<AddressResolutionDto> ResolveAsync(
        ResolveAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw Validation("text", "Address text is required.");
        if (request.PreferredScheme.HasValue)
            ValidateScheme(request.PreferredScheme.Value);
        if (VietnameseTextNormalizer.Display(request.Text).Length > 1500)
            throw Validation("text", "Address text cannot exceed 1500 characters.");
        var units = await _memoryCache.GetOrCreateAsync(ResolverUnitsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return await _dbContext.AdministrativeUnits.AsNoTracking()
                .Where(unit => unit.IsActive)
                .ToListAsync(cancellationToken);
        }) ?? [];
        return AdministrativeAddressResolver.Resolve(request, units, _resolutionOptions);
    }

    private static AdministrativeUnitDto ToDto(AdministrativeUnit unit) => new(
        unit.Code,
        unit.Name,
        unit.FullName,
        unit.Scheme,
        unit.UnitType,
        unit.HierarchyLevel,
        unit.ParentCode,
        unit.IsActive,
        unit.SortOrder);

    private static void ValidateScheme(AdministrativeScheme scheme)
    {
        if (!Enum.IsDefined(scheme))
            throw Validation("scheme", "Administrative scheme is invalid.");
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > MaximumResultCount)
            throw Validation("limit", $"Limit must be between 1 and {MaximumResultCount}.");
    }

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
