using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public class ProductType : Entity
{
    private readonly List<ProductTypeTranslation> _translations = new();

    public int Id { get; private set; }
    public string Code { get; private set; } = default!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyCollection<ProductTypeTranslation> Translations => _translations;

    private ProductType() { }

    public ProductType(string code, int sortOrder, bool isActive = true)
    {
        Update(code, sortOrder, isActive);
    }

    public void Update(string code, int sortOrder, bool isActive)
    {
        var normalizedCode = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
            throw new DomainException("Product type code is required");
        if (normalizedCode.Length > 50)
            throw new DomainException("Product type code must not exceed 50 characters");
        if (normalizedCode.Any(character =>
                !char.IsAsciiLetterUpper(character) && !char.IsDigit(character) && character != '_'))
            throw new DomainException("Product type code may contain only A-Z, 0-9, and underscore");
        if (sortOrder < 0)
            throw new DomainException("Sort order must be greater than or equal to 0");

        Code = normalizedCode;
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public void AddOrUpdateTranslation(string languageCode, string name, string? description)
    {
        var existing = _translations.FirstOrDefault(translation =>
            string.Equals(translation.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            _translations.Add(new ProductTypeTranslation(languageCode, name, description));
        else
            existing.Update(name, description);
    }
}
