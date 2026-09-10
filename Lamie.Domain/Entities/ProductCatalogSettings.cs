using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class ProductCatalogSettings
{
    public const int SingletonId = 1;
    public const decimal DefaultPriceDeviationPercent = 20m;
    public const decimal MaximumPriceDeviationPercent = 100m;

    private ProductCatalogSettings()
    {
    }

    public ProductCatalogSettings(decimal priceDeviationPercent, DateTime updatedAt)
    {
        Id = SingletonId;
        UpdatePriceDeviation(priceDeviationPercent, updatedAt);
    }

    public int Id { get; private set; }
    public decimal PriceDeviationPercent { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdatePriceDeviation(decimal priceDeviationPercent, DateTime updatedAt)
    {
        if (priceDeviationPercent < 0 || priceDeviationPercent > MaximumPriceDeviationPercent)
            throw new DomainException("Price deviation percent must be between 0 and 100");

        PriceDeviationPercent = decimal.Round(priceDeviationPercent, 2, MidpointRounding.AwayFromZero);
        UpdatedAt = updatedAt;
    }
}
