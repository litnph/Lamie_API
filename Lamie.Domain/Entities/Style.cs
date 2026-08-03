using System.Collections.Generic;
using System.Linq;

namespace Lamie.Domain.Entities;

public class Style : Entity
{
    private readonly List<StyleTranslation> _translations = new();

    public int Id { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<StyleTranslation> Translations => _translations;

    private Style() { } // EF

    public Style(bool isActive = true)
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
            _translations.Add(new StyleTranslation(languageCode, name, description));
        }
        else
        {
            existing.Update(name, description);
        }
    }
}

