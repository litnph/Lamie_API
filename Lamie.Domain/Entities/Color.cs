using System.Collections.Generic;
using System.Linq;

namespace Lamie.Domain.Entities;

public class Color : Entity
{
    private readonly List<ColorTranslation> _translations = new();

    public int Id { get; private set; }
    public string HexCode { get; private set; } = default!;
    public string RgbCode { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<ColorTranslation> Translations => _translations;

    private Color() { } // EF

    public Color(string hexCode, string rgbCode, bool isActive = true)
    {
        HexCode = hexCode;
        RgbCode = rgbCode;
        IsActive = isActive;
    }

    public void Update(string hexCode, string rgbCode, bool isActive)
    {
        HexCode = hexCode;
        RgbCode = rgbCode;
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
            _translations.Add(new ColorTranslation(languageCode, name, description));
        }
        else
        {
            existing.Update(name, description);
        }
    }
}

