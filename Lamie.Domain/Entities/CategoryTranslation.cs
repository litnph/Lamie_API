namespace Lamie.Domain.Entities;

public class CategoryTranslation : Entity
{
    public int Id { get; private set; }
    public int CategoryId { get; private set; }
    public string LanguageCode { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    private CategoryTranslation() { } // EF

    internal CategoryTranslation(string languageCode, string name, string? description)
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

