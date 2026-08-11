using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed record OrderItemSnapshot(
    int? ProductId,
    string? ProductSku,
    string ProductName,
    string? ThumbnailUrl,
    decimal UnitPrice,
    int Quantity,
    decimal DiscountAmount = 0,
    string? Note = null,
    bool HasCard = false,
    string? CardMessage = null,
    bool HasBanner = false,
    string? BannerMessage = null);

public sealed record OrderItemUpdate(Guid? Id, OrderItemSnapshot Snapshot);

public sealed class OrderItem
{
    private OrderItem()
    {
    }

    internal OrderItem(OrderItemSnapshot snapshot)
    {
        Id = Guid.NewGuid();
        Apply(Normalize(snapshot));
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public int? ProductId { get; private set; }
    public string? ProductSku { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string? ThumbnailUrl { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal LineTotal { get; private set; }
    public string? Note { get; private set; }
    public bool HasCard { get; private set; }
    public string? CardMessage { get; private set; }
    public bool HasBanner { get; private set; }
    public string? BannerMessage { get; private set; }

    internal void Update(OrderItemSnapshot snapshot) => Apply(Normalize(snapshot));

    internal static void Validate(OrderItemSnapshot snapshot) => _ = Normalize(snapshot);

    private void Apply(NormalizedSnapshot snapshot)
    {
        ProductId = snapshot.ProductId;
        ProductSku = snapshot.ProductSku;
        ProductName = snapshot.ProductName;
        ThumbnailUrl = snapshot.ThumbnailUrl;
        UnitPrice = snapshot.UnitPrice;
        Quantity = snapshot.Quantity;
        DiscountAmount = snapshot.DiscountAmount;
        LineTotal = snapshot.LineTotal;
        Note = snapshot.Note;
        HasCard = snapshot.HasCard;
        CardMessage = snapshot.CardMessage;
        HasBanner = snapshot.HasBanner;
        BannerMessage = snapshot.BannerMessage;
    }

    private static NormalizedSnapshot Normalize(OrderItemSnapshot snapshot)
    {
        if (snapshot.ProductId is <= 0)
            throw new DomainException("Product id must be greater than zero when supplied.");
        if (string.IsNullOrWhiteSpace(snapshot.ProductName))
            throw new DomainException("Order item product name is required.");
        if (snapshot.UnitPrice < 0)
            throw new DomainException("Order item unit price cannot be negative.");
        if (snapshot.Quantity <= 0)
            throw new DomainException("Order item quantity must be greater than zero.");

        var productName = snapshot.ProductName.Trim();
        if (productName.Length > 300)
            throw new DomainException("Order item product name cannot exceed 300 characters.");

        var unitPrice = decimal.Round(snapshot.UnitPrice, 2, MidpointRounding.AwayFromZero);
        var discountAmount = decimal.Round(snapshot.DiscountAmount, 2, MidpointRounding.AwayFromZero);
        var gross = decimal.Round(unitPrice * snapshot.Quantity, 2, MidpointRounding.AwayFromZero);
        if (discountAmount < 0 || discountAmount > gross)
            throw new DomainException("Order item discount must be between zero and the gross line amount.");

        var cardMessage = NormalizeOptional(snapshot.CardMessage, 1000, "Card message");
        var bannerMessage = NormalizeOptional(snapshot.BannerMessage, 1000, "Banner message");
        if (snapshot.HasCard && cardMessage is null)
            throw new DomainException("Card message is required when the order item has a card.");
        if (snapshot.HasBanner && bannerMessage is null)
            throw new DomainException("Banner message is required when the order item has a banner.");

        return new NormalizedSnapshot(
            snapshot.ProductId,
            NormalizeOptional(snapshot.ProductSku, 100, "Product SKU"),
            productName,
            NormalizeOptional(snapshot.ThumbnailUrl, 2048, "Thumbnail URL"),
            unitPrice,
            snapshot.Quantity,
            discountAmount,
            gross - discountAmount,
            NormalizeOptional(snapshot.Note, 1000, "Order item note"),
            snapshot.HasCard,
            snapshot.HasCard ? cardMessage : null,
            snapshot.HasBanner,
            snapshot.HasBanner ? bannerMessage : null);
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > maxLength)
            throw new DomainException($"{field} cannot exceed {maxLength} characters.");
        return normalized;
    }

    private sealed record NormalizedSnapshot(
        int? ProductId,
        string? ProductSku,
        string ProductName,
        string? ThumbnailUrl,
        decimal UnitPrice,
        int Quantity,
        decimal DiscountAmount,
        decimal LineTotal,
        string? Note,
        bool HasCard,
        string? CardMessage,
        bool HasBanner,
        string? BannerMessage);
}
