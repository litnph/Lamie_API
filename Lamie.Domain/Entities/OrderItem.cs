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
    string? BannerMessage = null,
    IReadOnlyList<IngredientRecipeSnapshot>? IngredientRecipe = null,
    DateTime? IngredientSnapshotCapturedAtUtc = null,
    int? ProductTypeId = null,
    string? ProductTypeName = null);

public sealed record OrderItemUpdate(Guid? Id, OrderItemSnapshot Snapshot);

public sealed class OrderItem
{
    private readonly List<OrderItemIngredientSnapshot> _ingredientSnapshots = [];

    private OrderItem()
    {
    }

    internal OrderItem(OrderItemSnapshot snapshot)
    {
        Id = Guid.NewGuid();
        var normalized = Normalize(snapshot);
        Apply(normalized);
        ReplaceIngredientSnapshots(normalized);
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public int? ProductId { get; private set; }
    public int? ProductTypeId { get; private set; }
    public string? ProductTypeName { get; private set; }
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
    public DateTime? IngredientSnapshotCapturedAtUtc { get; private set; }
    public IReadOnlyCollection<OrderItemIngredientSnapshot> IngredientSnapshots => _ingredientSnapshots;

    internal void Update(OrderItemSnapshot snapshot)
    {
        var normalized = Normalize(snapshot);
        var quantityChanged = Quantity != normalized.Quantity;
        Apply(normalized);
        if (normalized.IngredientRecipe is not null)
            ReplaceIngredientSnapshots(normalized);
        else if (quantityChanged && _ingredientSnapshots.Count > 0)
            RefreshIngredientSnapshotQuantities(normalized.Quantity);
    }

    internal static void Validate(OrderItemSnapshot snapshot) => _ = Normalize(snapshot);

    private void Apply(NormalizedSnapshot snapshot)
    {
        ProductId = snapshot.ProductId;
        ProductTypeId = snapshot.ProductTypeId;
        ProductTypeName = snapshot.ProductTypeName;
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

    private void ReplaceIngredientSnapshots(NormalizedSnapshot snapshot)
    {
        _ingredientSnapshots.Clear();
        IngredientSnapshotCapturedAtUtc = snapshot.IngredientSnapshotCapturedAtUtc;
        if (snapshot.IngredientRecipe is null || snapshot.IngredientSnapshotCapturedAtUtc is null)
            return;

        foreach (var recipe in snapshot.IngredientRecipe)
        {
            _ingredientSnapshots.Add(new OrderItemIngredientSnapshot(
                recipe,
                snapshot.Quantity,
                snapshot.IngredientSnapshotCapturedAtUtc.Value));
        }
    }

    private void RefreshIngredientSnapshotQuantities(int productQuantity)
    {
        DateTime? capturedAtUtc = IngredientSnapshotCapturedAtUtc.HasValue
            ? DateTime.SpecifyKind(IngredientSnapshotCapturedAtUtc.Value, DateTimeKind.Utc)
            : null;
        if (!capturedAtUtc.HasValue)
            return;

        var recipe = _ingredientSnapshots
            .OrderBy(snapshot => snapshot.SortOrder)
            .ThenBy(snapshot => snapshot.Id)
            .Select(snapshot => new IngredientRecipeSnapshot(
                snapshot.IngredientId,
                snapshot.IngredientCode,
                snapshot.IngredientName,
                snapshot.BaseUnitCode,
                snapshot.BaseUnitName,
                snapshot.BaseUnitSymbol,
                snapshot.PerProductBaseQuantity,
                snapshot.Note,
                snapshot.SortOrder))
            .ToArray();

        _ingredientSnapshots.Clear();
        foreach (var item in recipe)
            _ingredientSnapshots.Add(new OrderItemIngredientSnapshot(item, productQuantity, capturedAtUtc.Value));
    }

    private static NormalizedSnapshot Normalize(OrderItemSnapshot snapshot)
    {
        if (snapshot.ProductId is <= 0)
            throw new DomainException("Product id must be greater than zero when supplied.");
        if (snapshot.ProductTypeId is <= 0)
            throw new DomainException("Product type id must be greater than zero when supplied.");
        if (string.IsNullOrWhiteSpace(snapshot.ProductName))
            throw new DomainException("Order item product name is required.");
        if (snapshot.UnitPrice < 0)
            throw new DomainException("Order item unit price cannot be negative.");
        if (snapshot.Quantity <= 0)
            throw new DomainException("Order item quantity must be greater than zero.");
        if ((snapshot.IngredientRecipe is null) != (snapshot.IngredientSnapshotCapturedAtUtc is null))
            throw new DomainException("Ingredient recipe and capture time must either both be supplied or both be omitted.");
        if (snapshot.IngredientSnapshotCapturedAtUtc is { Kind: not DateTimeKind.Utc })
            throw new DomainException("Ingredient snapshot time must be UTC.");

        var ingredientRecipe = snapshot.IngredientRecipe?.ToArray();
        if (ingredientRecipe is not null)
        {
            if (ingredientRecipe.Where(item => item.IngredientId.HasValue)
                .GroupBy(item => item.IngredientId!.Value).Any(group => group.Count() > 1))
                throw new DomainException("Ingredient snapshot ids must be unique.");
            if (ingredientRecipe.GroupBy(item => item.IngredientCode, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
                throw new DomainException("Ingredient snapshot codes must be unique.");
            foreach (var recipe in ingredientRecipe)
            {
                OrderItemIngredientSnapshot.Validate(
                    recipe,
                    snapshot.Quantity,
                    snapshot.IngredientSnapshotCapturedAtUtc!.Value);
            }
        }

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
            snapshot.HasBanner ? bannerMessage : null,
            ingredientRecipe,
            snapshot.IngredientSnapshotCapturedAtUtc,
            snapshot.ProductTypeId,
            NormalizeOptional(snapshot.ProductTypeName, 200, "Product type name"));
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
        string? BannerMessage,
        IReadOnlyList<IngredientRecipeSnapshot>? IngredientRecipe,
        DateTime? IngredientSnapshotCapturedAtUtc,
        int? ProductTypeId,
        string? ProductTypeName);
}
