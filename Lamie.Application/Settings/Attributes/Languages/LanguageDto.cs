namespace Lamie.Application.Settings.Attributes.Languages;

public sealed record LanguageDto
{
    public string Code { get; init; } = default!;
    public string Name { get; init; } = default!;
    public bool IsActive { get; init; }
}

