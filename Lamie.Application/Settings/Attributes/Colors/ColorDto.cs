namespace Lamie.Application.Settings.Attributes.Colors;

public sealed record ColorTranslationInput
{
    public string LanguageCode { get; set; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

public sealed record ColorDto
{
    public int Id { get; init; }
    public string HexCode { get; init; } = default!;
    public string RgbCode { get; init; } = default!;
    public bool IsActive { get; init; }
    public IReadOnlyList<ColorTranslationDto> Translations { get; init; } = [];
}

public sealed record ColorTranslationDto
{
    public string LanguageCode { get; init; } = default!;
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
}

