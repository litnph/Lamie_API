using Lamie.API.Controllers;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Common.Persistence;
using Lamie.Application.Settings.Products.Commands;
using Lamie.Application.Settings.Products.Dtos;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Lamie.Tests.Products;

public sealed class ProductSynchronizationTests
{
    [Fact]
    public void ConstructorRejectsNegativeStockEvenWithoutApplicationValidation()
    {
        var exception = Assert.Throws<Lamie.Domain.Exceptions.DomainException>(
            () => new Product("SKU-NEGATIVE", 100m, -1, 1, 1));

        Assert.Equal("Stock must be greater than or equal to 0", exception.Message);
    }

    [Fact]
    public void ReplaceMethodsSynchronizeTranslationsAndRelations()
    {
        var product = new Product("OLD", 100m, 2, 1, 1);
        product.AddTranslation("vi", "Tên cũ", "ten-cu", "Mô tả cũ");
        product.AddTranslation("en", "Old name", "old-name", "Old description");
        product.AddTag(1);
        product.AddTag(2);
        product.AddColor(4);

        product.ReplaceTranslations([
            ("vi", "Tên mới", "ten-moi", "Mô tả mới"),
            ("ja", "新しい名前", "new-ja", "説明")
        ]);
        product.ReplaceTagIds([2, 3]);
        product.ReplaceColorIds([]);

        Assert.Equal(2, product.Translations.Count);
        Assert.DoesNotContain(product.Translations, item => item.LanguageCode == "en");
        Assert.Contains(product.Translations, item =>
            item.LanguageCode == "vi" && item.Name == "Tên mới" && item.Slug == "ten-moi");
        Assert.Equal([2, 3], product.Tags.Select(item => item.TagId).OrderBy(id => id));
        Assert.Empty(product.Colors);
    }

