using System.Text.RegularExpressions;
using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class MeasurementUnit
{
    private static readonly Regex CodePattern = new("^[A-Z0-9][A-Z0-9_-]{0,49}$", RegexOptions.Compiled);

    private MeasurementUnit()
    {
    }

    public MeasurementUnit(
        string code,
        string name,
        string? symbol,
        bool allowsFractional,
        bool isActive,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        Id = 0;
        Apply(code, name, symbol, allowsFractional, isActive);
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Symbol { get; private set; }
    public bool AllowsFractional { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(
        string code,
        string name,
        string? symbol,
        bool allowsFractional,
        bool isActive,
        DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        Apply(code, name, symbol, allowsFractional, isActive);
        UpdatedAt = nowUtc;
    }

    private void Apply(string code, string name, string? symbol, bool allowsFractional, bool isActive)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(normalizedCode))
            throw new DomainException("Measurement unit code must contain only uppercase letters, digits, underscores, or hyphens and be at most 50 characters.");

        var normalizedName = Required(name, 120, "Measurement unit name");
        var normalizedSymbol = Optional(symbol, 30, "Measurement unit symbol");

        Code = normalizedCode;
        Name = normalizedName;
        Symbol = normalizedSymbol;
        AllowsFractional = allowsFractional;
        IsActive = isActive;
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
