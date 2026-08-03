namespace Lamie.Application.Settings.Attributes.Collections;

public sealed record CollectionTranslationInput
{
    public string LanguageCode { get; set; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

public sealed record CollectionDto
{
    public int Id { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<CollectionTranslationDto> Translations { get; init; } = [];
}

public sealed record CollectionTranslationDto
{
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

