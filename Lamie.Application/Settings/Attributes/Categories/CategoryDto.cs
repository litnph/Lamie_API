namespace Lamie.Application.Settings.Attributes.Categories;

public sealed record CategoryTranslationInput
{
    public string LanguageCode { get; set; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

public sealed record CategoryDto
{
    public int Id { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<CategoryTranslationDto> Translations { get; init; } = [];
}

public sealed record CategoryTranslationDto
{
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

