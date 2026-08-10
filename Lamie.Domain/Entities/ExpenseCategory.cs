using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class ExpenseCategory
{
    private ExpenseCategory()
    {
    }

    public ExpenseCategory(
        string name,
        string? description,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        Update(name, description, sortOrder, isActive, nowUtc);
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Update(
        string name,
        string? description,
        int sortOrder,
        bool isActive,
        DateTime nowUtc)
    {
        var normalizedName = NormalizeName(name);
        if (name.Trim().Length > 120)
            throw new DomainException("Expense category name cannot exceed 120 characters.");
        if (sortOrder < 0)
            throw new DomainException("Expense category sort order cannot be negative.");

        Name = name.Trim();
        NormalizedName = normalizedName;
        Description = NormalizeOptional(description, 500, "Expense category description");
        SortOrder = sortOrder;
        IsActive = isActive;
        UpdatedAt = nowUtc;
    }

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Expense category name is required.");

        return name.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}
