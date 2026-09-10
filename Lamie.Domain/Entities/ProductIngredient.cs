using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed record ProductIngredientDefinition(
    int IngredientId,
    decimal BaseQuantity,
    string? Note,
    int SortOrder);

public sealed class ProductIngredient
{
    private ProductIngredient()
    {
    }

    internal ProductIngredient(ProductIngredientDefinition definition)
    {
        Apply(definition);
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int IngredientId { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public string? Note { get; private set; }
    public int SortOrder { get; private set; }

    internal void Update(ProductIngredientDefinition definition) => Apply(definition);

    private void Apply(ProductIngredientDefinition definition)
    {
        if (definition.IngredientId <= 0)
            throw new DomainException("Product ingredient is required.");
        if (definition.BaseQuantity <= 0)
            throw new DomainException("Product ingredient base quantity must be greater than zero.");
        if (definition.SortOrder < 0)
            throw new DomainException("Product ingredient sort order cannot be negative.");
        var note = string.IsNullOrWhiteSpace(definition.Note) ? null : definition.Note.Trim();
        if (note?.Length > 1000)
            throw new DomainException("Product ingredient note cannot exceed 1000 characters.");

        IngredientId = definition.IngredientId;
        BaseQuantity = DecimalQuantity.Normalize(definition.BaseQuantity);
        Note = note;
        SortOrder = definition.SortOrder;
    }
}

internal static class DecimalQuantity
{
    public static decimal Normalize(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
