using System.Collections.Generic;

namespace Lamie.Domain.Entities;

public class Tag : Entity
{
    private readonly List<TagTranslation> _translations = new();

    public int Id { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<TagTranslation> Translations => _translations;

    private Tag() { } // EF

    public Tag(bool isActive = true)
    {
        IsActive = isActive;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }

    public void AddOrUpdateTranslation(string languageCode, string name, string? description)
    {
        var existing = _translations.FirstOrDefault(t => t.LanguageCode == languageCode);
        if (existing is null)
        {
            _translations.Add(new TagTranslation(languageCode, name, description));
        }
        else
        {
            existing.Update(name, description);
        }
    }
}

