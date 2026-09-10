using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed record IngredientRecipeSnapshot(
    int? IngredientId,
    string IngredientCode,
    string IngredientName,
    string BaseUnitCode,
    string BaseUnitName,
    string? BaseUnitSymbol,
    decimal PerProductBaseQuantity,
    string? Note,
    int SortOrder);

public sealed class OrderItemIngredientSnapshot
{
    private OrderItemIngredientSnapshot()
    {
    }

    internal OrderItemIngredientSnapshot(
        IngredientRecipeSnapshot recipe,
        int productQuantity,
        DateTime capturedAtUtc)
    {
        Validate(recipe, productQuantity, capturedAtUtc);
        Id = Guid.NewGuid();
        IngredientId = recipe.IngredientId;
        IngredientCode = Limit(recipe.IngredientCode, 80, "Ingredient snapshot code", required: true)!;
        IngredientName = Limit(recipe.IngredientName, 200, "Ingredient snapshot name", required: true)!;
        BaseUnitCode = Limit(recipe.BaseUnitCode, 50, "Ingredient snapshot base unit code", required: true)!;
        BaseUnitName = Limit(recipe.BaseUnitName, 120, "Ingredient snapshot base unit name", required: true)!;
        BaseUnitSymbol = Limit(recipe.BaseUnitSymbol, 30, "Ingredient snapshot base unit symbol", required: false);
        PerProductBaseQuantity = DecimalQuantity.Normalize(recipe.PerProductBaseQuantity);
        ProductQuantity = productQuantity;
        TotalBaseQuantity = DecimalQuantity.Normalize(PerProductBaseQuantity * productQuantity);
        Note = Limit(recipe.Note, 1000, "Ingredient snapshot note", required: false);
        SortOrder = recipe.SortOrder;
        CapturedAtUtc = capturedAtUtc;
    }

    internal static void Validate(
        IngredientRecipeSnapshot recipe,
        int productQuantity,
        DateTime capturedAtUtc)
    {
        if (capturedAtUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Ingredient snapshot time must be UTC.");
        if (recipe.IngredientId is <= 0)
            throw new DomainException("Ingredient snapshot id must be positive when supplied.");
        if (string.IsNullOrWhiteSpace(recipe.IngredientName))
            throw new DomainException("Ingredient snapshot name is required.");
        if (string.IsNullOrWhiteSpace(recipe.BaseUnitName))
            throw new DomainException("Ingredient snapshot base unit name is required.");
        if (recipe.PerProductBaseQuantity <= 0)
            throw new DomainException("Ingredient snapshot recipe quantity must be greater than zero.");
        if (productQuantity <= 0)
            throw new DomainException("Ingredient snapshot product quantity must be greater than zero.");
        if (recipe.SortOrder < 0)
            throw new DomainException("Ingredient snapshot sort order cannot be negative.");
        _ = Limit(recipe.IngredientCode, 80, "Ingredient snapshot code", required: true);
        _ = Limit(recipe.IngredientName, 200, "Ingredient snapshot name", required: true);
        _ = Limit(recipe.BaseUnitCode, 50, "Ingredient snapshot base unit code", required: true);
        _ = Limit(recipe.BaseUnitName, 120, "Ingredient snapshot base unit name", required: true);
        _ = Limit(recipe.BaseUnitSymbol, 30, "Ingredient snapshot base unit symbol", required: false);
        _ = Limit(recipe.Note, 1000, "Ingredient snapshot note", required: false);
    }

    public Guid Id { get; private set; }
    public Guid OrderItemId { get; private set; }
    public int? IngredientId { get; private set; }
    public string IngredientCode { get; private set; } = string.Empty;
    public string IngredientName { get; private set; } = string.Empty;
    public string BaseUnitCode { get; private set; } = string.Empty;
    public string BaseUnitName { get; private set; } = string.Empty;
    public string? BaseUnitSymbol { get; private set; }
    public decimal PerProductBaseQuantity { get; private set; }
    public int ProductQuantity { get; private set; }
    public decimal TotalBaseQuantity { get; private set; }
    public string? Note { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CapturedAtUtc { get; private set; }

    private static string? Limit(string? value, int maxLength, string field, bool required)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (required && normalized is null)
            throw new DomainException($"{field} is required.");
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }
}
