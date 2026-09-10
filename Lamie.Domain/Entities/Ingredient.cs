using System.Text.RegularExpressions;
using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed record IngredientConversionDefinition(
    int? Id,
    string Code,
    string Name,
    int UnitId,
    decimal FactorToBase,
    int SortOrder,
    bool IsActive);

public sealed class Ingredient
{
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9_-]{0,79}$", RegexOptions.Compiled);
    private readonly List<IngredientConversion> _conversions = [];

    private Ingredient()
    {
    }

    public Ingredient(
        string code,
        string name,
        int baseUnitId,
        string? note,
        bool isActive,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        Apply(code, name, baseUnitId, note, isActive);
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int BaseUnitId { get; private set; }
    public string? Note { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public IReadOnlyCollection<IngredientConversion> Conversions => _conversions;

    public void Update(
        string code,
        string name,
        int baseUnitId,
        string? note,
        bool isActive,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        Apply(code, name, baseUnitId, note, isActive);
        UpdatedAt = nowUtc;
    }

    public void ReplaceConversions(
        IEnumerable<IngredientConversionDefinition> definitions,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        var requested = definitions.ToList();
        ValidateConversions(requested);

        var existingById = _conversions.ToDictionary(item => item.Id);
        var requestedIds = requested.Where(item => item.Id.HasValue).Select(item => item.Id!.Value).ToHashSet();
        if (requestedIds.Any(id => id <= 0 || !existingById.ContainsKey(id)))
            throw new DomainException("An ingredient conversion id does not belong to this ingredient.");
        if (requestedIds.Count != requested.Count(item => item.Id.HasValue))
            throw new DomainException("Ingredient conversion ids must be unique.");

        foreach (var omitted in _conversions.Where(item => !requestedIds.Contains(item.Id)))
        {
            omitted.Update(
                omitted.Code,
                omitted.Name,
                omitted.UnitId,
                omitted.FactorToBase,
                omitted.SortOrder,
                false,
                nowUtc);
        }
        foreach (var definition in requested)
        {
            if (definition.Id.HasValue)
            {
                existingById[definition.Id.Value].Update(
                    definition.Code,
                    definition.Name,
                    definition.UnitId,
                    definition.FactorToBase,
                    definition.SortOrder,
                    definition.IsActive,
                    nowUtc);
            }
            else
            {
                _conversions.Add(new IngredientConversion(
                    definition.Code,
                    definition.Name,
                    definition.UnitId,
                    definition.FactorToBase,
                    definition.SortOrder,
                    definition.IsActive,
                    nowUtc));
            }
        }

        UpdatedAt = nowUtc;
    }

    private void Apply(string code, string name, int baseUnitId, string? note, bool isActive)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(normalizedCode))
            throw new DomainException("Ingredient code must contain only uppercase letters, digits, underscores, or hyphens and be at most 80 characters.");
        if (baseUnitId <= 0)
            throw new DomainException("Ingredient base unit is required.");

        Code = normalizedCode;
        Name = Required(name, 200, "Ingredient name");
        BaseUnitId = baseUnitId;
        Note = Optional(note, 2000, "Ingredient note");
        IsActive = isActive;
    }

    private static void ValidateConversions(IReadOnlyCollection<IngredientConversionDefinition> definitions)
    {
        if (definitions.GroupBy(item => item.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new DomainException("Ingredient conversion codes must be unique.");
        if (definitions.GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new DomainException("Ingredient conversion names must be unique.");
        if (definitions.GroupBy(item => item.FactorToBase).Any(group => group.Count() > 1))
            throw new DomainException("Ingredient conversion factors must be unique.");
    }

    private static string Required(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{field} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException("Current time must be UTC.");
    }
}

public sealed class IngredientConversion
{
    private IngredientConversion()
    {
    }

    internal IngredientConversion(
        string code,
        string name,
        int unitId,
        decimal factorToBase,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        Apply(code, name, unitId, factorToBase, sortOrder, isActive);
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public int Id { get; private set; }
    public int IngredientId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int UnitId { get; private set; }
    public decimal FactorToBase { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    internal void Update(
        string code,
        string name,
        int unitId,
        decimal factorToBase,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        Apply(code, name, unitId, factorToBase, sortOrder, isActive);
        UpdatedAt = nowUtc;
    }

    private void Apply(string code, string name, int unitId, decimal factorToBase, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 80)
            throw new DomainException("Ingredient conversion code is required and cannot exceed 80 characters.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new DomainException("Ingredient conversion name is required and cannot exceed 160 characters.");
        if (unitId <= 0)
            throw new DomainException("Ingredient conversion unit is required.");
        var normalizedFactor = DecimalQuantity.Normalize(factorToBase);
        if (normalizedFactor <= 1)
            throw new DomainException("Ingredient conversion factor must be greater than 1.");
        if (sortOrder < 0)
            throw new DomainException("Ingredient conversion sort order cannot be negative.");

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        UnitId = unitId;
        FactorToBase = normalizedFactor;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
