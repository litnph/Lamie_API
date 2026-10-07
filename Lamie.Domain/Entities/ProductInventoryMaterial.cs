using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed record ProductInventoryMaterialDefinition(
    Guid StockUnitId,
    decimal Quantity,
    int SortOrder);

public sealed class ProductInventoryMaterial
{
    private ProductInventoryMaterial()
    {
    }

    internal ProductInventoryMaterial(ProductInventoryMaterialDefinition definition)
    {
        Apply(definition);
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public Guid StockUnitId { get; private set; }
    public decimal Quantity { get; private set; }
    public int SortOrder { get; private set; }

    internal void Update(ProductInventoryMaterialDefinition definition) => Apply(definition);

    private void Apply(ProductInventoryMaterialDefinition definition)
    {
        if (definition.StockUnitId == Guid.Empty)
            throw new DomainException("Product inventory material stock unit is required.");
        var quantity = DecimalQuantity.Normalize(definition.Quantity);
        if (quantity <= 0)
            throw new DomainException("Product inventory material quantity must be greater than zero.");
        if (definition.SortOrder < 0)
            throw new DomainException("Product inventory material sort order cannot be negative.");

        StockUnitId = definition.StockUnitId;
        Quantity = quantity;
        SortOrder = definition.SortOrder;
    }
}
