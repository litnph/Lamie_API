namespace Lamie.Application.Settings.Attributes.Occasions;

public sealed record OccasionTranslationInput
{
    public string LanguageCode { get; set; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

public sealed record OccasionDto
{
    public int Id { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<OccasionTranslationDto> Translations { get; init; } = [];
}

public sealed record OccasionTranslationDto
{
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

