namespace Lamie.Application.Settings.Products.Dtos;

public sealed record ProductTranslationDto
{
    public int Id { get; init; }
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public string Description { get; init; } = default!;
}

public sealed record ProductImageDto
{
    public int Id { get; init; }
    public string ImageUrl { get; init; } = default!;
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
}

public sealed record ProductIngredientInputDto
{
    public int IngredientId { get; init; }
    public decimal BaseQuantity { get; init; }
    public string? Note { get; init; }
    public int SortOrder { get; init; }
}

public sealed record ProductIngredientDto
{
    public int Id { get; init; }
    public int IngredientId { get; init; }
    public decimal BaseQuantity { get; init; }
    public string? Note { get; init; }
    public int SortOrder { get; init; }
}

public sealed record ProductDetailsDto
{
    public int Id { get; init; }
    public string Sku { get; init; } = default!;
    public decimal Price { get; init; }
    public decimal? SalePrice { get; init; }
    public int Stock { get; init; }
    public bool TracksInventory { get; init; }
    public int CategoryId { get; init; }
    public int? ProductTypeId { get; init; }
    public bool IsActive { get; init; }
    public bool IsVisibleOnFE { get; init; }
    public string? ThumbnailUrl { get; init; }

    public IReadOnlyList<ProductTranslationDto> Translations { get; init; } = [];
    public IReadOnlyList<ProductImageDto> Images { get; init; } = [];

    public IReadOnlyList<int> TagIds { get; init; } = [];
    public IReadOnlyList<int> ColorIds { get; init; } = [];
    public IReadOnlyList<int> CollectionIds { get; init; } = [];
    public IReadOnlyList<int> StyleIds { get; init; } = [];
    public IReadOnlyList<int> OccasionIds { get; init; } = [];
    public IReadOnlyList<int> SimilarProductIds { get; init; } = [];
    public IReadOnlyList<ProductIngredientDto> Ingredients { get; init; } = [];
}