    [Fact]
    public async Task UpdateHandlerPersistsFullAggregateAndSupportsThumbnailFile()
    {
        var product = new Product("OLD", 100m, 2, 1, 1);
        product.AddTranslation("vi", "Tên cũ", "ten-cu", "");
        product.AddTag(1);
        product.AddImage("/uploads/products/old/image.jpg", 1);
        product.SetThumbnail("/uploads/products/old/thumbnail.jpg");

        var repository = new FakeProductRepository(product);
        var storage = new FakeFileStorage();
        var handler = new UpdateProductHandler(
            repository,
            storage,
            new FakeProductTypeRepository(),
            new FakeReferentialIntegrityService());
        await using var thumbnailContent = new MemoryStream(ValidPng);
        var thumbnail = new FormFile(thumbnailContent, 0, thumbnailContent.Length, "ThumbnailFile", "thumb.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
        var command = new UpdateProductCommand
        {
            Id = product.Id,
            Sku = "OLD",
            Price = 120m,
            SalePrice = 90m,
            Stock = 7,
            CategoryId = 3,
            ProductTypeId = 2,
            ThumbnailFile = thumbnail,
            Translations =
            [
                new CreateProductTranslationDto
                {
                    LanguageCode = "vi",
                    Name = "Tên mới",
                    Slug = "ten-moi",
                    Description = "Mô tả"
                }
            ],
            TagIds = [2],
            ColorIds = [4],
            CollectionIds = [5],
            StyleIds = [6],
            OccasionIds = [7],
            Images = []
        };
        command.UseFullReplacementSemantics();

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("OLD", product.Sku);
        Assert.Equal(120m, product.Price);
        Assert.Equal(90m, product.SalePrice);
        Assert.Equal(7, product.Stock);
        Assert.Equal(3, product.CategoryId);
        Assert.Equal(2, product.ProductTypeId);
        Assert.Single(product.Translations);
        Assert.Equal("Tên mới", product.Translations.Single().Name);
        Assert.Equal([2], product.Tags.Select(item => item.TagId));
        Assert.Equal([4], product.Colors.Select(item => item.ColorId));
        Assert.Equal([5], product.Collections.Select(item => item.CollectionId));
        Assert.Equal([6], product.Styles.Select(item => item.StyleId));
        Assert.Equal([7], product.Occasions.Select(item => item.OccasionId));
        Assert.All(product.Images, image => Assert.False(image.IsActive));
        Assert.StartsWith("/uploads/products/OLD/", product.ThumbnailUrl);
        Assert.Equal(1, repository.UpdateCount);
        Assert.Contains("/uploads/products/old/thumbnail.jpg", storage.DeletedUrls);
        Assert.Contains("/uploads/products/old/image.jpg", storage.DeletedUrls);
    }

    [Fact]
    public async Task LegacyCollectionUpdatePreservesOmittedCollectionsAndMedia()
    {
        var product = new Product("OLD", 100m, 2, 1, 1);
        product.AddTranslation("vi", "Tên cũ", "ten-cu", "");
        product.AddTag(1);
        product.AddImage("/uploads/products/old/image.jpg", 1);
        product.SetThumbnail("/uploads/products/old/thumbnail.jpg");
        var repository = new FakeProductRepository(product);
        var storage = new FakeFileStorage();
        var handler = new UpdateProductHandler(
            repository,
            storage,
            new FakeProductTypeRepository(),
            new FakeReferentialIntegrityService());
        var command = new UpdateProductCommand
        {
            Id = product.Id,
            Sku = "OLD",
            Price = 110m,
            Stock = 3,
            CategoryId = 2,
            ProductTypeId = 1
        };

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("OLD", product.Sku);
        Assert.Single(product.Translations);
        Assert.Equal([1], product.Tags.Select(item => item.TagId));
        Assert.Single(product.Images);
        Assert.True(product.Images.Single().IsActive);
        Assert.Equal("/uploads/products/old/thumbnail.jpg", product.ThumbnailUrl);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task ValidatorsRejectZeroSalePriceDuplicateLanguagesAndInvalidSignature()
    {
        using var invalidThumbnailContent = new MemoryStream([1, 2, 3]);
        var command = new CreateProductCommand
        {
            Sku = "SKU-1",
            Price = 100m,
            SalePrice = 0m,
            Stock = 1,
            CategoryId = 1,
            ProductTypeId = 1,
            ThumbnailFile = new FormFile(
                invalidThumbnailContent,
                0,
                invalidThumbnailContent.Length,
                "ThumbnailFile",
                "not-an-image.txt")
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/plain"
            },
            Translations =
            [
                new CreateProductTranslationDto { LanguageCode = "vi", Name = "Một", Slug = "mot" },
                new CreateProductTranslationDto { LanguageCode = "VI", Name = "Hai", Slug = "hai" }
            ]
        };

        var result = await new CreateProductValidator().ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.SalePrice));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.Translations));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(command.ThumbnailFile));
    }

    [Fact]
    public async Task SharedImagePolicyAcceptsPngSignatureAndRejectsDisguisedContent()
    {
        var valid = FormFile("valid.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]);
        var disguised = FormFile("fake.png", "image/png", "not png"u8.ToArray());

        Assert.True(ImageUploadPolicy.HasAllowedMetadata(valid));
        Assert.True(await ImageUploadPolicy.HasValidSignatureAsync(valid));
        Assert.True(ImageUploadPolicy.HasAllowedMetadata(disguised));
        Assert.False(await ImageUploadPolicy.HasValidSignatureAsync(disguised));
    }

    [Fact]
    public async Task ReferencedProductDeleteReturnsConflictBeforeDeletingFilesOrData()
    {
        var product = new Product("ORDERED", 100m, 2, 1, 1);
        product.AddImage("/uploads/products/ordered/image.png", 0);
        var repository = new FakeProductRepository(product) { HasOrderReferences = true };
        var storage = new FakeFileStorage();
        var handler = new DeleteProductHandler(repository, storage);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(new DeleteProductCommand(1), CancellationToken.None));

        Assert.Equal(0, repository.DeleteCount);
        Assert.Empty(storage.DeletedUrls);
    }

    [Fact]
    public async Task CreateFailureCleansFilesUploadedByTheRequest()
    {
        var repository = new FakeProductRepository(null);
        var storage = new FakeFileStorage();
        var handler = new CreateProductHandler(
            repository,
            storage,
            new FakeProductTypeRepository(),
            new FakeReferentialIntegrityService());
        var command = new CreateProductCommand
        {
            Sku = "N3W1",
            Price = 100,
            Stock = 1,
            CategoryId = 1,
            ProductTypeId = 1,
            ThumbnailFile = FormFile(
                "thumbnail.png",
                "image/png",
                ValidPng),
            Translations =
            [
                new CreateProductTranslationDto
                {
                    LanguageCode = "vi",
                    Name = "New",
                    Slug = "new"
                }
            ]
        };

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Single(storage.DeletedUrls);
        Assert.Contains("/uploads/products/N3W1/", storage.DeletedUrls[0]);
    }

    [Fact]
    public async Task UpdateFailureCleansNewUploadAndRetainsPreviouslyStoredFile()
    {
        var product = new Product("OLD", 100m, 2, 1, 1);
        product.SetThumbnail("/uploads/products/OLD/original.png");
        var repository = new FakeProductRepository(product) { ThrowOnUpdate = true };
        var storage = new FakeFileStorage();
        var handler = new UpdateProductHandler(
            repository,
            storage,
            new FakeProductTypeRepository(),
            new FakeReferentialIntegrityService());
        var command = new UpdateProductCommand
        {
            Id = 1,
            Sku = "OLD",
            Price = 100,
            Stock = 2,
            CategoryId = 1,
            ProductTypeId = 1,
            ThumbnailFile = FormFile("new.png", "image/png", ValidPng)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(command, CancellationToken.None));

        Assert.Single(storage.DeletedUrls);
        Assert.Contains("/uploads/products/OLD/", storage.DeletedUrls[0]);
        Assert.DoesNotContain("/uploads/products/OLD/original.png", storage.DeletedUrls);
    }

    [Fact]
    public void ProductControllerKeepsCollectionAndItemUpdateRoutes()
    {
        var collectionUpdate = typeof(ProductController).GetMethod(nameof(ProductController.Update));
        var itemUpdate = typeof(ProductController).GetMethod(nameof(ProductController.UpdateById));
        Assert.NotNull(collectionUpdate);
        Assert.NotNull(itemUpdate);

        var collectionTemplates = collectionUpdate!
            .GetCustomAttributes(typeof(HttpPutAttribute), inherit: false)
            .Cast<HttpPutAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();
        var itemTemplates = itemUpdate!
            .GetCustomAttributes(typeof(HttpPutAttribute), inherit: false)
            .Cast<HttpPutAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();

        Assert.Contains(collectionTemplates, template => template is null);
        Assert.Contains("{id:int}", itemTemplates);
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        private readonly Product? _product;

        public FakeProductRepository(Product? product)
        {
            _product = product;
        }

        public int UpdateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public bool HasOrderReferences { get; init; }
        public bool ThrowOnUpdate { get; init; }

        public Task AddAsync(Product product) => throw new NotSupportedException();

        public Task<Product?> GetByIdAsync(int id) => Task.FromResult(_product);

        public Task<List<Product>?> GetAllAsync() => throw new NotSupportedException();

        public Task UpdateAsync(Product product)
        {
            Assert.Same(_product, product);
            if (ThrowOnUpdate)
                throw new InvalidOperationException("Simulated persistence failure.");
            UpdateCount += 1;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Product product)
        {
            DeleteCount += 1;
            return Task.CompletedTask;
        }

        public Task<bool> HasOrderReferencesAsync(
            int productId,
            CancellationToken cancellationToken = default) => Task.FromResult(HasOrderReferences);
    }

    private sealed class FakeProductTypeRepository : IProductTypeRepository
    {
        public Task<ProductType?> GetByIdAsync(int id) =>
            Task.FromResult<ProductType?>(id > 0 ? new ProductType($"TYPE_{id}", id) : null);

        public Task<List<ProductType>> GetAllAsync() => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(string code, int? excludingId = null) => throw new NotSupportedException();
        public Task<bool> IsInUseAsync(int id) => throw new NotSupportedException();
        public Task AddAsync(ProductType productType) => throw new NotSupportedException();
        public Task UpdateAsync(ProductType productType) => throw new NotSupportedException();
        public Task DeleteAsync(ProductType productType) => throw new NotSupportedException();
    }

    private sealed class FakeReferentialIntegrityService : IReferentialIntegrityService
    {
        public Task ValidateProductReferencesAsync(
            ProductReferenceSet references,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EnsureCanDeleteAsync(
            ReferencedMasterData target,
            object id,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeFileStorage : IFileStorage
    {
        public List<string> DeletedUrls { get; } = [];

        public async Task<string> UploadPublicAsync(
            Stream content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var target = new MemoryStream();
            await content.CopyToAsync(target, cancellationToken);
            Assert.NotEmpty(target.ToArray());
            Assert.Equal("image/png", contentType);
            return $"/uploads/{objectPath}";
        }

        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(publicUrl))
            {
                DeletedUrls.Add(publicUrl);
            }
            return Task.CompletedTask;
        }
    }

    private static byte[] ValidPng
    {
        get
        {
            using var image = new Image<Rgba32>(2, 2, new Rgba32(240, 240, 240));
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            return stream.ToArray();
        }
    }

    private static IFormFile FormFile(string name, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, stream.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
