using System.Collections.Generic;
using System.Linq;

namespace Lamie.Domain.Entities;

public class Collection : Entity
{
    private readonly List<CollectionTranslation> _translations = new();

    public int Id { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<CollectionTranslation> Translations => _translations;

    private Collection() { } // EF

    public Collection(bool isActive = true)
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
            _translations.Add(new CollectionTranslation(languageCode, name, description));
        }
        else
        {
            existing.Update(name, description);
        }
    }
}

