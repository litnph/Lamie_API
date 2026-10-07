using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum ProductWatermarkSource
{
    Sku = 1,
    ShopLogo = 2,
    UploadedImage = 3
}

public sealed class ProductCatalogSettings
{
    public const int SingletonId = 1;
    public const decimal DefaultPriceDeviationPercent = 20m;
    public const decimal MaximumPriceDeviationPercent = 100m;
    public const bool DefaultWatermarkEnabled = true;
    public const ProductWatermarkSource DefaultWatermarkSource = ProductWatermarkSource.Sku;

    private ProductCatalogSettings()
    {
    }

    public ProductCatalogSettings(decimal priceDeviationPercent, DateTime updatedAt)
    {
        Id = SingletonId;
        PriceDeviationPercent = NormalizePriceDeviation(priceDeviationPercent);
        WatermarkEnabled = DefaultWatermarkEnabled;
        WatermarkSource = DefaultWatermarkSource;
        UpdatedAt = updatedAt;
    }

    public int Id { get; private set; }
    public decimal PriceDeviationPercent { get; private set; }
    public string? ShopLogoUrl { get; private set; }
    public bool WatermarkEnabled { get; private set; }
    public ProductWatermarkSource WatermarkSource { get; private set; }
    public string? WatermarkImageUrl { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdatePriceDeviation(decimal priceDeviationPercent, DateTime updatedAt)
    {
        PriceDeviationPercent = NormalizePriceDeviation(priceDeviationPercent);
        UpdatedAt = updatedAt;
    }

    public void UpdateBranding(
        decimal priceDeviationPercent,
        bool watermarkEnabled,
        ProductWatermarkSource watermarkSource,
        string? shopLogoUrl,
        string? watermarkImageUrl,
        DateTime updatedAt)
    {
        if (!Enum.IsDefined(watermarkSource))
            throw new DomainException("Watermark source is invalid.");

        PriceDeviationPercent = NormalizePriceDeviation(priceDeviationPercent);
        WatermarkEnabled = watermarkEnabled;
        WatermarkSource = watermarkSource;
        ShopLogoUrl = OptionalUrl(shopLogoUrl, "Shop logo URL");
        WatermarkImageUrl = OptionalUrl(watermarkImageUrl, "Watermark image URL");
        UpdatedAt = updatedAt;
    }

    private static decimal NormalizePriceDeviation(decimal value)
    {
        if (value < 0 || value > MaximumPriceDeviationPercent)
            throw new DomainException("Price deviation percent must be between 0 and 100");
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string? OptionalUrl(string? value, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > 2048)
            throw new DomainException($"{field} cannot exceed 2048 characters.");
        return normalized;
    }
}
