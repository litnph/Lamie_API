using Lamie.API.Options;
using Lamie.API.Services;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Identity;
using Lamie.Application.Settings.Products;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Lamie.Tests.Products;

public sealed class ProductDiscoveryTests
{
    [Fact]
    public async Task Configured_shop_logo_is_applied_without_blocking_product_image_processing()
    {
        var databaseName = $"LamieLogoWatermark_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var settings = await dbContext.ProductCatalogSettings.SingleAsync();
            settings.UpdateBranding(
                settings.PriceDeviationPercent,
                true,
                ProductWatermarkSource.ShopLogo,
                "/uploads/shop/logo.png",
                null,
                DateTime.UtcNow);
            await dbContext.SaveChangesAsync();

            await using var logo = await SolidImageAsync(new Rgba32(116, 100, 90));
            await using var source = await SolidImageAsync(new Rgba32(245, 245, 245));
            var service = new ConfiguredProductImageWatermarkService(
                dbContext,
                new FixedFileReader(logo.ToArray()),
                NullLogger<ConfiguredProductImageWatermarkService>.Instance);

            await using var output = await service.ApplyAsync(source, "A7K2", "image/png");
            using var result = await Image.LoadAsync<Rgba32>(output);

            Assert.Equal(160, result.Width);
            Assert.Equal(160, result.Height);
            Assert.NotEqual(new Rgba32(245, 245, 245), result[140, 140]);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task Unreadable_configured_logo_falls_back_to_the_clean_product_image()
    {
        var databaseName = $"LamieInvalidLogoWatermark_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var settings = await dbContext.ProductCatalogSettings.SingleAsync();
            settings.UpdateBranding(
                settings.PriceDeviationPercent,
                true,
                ProductWatermarkSource.ShopLogo,
                "/uploads/shop/logo.png",
                null,
                DateTime.UtcNow);
            await dbContext.SaveChangesAsync();

            await using var source = await SolidImageAsync(new Rgba32(245, 245, 245));
            var expected = source.ToArray();
            var service = new ConfiguredProductImageWatermarkService(
                dbContext,
                new FixedFileReader([1, 2, 3]),
                NullLogger<ConfiguredProductImageWatermarkService>.Instance);

            await using var output = await service.ApplyAsync(source, "A7K2", "image/png");

            Assert.Equal(expected, output.ToArray());
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task Branding_settings_upload_assets_persist_and_survive_watermark_disable()
    {
        var databaseName = $"LamieBranding_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var storage = new TrackingFileStorage();
            var service = new ProductCatalogFeatureService(
                dbContext,
                new MissingFileReader(),
                new StubEmbeddingProvider(),
                new NoOpAuditWriter(),
                TimeProvider.System,
                Options.Create(new ProductRecognitionOptions()),
                NullLogger<ProductCatalogFeatureService>.Instance,
                storage);

            var enabled = await service.UpdateBrandingSettingsAsync(
                new UpdateProductCatalogBrandingRequest(25, true, ProductWatermarkSource.UploadedImage),
                new ProductCatalogAssetUpload("logo.png", "image/png", [1, 2, 3]),
                new ProductCatalogAssetUpload("mark.png", "image/png", [4, 5, 6]),
                CancellationToken.None);
            var replaced = await service.UpdateBrandingSettingsAsync(
                new UpdateProductCatalogBrandingRequest(25, true, ProductWatermarkSource.UploadedImage),
                new ProductCatalogAssetUpload("logo-new.png", "image/png", [7, 8, 9]),
                new ProductCatalogAssetUpload("mark-new.png", "image/png", [10, 11, 12]),
                CancellationToken.None);
            var disabled = await service.UpdateBrandingSettingsAsync(
                new UpdateProductCatalogBrandingRequest(25, false, ProductWatermarkSource.UploadedImage),
                null,
                null,
                CancellationToken.None);

            Assert.Equal(4, storage.UploadedPaths.Count);
            Assert.Contains(enabled.ShopLogoUrl, storage.DeletedUrls);
            Assert.Contains(enabled.WatermarkImageUrl, storage.DeletedUrls);
            Assert.False(disabled.WatermarkEnabled);
            Assert.Equal(ProductWatermarkSource.UploadedImage, disabled.WatermarkSource);
            Assert.Equal(replaced.ShopLogoUrl, disabled.ShopLogoUrl);
            Assert.Equal(replaced.WatermarkImageUrl, disabled.WatermarkImageUrl);
            dbContext.ChangeTracker.Clear();
            var persisted = await service.GetSettingsAsync(CancellationToken.None);
            Assert.Equal(disabled, persisted);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task Visual_embedding_compares_image_features_and_ignores_the_watermark_edge()
    {
        var provider = new ProductVisualEmbeddingProvider();
        await using var redSource = await SolidImageAsync(new Rgba32(220, 45, 60));
        await using var redCopy = await SolidImageAsync(new Rgba32(220, 45, 60));
        await using var blueSource = await SolidImageAsync(new Rgba32(35, 80, 220));

        var red = await provider.CreateAsync(redSource);
        var redAgain = await provider.CreateAsync(redCopy);
        var blue = await provider.CreateAsync(blueSource);

        Assert.Equal("lamie-visual-v1", provider.Version);
        Assert.Equal(red, redAgain);
        Assert.True(provider.CosineSimilarity(red, redAgain) > 0.999);
        Assert.True(provider.CosineSimilarity(red, blue) < 0.5);
    }

    [Fact]
    public async Task Recognition_returns_at_most_five_active_products_sorted_by_real_scores()
    {
        var databaseName = $"LamieProductRecognition_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var category = new Category(10);
            category.AddOrUpdateTranslation("vi", "Hoa", null);
            var productType = new ProductType("BOUQUET", 10);
            productType.AddOrUpdateTranslation("vi", "Bó hoa", null);
            dbContext.AddRange(category, productType);
            await dbContext.SaveChangesAsync();

            foreach (var score in new byte[] { 50, 99, 70, 90, 60, 80 })
            {
                var product = new Product($"P{score}", 500_000, 0, category.Id, productType.Id, false);
                product.AddTranslation("vi", $"Sản phẩm {score}", $"san-pham-{score}", string.Empty);
                product.SetThumbnail($"/uploads/products/{score}.png", [score], StubEmbeddingProvider.VersionName);
                dbContext.Products.Add(product);
            }
            var inactive = new Product("OFF", 500_000, 0, category.Id, productType.Id, false);
            inactive.AddTranslation("vi", "Đã tắt", "da-tat", string.Empty);
            inactive.SetThumbnail("/uploads/products/off.png", [100], StubEmbeddingProvider.VersionName);
            typeof(Product).GetProperty(nameof(Product.IsActive))!.SetValue(inactive, false);
            dbContext.Products.Add(inactive);
            await dbContext.SaveChangesAsync();

            var service = new ProductCatalogFeatureService(
                dbContext,
                new MissingFileReader(),
                new StubEmbeddingProvider(),
                new NoOpAuditWriter(),
                TimeProvider.System,
                Options.Create(new ProductRecognitionOptions { MinimumSimilarity = 0, BackfillBatchSize = 40 }),
                NullLogger<ProductCatalogFeatureService>.Instance);

            var result = await service.RecognizeAsync(
                new ProductRecognitionUpload("query.png", "image/png", [1]),
                CancellationToken.None);

            Assert.Equal(5, result.Results.Count);
            Assert.Equal(new[] { "P99", "P90", "P80", "P70", "P60" }, result.Results.Select(item => item.Sku));
            Assert.Equal(new decimal[] { 99, 90, 80, 70, 60 }, result.Results.Select(item => item.SimilarityPercent));
            Assert.DoesNotContain(result.Results, item => item.Sku == "OFF");
            Assert.Equal(6, result.IndexedProductCount);
            Assert.Equal(6, result.ActiveProductCount);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task Similar_products_are_directional_deduplicated_and_cannot_reference_self()
    {
        var databaseName = $"LamieSimilarProducts_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var category = new Category(10);
            category.AddOrUpdateTranslation("vi", "Hoa", null);
            var productType = new ProductType("BOUQUET", 10);
            productType.AddOrUpdateTranslation("vi", "Bó hoa", null);
            dbContext.AddRange(category, productType);
            await dbContext.SaveChangesAsync();
            var source = new Product("SRC1", 500_000, 0, category.Id, productType.Id, false);
            var first = new Product("REF1", 450_000, 0, category.Id, productType.Id, false);
            var second = new Product("REF2", 550_000, 0, category.Id, productType.Id, false);
            dbContext.AddRange(source, first, second);
            await dbContext.SaveChangesAsync();

            source.ReplaceSimilarProductIds([first.Id, first.Id, second.Id]);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();
            var reloaded = await dbContext.Products.Include(item => item.SimilarProducts)
                .SingleAsync(item => item.Id == source.Id);

            Assert.Equal(new[] { first.Id, second.Id }, reloaded.SimilarProducts.Select(item => item.SimilarProductId).OrderBy(id => id));
            Assert.Throws<Lamie.Domain.Exceptions.DomainException>(() => reloaded.ReplaceSimilarProductIds([reloaded.Id]));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    private static async Task<MemoryStream> SolidImageAsync(Rgba32 color)
    {
        using var image = new Image<Rgba32>(160, 160, color);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class StubEmbeddingProvider : IProductVisualEmbeddingProvider
    {
        public const string VersionName = "test-visual-v1";
        public string Version => VersionName;

        public Task<byte[]> CreateAsync(Stream image, CancellationToken cancellationToken = default) =>
            Task.FromResult(new byte[] { 1 });

        public double CosineSimilarity(byte[] left, byte[] right) =>
            right.Length == 0 ? 0 : right[0] / 100d;
    }

    private sealed class MissingFileReader : IPublicFileReader
    {
        public Task<StoredPublicFile?> ReadPublicAsync(string? publicUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredPublicFile?>(null);
    }

    private sealed class FixedFileReader(byte[] bytes) : IPublicFileReader
    {
        public Task<StoredPublicFile?> ReadPublicAsync(string? publicUrl, CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredPublicFile?>(new StoredPublicFile(bytes, "image/png", "logo.png"));
    }

    private sealed class NoOpAuditWriter : IAccessAuditWriter
    {
        public void Record(string action, string entityType, string entityId, object? before, object? after)
        {
        }
    }

    private sealed class TrackingFileStorage : IFileStorage
    {
        public List<string> UploadedPaths { get; } = [];
        public List<string?> DeletedUrls { get; } = [];

        public Task<string> UploadPublicAsync(Stream content, string objectPath, string contentType, CancellationToken cancellationToken = default)
        {
            UploadedPaths.Add(objectPath);
            return Task.FromResult($"/uploads/{objectPath}");
        }

        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default)
        {
            DeletedUrls.Add(publicUrl);
            return Task.CompletedTask;
        }
    }
}
