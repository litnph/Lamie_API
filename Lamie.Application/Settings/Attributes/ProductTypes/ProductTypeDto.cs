namespace Lamie.Application.Settings.Attributes.ProductTypes;

public sealed record ProductTypeTranslationInput
{
    public string LanguageCode { get; set; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

public sealed record ProductTypeDto
{
    public int Id { get; init; }
    public string Code { get; init; } = default!;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<ProductTypeTranslationDto> Translations { get; init; } = [];
}

public sealed record ProductTypeTranslationDto
{
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}
