namespace Lamie.Domain.Entities;

public class TagTranslation : Entity
{
    public int Id { get; private set; }
    public int TagId { get; private set; }
    public string LanguageCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    private TagTranslation() { } // EF

    internal TagTranslation(string languageCode, string name, string? description)
    {
        LanguageCode = languageCode;
        Name = name;
        Description = description;
    }

    internal void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}


