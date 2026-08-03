namespace Lamie.Application.Settings.Attributes.Styles;

public sealed record StyleTranslationInput
{
    public string LanguageCode { get; set; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

public sealed record StyleDto
{
    public int Id { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<StyleTranslationDto> Translations { get; init; } = [];
}

public sealed record StyleTranslationDto
{
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

