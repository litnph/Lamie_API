using System.Security.Claims;
using Lamie.API.Options;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Content;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lamie.Tests.Content;

public sealed class ContentPublishingServiceIntegrationTests
{
    [Fact]
    public async Task GenerateSaveAndEdit_PreserveSnapshotsAndCreateImmutableVersion()
    {
        var databaseName = $"LamieContent_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            Assert.True(dbContext.Model
                .FindEntityType(typeof(ContentGeneration))!
                .FindProperty(nameof(ContentGeneration.Status))!
                .IsConcurrencyToken);
            var category = new Category(10);
            category.AddOrUpdateTranslation("vi", "Hoa bó", null);
            var productType = new ProductType("BOUQUET", 10);
            productType.AddOrUpdateTranslation("vi", "Bó hoa", null);
            dbContext.AddRange(category, productType);
            await dbContext.SaveChangesAsync();

            var product = new Product("T001", 450_000, 0, category.Id, productType.Id, false);
            product.AddTranslation("vi", "Hoa hồng Lamie", "hoa-hong-lamie", "Một bó hoa dịu dàng.");
            product.SetThumbnail("/uploads/products/source.png");
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();

            var actorId = Guid.NewGuid();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", actorId.ToString())],
                    "test"))
            };
            var provider = new FakeContentAiProvider();
            var storage = new TrackingFileStorage();
            var clock = new MutableTimeProvider(
                new DateTimeOffset(2026, 8, 16, 2, 0, 0, TimeSpan.Zero));
            var service = new ContentPublishingService(
                dbContext,
                provider,
                storage,
                new StaticFileReader(),
                new HttpContextAccessor { HttpContext = httpContext },
                new NoOpAuditWriter(),
                clock,
                Options.Create(new OpenAIContentOptions()));

            await service.UpsertFooterAsync(
                ContentPlatform.Facebook,
                new UpdateContentFooterRequest("Footer cũ", "#lamie", true),
                CancellationToken.None);
            var draft = await service.GenerateAsync(
                new GenerateContentRequest(product.Id, "Quà sinh nhật", [], "request-1"),
                CancellationToken.None);
            var repeated = await service.GenerateAsync(
                new GenerateContentRequest(product.Id, "Quà sinh nhật", [], "request-1"),
                CancellationToken.None);

            Assert.Equal(ContentGenerationStatus.Draft, draft.Status);
            Assert.Equal(draft.Id, repeated.Id);
            Assert.Equal(1, provider.CallCount);
            Assert.Single(provider.LastRequest!.Images);
            Assert.NotEqual("/uploads/products/source.png", draft.ProductImageUrlSnapshot);
            Assert.StartsWith("/uploads/content/", draft.ProductImageUrlSnapshot, StringComparison.Ordinal);
            Assert.Single(draft.Assets);
            Assert.Single(storage.Paths);
            var durableImageUrl = draft.ProductImageUrlSnapshot!;
            product.SetThumbnail(null);
            await dbContext.SaveChangesAsync();
            var afterProductImageChange = await service.GetAsync(draft.Id, CancellationToken.None);
            Assert.Equal(durableImageUrl, afterProductImageChange.ProductImageUrlSnapshot);
            Assert.Contains(durableImageUrl, storage.Paths);
            Assert.Equal(3, draft.Items.Count);
            Assert.Equal("Footer cũ", draft.Items.Single(item => item.Platform == ContentPlatform.Facebook).FooterSnapshot);
            await Assert.ThrowsAsync<ConflictException>(() => service.GenerateAsync(
                new GenerateContentRequest(product.Id, "Brief khác", [], "request-1"),
                CancellationToken.None));
            Assert.Equal(1, provider.CallCount);

            await service.UpsertFooterAsync(
                ContentPlatform.Facebook,
                new UpdateContentFooterRequest("Footer mới", "#new", true),
                CancellationToken.None);
            clock.Value = new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero);
            var firstSave = new SaveContentRequest(draft.Items
                .Select(item => new SaveContentItemRequest(item.Platform, item.Body + " – đã duyệt"))
                .ToList());
            var saved = await service.SaveAsync(draft.Id, firstSave, CancellationToken.None);
            var repeatedSave = await service.SaveAsync(draft.Id, firstSave, CancellationToken.None);

            Assert.Equal(ContentGenerationStatus.Saved, saved.Status);
            Assert.Equal(saved.Id, repeatedSave.Id);
            var savedFacebook = saved.Items.Single(item => item.Platform == ContentPlatform.Facebook);
            Assert.Equal("Footer cũ", savedFacebook.FooterSnapshot);
            Assert.Contains("Footer cũ", savedFacebook.FullContent);
            Assert.DoesNotContain("Footer mới", savedFacebook.FullContent);

            var editedSave = new SaveContentRequest(saved.Items
                .Select(item => new SaveContentItemRequest(
                    item.Platform,
                    item.Platform == ContentPlatform.Instagram ? item.Body + " bản 2" : item.Body))
                .ToList());
            var version = await service.SaveAsync(saved.Id, editedSave, CancellationToken.None);
            var repeatedVersion = await service.SaveAsync(saved.Id, editedSave, CancellationToken.None);
            var original = await service.GetAsync(saved.Id, CancellationToken.None);

            Assert.NotEqual(saved.Id, version.Id);
            Assert.Equal(version.Id, repeatedVersion.Id);
            Assert.Equal(saved.Id, version.ParentGenerationId);
            Assert.Equal(ContentGenerationStatus.Saved, version.Status);
            Assert.DoesNotContain("bản 2", original.Items.Single(item => item.Platform == ContentPlatform.Instagram).Body);
            Assert.Contains("bản 2", version.Items.Single(item => item.Platform == ContentPlatform.Instagram).Body);

            var history = await service.GetHistoryAsync(
                new ContentHistoryQuery
                {
                    ProductId = product.Id,
                    Status = ContentGenerationStatus.Saved,
                    From = new DateOnly(2026, 8, 17),
                    To = new DateOnly(2026, 8, 17),
                    Page = 1,
                    PageSize = 20
                },
                CancellationToken.None);
            Assert.Equal(2, history.TotalCount);
            Assert.All(history.Items, item => Assert.Equal("Hoa hồng Lamie", item.ProductNameSnapshot));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task ConcurrentGenerate_WithSameKey_ChargesProviderOnceAndReturnsSameGeneration()
    {
        var databaseName = $"LamieContentRace_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        try
        {
            await using (var seed = new AppDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync();
                var category = new Category(10);
                category.AddOrUpdateTranslation("vi", "Hoa bó", null);
                var productType = new ProductType("BOUQUET", 10);
                productType.AddOrUpdateTranslation("vi", "Bó hoa", null);
                seed.AddRange(category, productType);
                await seed.SaveChangesAsync();
                var product = new Product("R001", 100_000, 0, category.Id, productType.Id, false);
                product.AddTranslation("vi", "Hoa race", "hoa-race", string.Empty);
                seed.Products.Add(product);
                await seed.SaveChangesAsync();
            }

            await using var firstContext = new AppDbContext(options);
            await using var secondContext = new AppDbContext(options);
            var productId = await firstContext.Products.Select(item => item.Id).SingleAsync();
            var actorId = Guid.NewGuid();
            var provider = new DelayedContentAiProvider();
            var firstService = CreateService(firstContext, provider, actorId);
            var secondService = CreateService(secondContext, provider, actorId);
            var request = new GenerateContentRequest(productId, "Cùng brief", [], "race-key");

            var firstTask = firstService.GenerateAsync(request, CancellationToken.None);
            var secondTask = secondService.GenerateAsync(request, CancellationToken.None);
            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Equal(results[0].Id, results[1].Id);
            Assert.Equal(1, provider.CallCount);
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static ContentPublishingService CreateService(
        AppDbContext dbContext,
        IContentAiProvider provider,
        Guid actorId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", actorId.ToString())],
                "test"))
        };
        return new ContentPublishingService(
            dbContext,
            provider,
            new TrackingFileStorage(),
            new StaticFileReader(),
            new HttpContextAccessor { HttpContext = httpContext },
            new NoOpAuditWriter(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 17, 2, 0, 0, TimeSpan.Zero)),
            Options.Create(new OpenAIContentOptions()));
    }

    private sealed class FakeContentAiProvider : IContentAiProvider
    {
        public int CallCount { get; private set; }
        public ContentAiRequest? LastRequest { get; private set; }
        public bool IsConfigured => true;
        public string Model => "fake-model";

        public Task<ContentAiResult> GenerateAsync(
            ContentAiRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new ContentAiResult(
                "Facebook riêng",
                "Instagram riêng",
                "TikTok riêng",
                "fake",
                Model,
                "test-v1"));
        }
    }

    private sealed class DelayedContentAiProvider : IContentAiProvider
    {
        private int _callCount;

        public int CallCount => _callCount;
        public bool IsConfigured => true;
        public string Model => "fake-model";

        public async Task<ContentAiResult> GenerateAsync(
            ContentAiRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(200, cancellationToken);
            return new ContentAiResult("Facebook", "Instagram", "TikTok", "fake", Model, "test-v1");
        }
    }

    private sealed class TrackingFileStorage : IFileStorage
    {
        public HashSet<string> Paths { get; } = new(StringComparer.Ordinal);

        public async Task<string> UploadPublicAsync(
            Stream content,
            string objectPath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            await content.CopyToAsync(Stream.Null, cancellationToken);
            var url = "/uploads/" + objectPath.Replace('\\', '/');
            Paths.Add(url);
            return url;
        }

        public Task DeleteAsync(string? publicUrl, CancellationToken cancellationToken = default)
        {
            if (publicUrl is not null)
                Paths.Remove(publicUrl);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticFileReader : IPublicFileReader
    {
        public Task<StoredPublicFile?> ReadPublicAsync(
            string? publicUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredPublicFile?>(new StoredPublicFile(
                [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01],
                "image/png",
                "source.png"));
    }

    private sealed class NoOpAuditWriter : IAccessAuditWriter
    {
        public void Record(string action, string entityType, string entityId, object? before, object? after)
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = value;

        public override DateTimeOffset GetUtcNow() => Value;
    }
}
