using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities
{
    public class ProductTranslation : Entity
    {
        public int Id { get; private set; }
        public int ProductId { get; private set; }
        public string LanguageCode { get; private set; } = default!;
        public string Name { get; private set; } = default!;
        public string Slug { get; private set; } = default!;
        public string Description { get; private set; } = default!;

        private ProductTranslation() { } // EF

        internal ProductTranslation(string languageCode, string name, string slug, string description)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                throw new DomainException("Language code is required");

            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Name is required");

            if (string.IsNullOrWhiteSpace(slug))
                throw new DomainException("Slug is required");

            LanguageCode = languageCode;
            Name = name;
            Slug = slug;
            Description = description;
        }

        internal void Update(string name, string slug, string description)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Name is required");

            if (string.IsNullOrWhiteSpace(slug))
                throw new DomainException("Slug is required");

            Name = name;
            Slug = slug;
            Description = description;
        }
    }

}
