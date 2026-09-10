using Lamie.API.Services;
using Lamie.API.Options;
using Lamie.Application.Common.Storage;
using Lamie.Application.FeData;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Options;
using Lamie.Infrastructure.Persistence;
using Lamie.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Lamie.Tests.FeData;

public sealed class FeDataExportTests
{
    [Fact]
    public void ResolveTargetDirectory_RequiresDedicatedFeDataLeaf()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "lamie-fe-data-root");
        var resolved = FeDataExportService.ResolveTargetDirectory(
            Path.Combine("..", "FE_Lamie", "public", "fe-data"),
            Path.Combine("..", "FE_Lamie", "public"),
            contentRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(contentRoot, "..", "FE_Lamie", "public", "fe-data")),
            resolved);
        Assert.Throws<InvalidOperationException>(() =>
            FeDataExportService.ResolveTargetDirectory(
                "../FE_Lamie/public",
                "../FE_Lamie/public",
                contentRoot));
        Assert.Throws<InvalidOperationException>(() =>
            FeDataExportService.ResolveTargetDirectory(
                Path.Combine(contentRoot, "outside", "fe-data"),
                Path.Combine(contentRoot, "public"),
                contentRoot));
    }

    [Fact]
    public async Task LocalReader_ReadsOnlyStorageOwnedPublicUrls()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lamie-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new LocalFileStorage(
                new LocalStorageOptions { RootPath = root, PublicBasePath = "/uploads" },
                root);
            await using var bytes = new MemoryStream(
                [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01]);
            var url = await storage.UploadPublicAsync(
                bytes,
                "products/1.png",
                "image/png");

            var stored = await storage.ReadPublicAsync(url);

            Assert.NotNull(stored);
            Assert.Equal("image/png", stored.ContentType);
            Assert.Equal("1.png", stored.FileName);
            Assert.Null(await storage.ReadPublicAsync("/uploads/../secret.txt"));
            Assert.Null(await storage.ReadPublicAsync("/other/products/1.png"));
            Assert.Null(await storage.ReadPublicAsync("https://example.com/uploads/products/1.png"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_IsStableAndDoesNotReplacePublishedDataWhenAnImageFails()
    {
        var databaseName = $"LamieFeData_{Guid.NewGuid():N}";
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(dbOptions);
        var root = Path.Combine(Path.GetTempPath(), $"lamie-fe-export-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "public", "fe-data");
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var category = new Category(10);
            category.AddOrUpdateTranslation("vi", "Hoa hằng ngày", null);
            var productType = new ProductType("BOUQUET", 10);
            productType.AddOrUpdateTranslation("vi", "Bó hoa", null);
            dbContext.AddRange(category, productType);
            await dbContext.SaveChangesAsync();

            var product = new Product("F001", 450_000, 0, category.Id, productType.Id, false);
            product.AddTranslation("vi", "Sương sớm", "Đóa-Hồng", string.Empty);
            product.AddImage("/uploads/products/f001.png", 0);
            product.SetThumbnail("/uploads/products/f001-thumbnail.png");
            product.SetVisibilityOnFE(true);
            var hiddenProduct = new Product("H001", 250_000, 0, category.Id, productType.Id, false);
            hiddenProduct.AddTranslation("vi", "Sản phẩm nội bộ", "san-pham-noi-bo", string.Empty);
            dbContext.Products.AddRange(product, hiddenProduct);
            await dbContext.SaveChangesAsync();

            var fileReader = new ToggleFileReader();
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero));
            var service = new FeDataExportService(
                dbContext,
                fileReader,
                new NoOpAuditWriter(),
                clock,
                Options.Create(new FeDataExportOptions
                {
                    AllowedRoot = Path.Combine(root, "public"),
                    TargetDirectory = target
                }),
                new TestWebHostEnvironment(root));

            var first = await service.ExportAsync(CancellationToken.None);
            var firstManifest = await File.ReadAllTextAsync(Path.Combine(target, "manifest.json"));
            var firstProducts = await File.ReadAllTextAsync(Path.Combine(target, "products.json"));
            var firstImageNames = Directory.GetFiles(Path.Combine(target, "images", "products"))
                .Select(Path.GetFileName)
                .ToArray();
            var staleGenerated = Path.Combine(target, "stale-generated.txt");
            var manualSibling = Path.Combine(root, "public", "manual-asset.txt");
            await File.WriteAllTextAsync(staleGenerated, "stale");
            await File.WriteAllTextAsync(manualSibling, "manual");

            var second = await service.ExportAsync(CancellationToken.None);
            var secondManifest = await File.ReadAllTextAsync(Path.Combine(target, "manifest.json"));
            var secondProducts = await File.ReadAllTextAsync(Path.Combine(target, "products.json"));
            var secondImageNames = Directory.GetFiles(Path.Combine(target, "images", "products"))
                .Select(Path.GetFileName)
                .ToArray();

            Assert.Equal(1, first.ProductCount);
            Assert.Equal(2, first.ImageCount);
            Assert.Equal(first.CatalogVersion, second.CatalogVersion);
            Assert.Equal(firstManifest, secondManifest);
            Assert.Equal(firstProducts, secondProducts);
            Assert.Equal(firstImageNames, secondImageNames);
            Assert.Contains("\"slug\": \"doa-hong\"", firstProducts, StringComparison.Ordinal);
            Assert.Contains("\"schemaVersion\": \"1.1\"", firstProducts, StringComparison.Ordinal);
            Assert.Contains("\"priceDeviationPercent\": 20", firstProducts, StringComparison.Ordinal);
            Assert.Contains("\"productLine\":", firstProducts, StringComparison.Ordinal);
            Assert.Contains("\"similarProductIds\": []", firstProducts, StringComparison.Ordinal);
            Assert.Contains("\"sortOrder\": 0", firstProducts, StringComparison.Ordinal);
            Assert.Contains("\"sortOrder\": 1", firstProducts, StringComparison.Ordinal);
            Assert.DoesNotContain("H001", firstProducts, StringComparison.Ordinal);
            Assert.False(File.Exists(staleGenerated));
            Assert.True(File.Exists(manualSibling));
            Assert.DoesNotContain("ingredient", firstProducts, StringComparison.OrdinalIgnoreCase);

            clock.Value = clock.Value.AddMinutes(1);
            var third = await service.ExportAsync(CancellationToken.None);
            var publishedManifest = await File.ReadAllTextAsync(Path.Combine(target, "manifest.json"));
            var publishedProducts = await File.ReadAllTextAsync(Path.Combine(target, "products.json"));
            Assert.NotEqual(second.CatalogVersion, third.CatalogVersion);
            Assert.NotEqual(secondProducts, publishedProducts);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(publishedProducts))).ToLowerInvariant(),
                third.CatalogVersion);

            var duplicate = new Product("F002", 300_000, 0, category.Id, productType.Id, false);
            duplicate.AddTranslation("vi", "Đóa hồng khác", "doa-hong", string.Empty);
            duplicate.SetVisibilityOnFE(true);
            dbContext.Products.Add(duplicate);
            await dbContext.SaveChangesAsync();

            var slugFailure = await Assert.ThrowsAsync<FeDataExportException>(() =>
                service.ExportAsync(CancellationToken.None));
            Assert.Contains(slugFailure.Issues, issue =>
                issue.Contains("slug 'doa-hong'", StringComparison.Ordinal));
            Assert.Equal(publishedManifest, await File.ReadAllTextAsync(Path.Combine(target, "manifest.json")));
            duplicate.SetVisibilityOnFE(false);
            await dbContext.SaveChangesAsync();

            fileReader.IsAvailable = false;
            var failure = await Assert.ThrowsAsync<FeDataExportException>(() =>
                service.ExportAsync(CancellationToken.None));

            Assert.Equal(2, failure.Issues.Count);
            Assert.Equal(publishedManifest, await File.ReadAllTextAsync(Path.Combine(target, "manifest.json")));
            Assert.Equal(publishedProducts, await File.ReadAllTextAsync(Path.Combine(target, "products.json")));
            Assert.DoesNotContain(
                Directory.EnumerateDirectories(Path.GetDirectoryName(target)!),
                path => Path.GetFileName(path).StartsWith(".fe-data.", StringComparison.Ordinal));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ToggleFileReader : IPublicFileReader
    {
        public bool IsAvailable { get; set; } = true;

        public Task<StoredPublicFile?> ReadPublicAsync(
            string? publicUrl,
            CancellationToken cancellationToken = default)
        {
            StoredPublicFile? result = IsAvailable
                ? new StoredPublicFile(
                    [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01],
                    "image/png",
                    "f001.png")
                : null;
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpAuditWriter : IAccessAuditWriter
    {
        public void Record(string action, string entityType, string entityId, object? before, object? after)
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = value;

        public override DateTimeOffset GetUtcNow() => Value;
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Lamie.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
