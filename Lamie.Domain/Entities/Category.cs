using System.Collections.Generic;
using System.Linq;

namespace Lamie.Domain.Entities;

public class Category : Entity
{
    private readonly List<CategoryTranslation> _translations = new();

    public int Id { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<CategoryTranslation> Translations => _translations;

    private Category() { } // EF

    public Category(int sortOrder, bool isActive = true)
    {
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public void Update(int sortOrder, bool isActive)
    {
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
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
            _translations.Add(new CategoryTranslation(languageCode, name, description));
        }
        else
        {
            existing.Update(name, description);
        }
    }
}

