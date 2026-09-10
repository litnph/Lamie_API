using Lamie.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lamie.Domain.Entities
{
    public class Product : Entity
    {
        private readonly List<ProductTranslation> _translations = new();
        private readonly List<ProductImage> _images = new();
        private readonly List<ProductCollection> _collections = new();
        private readonly List<ProductColor> _colors = new();
        private readonly List<ProductTag> _tags = new();
        private readonly List<ProductStyle> _styles = new();
        private readonly List<ProductOccasion> _occasions = new();
        private readonly List<ProductIngredient> _ingredients = new();
        private readonly List<ProductSimilarProduct> _similarProducts = new();

        public int Id { get; private set; }
        public string Sku { get; private set; } = default!;
        public decimal Price { get; private set; }
        public decimal? SalePrice { get; private set; }
        public int Stock { get; private set; }
        public bool TracksInventory { get; private set; }
        public int CategoryId { get; private set; }
        public int? ProductTypeId { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsVisibleOnFE { get; private set; }
        public string? ThumbnailUrl { get; private set; }
        public byte[]? ThumbnailVisualEmbedding { get; private set; }
        public string? ThumbnailVisualEmbeddingVersion { get; private set; }
        public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

        public IReadOnlyCollection<ProductTranslation> Translations => _translations;
        public IReadOnlyCollection<ProductImage> Images => _images;

        public IReadOnlyCollection<ProductCollection> Collections => _collections;
        public IReadOnlyCollection<ProductColor> Colors => _colors;
        public IReadOnlyCollection<ProductTag> Tags => _tags;
        public IReadOnlyCollection<ProductStyle> Styles => _styles;
        public IReadOnlyCollection<ProductOccasion> Occasions => _occasions;
        public IReadOnlyCollection<ProductIngredient> Ingredients => _ingredients;
        public IReadOnlyCollection<ProductSimilarProduct> SimilarProducts => _similarProducts;

        private Product() { } // EF

        public Product(string sku, decimal price, int stock, int categoryId, int productTypeId, bool tracksInventory = true)
        {
            if (string.IsNullOrWhiteSpace(sku))
                throw new DomainException("SKU is required");

            if (price <= 0)
                throw new DomainException("Price must be greater than 0");

            if (stock < 0)
                throw new DomainException("Stock must be greater than or equal to 0");

            if (categoryId <= 0)
                throw new DomainException("CategoryId is required");
            if (productTypeId <= 0)
                throw new DomainException("ProductTypeId is required");

            Sku = sku;
            Price = price;
            TracksInventory = tracksInventory;
            Stock = tracksInventory ? stock : 0;
            CategoryId = categoryId;
            ProductTypeId = productTypeId;
            IsActive = true;
            IsVisibleOnFE = false;
        }

        public void AddTranslation(string languageCode, string name, string slug, string description)
        {
            if (_translations.Any(x => string.Equals(x.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase)))
                throw new DomainException("Translation already exists");

            _translations.Add(new ProductTranslation(languageCode, name, slug, description));
        }

        public void UpdateDetails(string sku, int stock, int categoryId, int productTypeId, bool tracksInventory = true)
        {
            if (string.IsNullOrWhiteSpace(sku))
                throw new DomainException("SKU is required");

            if (stock < 0)
                throw new DomainException("Stock must be greater than or equal to 0");

            if (categoryId <= 0)
                throw new DomainException("CategoryId is required");

            if (productTypeId <= 0)
                throw new DomainException("ProductTypeId is required");

            Sku = sku;
            TracksInventory = tracksInventory;
            Stock = tracksInventory ? stock : 0;
            CategoryId = categoryId;
            ProductTypeId = productTypeId;
        }

        public void UpdatePricing(decimal price, decimal? salePrice)
        {
            if (price <= 0)
                throw new DomainException("Price must be greater than 0");

            if (salePrice.HasValue && salePrice.Value <= 0)
                throw new DomainException("Sale price must be greater than 0");

            if (salePrice.HasValue && salePrice.Value >= price)
                throw new DomainException("Sale price must be less than original price");

            Price = price;
            SalePrice = salePrice;
        }

        public void ReplaceTranslations(
            IEnumerable<(string LanguageCode, string Name, string Slug, string Description)> translations)
        {
            var requested = translations.ToList();
            if (requested.Count == 0)
                throw new DomainException("At least one translation is required");

            if (requested.Any(x => string.IsNullOrWhiteSpace(x.LanguageCode)))
                throw new DomainException("Language code is required");

            if (requested
                .GroupBy(x => x.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
                throw new DomainException("Translation language codes must be unique");

            var requestedCodes = requested
                .Select(x => x.LanguageCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _translations.RemoveAll(x => !requestedCodes.Contains(x.LanguageCode));

            foreach (var item in requested)
            {
                var existing = _translations.FirstOrDefault(x =>
                    string.Equals(x.LanguageCode, item.LanguageCode, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    AddTranslation(item.LanguageCode, item.Name, item.Slug, item.Description);
                }
                else
                {
                    existing.Update(item.Name, item.Slug, item.Description);
                }
            }
        }

        public void AddImage(
            string imageUrl,
            int sortOrder,
            byte[]? visualEmbedding = null,
            string? visualEmbeddingVersion = null)
        {
            _images.Add(new ProductImage(
                imageUrl,
                sortOrder,
                visualEmbedding,
                visualEmbeddingVersion));
        }

        public void UpdateImage(
            int imageId,
            string imageUrl,
            int sortOrder,
            byte[]? visualEmbedding = null,
            string? visualEmbeddingVersion = null)
        {
            var image = _images.FirstOrDefault(x => x.Id == imageId);
            if (image is null)
                throw new DomainException($"Image with id {imageId} not found.");

            image.Update(imageUrl, sortOrder, visualEmbedding, visualEmbeddingVersion);
        }

        public void DeactivateImage(int imageId)
        {
            var image = _images.FirstOrDefault(x => x.Id == imageId);
            if (image is null)
                return;

            image.Deactivate();
        }

        public void AddCollection(int collectionId)
        {
            if (collectionId <= 0) throw new DomainException("CollectionId is required");
            if (_collections.Any(x => x.CollectionId == collectionId)) return;
            _collections.Add(new ProductCollection(collectionId));
        }

        public void AddColor(int colorId)
        {
            if (colorId <= 0) throw new DomainException("ColorId is required");
            if (_colors.Any(x => x.ColorId == colorId)) return;
            _colors.Add(new ProductColor(colorId));
        }

        public void AddTag(int tagId)
        {
            if (tagId <= 0) throw new DomainException("TagId is required");
            if (_tags.Any(x => x.TagId == tagId)) return;
            _tags.Add(new ProductTag(tagId));
        }

        public void AddStyle(int styleId)
        {
            if (styleId <= 0) throw new DomainException("StyleId is required");
            if (_styles.Any(x => x.StyleId == styleId)) return;
            _styles.Add(new ProductStyle(styleId));
        }

        public void AddOccasion(int occasionId)
        {
            if (occasionId <= 0) throw new DomainException("OccasionId is required");
            if (_occasions.Any(x => x.OccasionId == occasionId)) return;
            _occasions.Add(new ProductOccasion(occasionId));
        }

        public void ReplaceTagIds(IEnumerable<int> tagIds)
        {
            SynchronizeRelation(_tags, tagIds, item => item.TagId, AddTag);
        }

        public void ReplaceColorIds(IEnumerable<int> colorIds)
        {
            SynchronizeRelation(_colors, colorIds, item => item.ColorId, AddColor);
        }

        public void ReplaceCollectionIds(IEnumerable<int> collectionIds)
        {
            SynchronizeRelation(_collections, collectionIds, item => item.CollectionId, AddCollection);
        }

        public void ReplaceStyleIds(IEnumerable<int> styleIds)
        {
            SynchronizeRelation(_styles, styleIds, item => item.StyleId, AddStyle);
        }

        public void ReplaceOccasionIds(IEnumerable<int> occasionIds)
        {
            SynchronizeRelation(_occasions, occasionIds, item => item.OccasionId, AddOccasion);
        }

        public void ReplaceSimilarProductIds(IEnumerable<int> productIds)
        {
            var ids = productIds.Distinct().ToArray();
            if (ids.Any(id => id <= 0))
                throw new DomainException("Similar product ids must be greater than 0");
            if (Id > 0 && ids.Contains(Id))
                throw new DomainException("A product cannot be similar to itself");

            var requested = ids.ToHashSet();
            _similarProducts.RemoveAll(item => !requested.Contains(item.SimilarProductId));
            var existing = _similarProducts.Select(item => item.SimilarProductId).ToHashSet();
            foreach (var id in ids.Where(id => !existing.Contains(id)))
                _similarProducts.Add(new ProductSimilarProduct(id));
        }

        public void ReplaceIngredients(IEnumerable<ProductIngredientDefinition> definitions)
        {
            var requested = definitions.ToList();
            if (requested.GroupBy(item => item.IngredientId).Any(group => group.Count() > 1))
                throw new DomainException("Product ingredient ids must be unique");

            var requestedIds = requested.Select(item => item.IngredientId).ToHashSet();
            _ingredients.RemoveAll(item => !requestedIds.Contains(item.IngredientId));

            foreach (var definition in requested)
            {
                var existing = _ingredients.FirstOrDefault(item => item.IngredientId == definition.IngredientId);
                if (existing is null)
                    _ingredients.Add(new ProductIngredient(definition));
                else
                    existing.Update(definition);
            }
        }

        public void SetVisibilityOnFE(bool isVisible) => IsVisibleOnFE = isVisible;

        private static void SynchronizeRelation<TRelation>(
            List<TRelation> current,
            IEnumerable<int> requestedIds,
            Func<TRelation, int> getId,
            Action<int> add)
        {
            var ids = requestedIds.Distinct().ToArray();
            if (ids.Any(id => id <= 0))
                throw new DomainException("Relation ids must be greater than 0");

            var requested = ids.ToHashSet();
            current.RemoveAll(item => !requested.Contains(getId(item)));

            var existing = current.Select(getId).ToHashSet();
            foreach (var id in ids.Where(id => !existing.Contains(id)))
            {
                add(id);
            }
        }

        public void SetSalePrice(decimal salePrice)
        {
            if (salePrice <= 0)
                throw new DomainException("Sale price must be greater than 0");

            if (salePrice >= Price)
                throw new DomainException("Sale price must be less than original price");

            SalePrice = salePrice;
        }

        public void RemoveSalePrice()
        {
            SalePrice = null;
        }

        public void ChangePrice(decimal newPrice)
        {
            if (newPrice <= 0)
                throw new DomainException("Price must be greater than 0");

            if (SalePrice.HasValue && SalePrice.Value >= newPrice)
                throw new DomainException("New price must be greater than sale price");

            Price = newPrice;
        }

        public void SetThumbnail(
            string? url,
            byte[]? visualEmbedding = null,
            string? visualEmbeddingVersion = null)
        {
            if (string.Equals(ThumbnailUrl, url, StringComparison.OrdinalIgnoreCase)
                && visualEmbedding is null)
            {
                ThumbnailUrl = url;
                return;
            }

            ThumbnailUrl = url;
            ThumbnailVisualEmbedding = visualEmbedding;
            ThumbnailVisualEmbeddingVersion = visualEmbedding is null
                ? null
                : visualEmbeddingVersion;
        }

        public void ReserveStock(int quantity)
        {
            if (quantity <= 0)
                throw new DomainException("Reserved quantity must be greater than zero");
            if (!IsActive)
                throw new DomainException($"Product '{Sku}' is inactive");
            if (!TracksInventory)
                return;
            if (Stock < quantity)
                throw new DomainException($"Insufficient stock for product '{Sku}'");

            Stock -= quantity;
        }

        public void RestoreStock(int quantity)
        {
            if (quantity <= 0)
                throw new DomainException("Restored quantity must be greater than zero");
            if (!TracksInventory)
                return;

            Stock = checked(Stock + quantity);
        }
    }

}
