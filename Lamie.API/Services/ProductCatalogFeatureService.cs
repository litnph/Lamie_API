using Lamie.API.Options;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Identity;
using Lamie.Application.Settings.Products;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public sealed class ProductCatalogFeatureService : IProductCatalogFeatureService
{
    private const int MaximumResults = 5;
    private static readonly SemaphoreSlim RecognitionGate = new(4, 4);
    private static readonly SemaphoreSlim BackfillGate = new(1, 1);

    private readonly AppDbContext _dbContext;
    private readonly IPublicFileReader _fileReader;
    private readonly IProductVisualEmbeddingProvider _embeddingProvider;
    private readonly IAccessAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ProductRecognitionOptions _options;
    private readonly ILogger<ProductCatalogFeatureService> _logger;

    public ProductCatalogFeatureService(
        AppDbContext dbContext,
        IPublicFileReader fileReader,
        IProductVisualEmbeddingProvider embeddingProvider,
        IAccessAuditWriter auditWriter,
        TimeProvider timeProvider,
        IOptions<ProductRecognitionOptions> options,
        ILogger<ProductCatalogFeatureService> logger)
    {
        _dbContext = dbContext;
        _fileReader = fileReader;
        _embeddingProvider = embeddingProvider;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProductCatalogSettingsDto> GetSettingsAsync(
        CancellationToken cancellationToken)
    {
        var setting = await _dbContext.ProductCatalogSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == ProductCatalogSettings.SingletonId,
                cancellationToken);
        return setting is null
            ? new ProductCatalogSettingsDto(
                ProductCatalogSettings.DefaultPriceDeviationPercent,
                null)
            : ToSettingsDto(setting);
    }

    public async Task<ProductCatalogSettingsDto> UpdateSettingsAsync(
        UpdateProductCatalogSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PriceDeviationPercent is < 0 or > ProductCatalogSettings.MaximumPriceDeviationPercent)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["priceDeviationPercent"] = ["Price deviation percent must be between 0 and 100."]
            });
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var setting = await _dbContext.ProductCatalogSettings.SingleOrDefaultAsync(
            item => item.Id == ProductCatalogSettings.SingletonId,
            cancellationToken);
        var before = setting is null ? null : ToSettingsDto(setting);
        if (setting is null)
        {
            setting = new ProductCatalogSettings(request.PriceDeviationPercent, now);
            _dbContext.ProductCatalogSettings.Add(setting);
        }
        else
        {
            setting.UpdatePriceDeviation(request.PriceDeviationPercent, now);
        }

        var after = ToSettingsDto(setting);
        _auditWriter.Record(
            "product-catalog.settings.update",
            nameof(ProductCatalogSettings),
            ProductCatalogSettings.SingletonId.ToString(),
            before,
            after);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return after;
    }

    public async Task<ProductRecognitionResponseDto> RecognizeAsync(
        ProductRecognitionUpload upload,
        CancellationToken cancellationToken)
    {
        if (!await RecognitionGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
            throw new ConflictException("Product recognition is busy. Please retry shortly.");

        try
        {
            await using var source = new MemoryStream(upload.Bytes, writable: false);
            var queryEmbedding = await _embeddingProvider.CreateAsync(source, cancellationToken);
            var products = await _dbContext.Products
                .AsNoTracking()
                .Where(product => product.IsActive)
                .Include(product => product.Translations)
                .Include(product => product.Images)
                .AsSplitQuery()
                .OrderBy(product => product.Id)
                .ToListAsync(cancellationToken);

            var candidates = products
                .Select(product => new
                {
                    Product = product,
                    Score = ProductScore(queryEmbedding, product)
                })
                .Where(item => item.Score >= (double)_options.MinimumSimilarity)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Product.Id)
                .Take(MaximumResults)
                .Select(item => new ProductRecognitionResultDto(
                    item.Product.Id,
                    item.Product.Sku,
                    PreferredProductName(item.Product),
                    PreferredImageUrl(item.Product),
                    decimal.Round((decimal)item.Score * 100m, 1, MidpointRounding.AwayFromZero)))
                .ToList();

            return new ProductRecognitionResponseDto(
                candidates,
                products.Count(HasCurrentEmbedding),
                products.Count,
                decimal.Round(_options.MinimumSimilarity * 100m, 1),
                _embeddingProvider.Version);
        }
        finally
        {
            RecognitionGate.Release();
        }
    }

    public async Task<ProductRecognitionIndexStatusDto> GetRecognitionStatusAsync(
        CancellationToken cancellationToken)
    {
        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .Include(product => product.Images)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new ProductRecognitionIndexStatusDto(
            products.Count(HasCurrentEmbedding),
            products.Count,
            PendingImageCount(products),
            _embeddingProvider.Version);
    }

    public async Task<ProductRecognitionBackfillResultDto> BackfillRecognitionAsync(
        int? requestedBatchSize,
        CancellationToken cancellationToken)
    {
        if (!await BackfillGate.WaitAsync(0, cancellationToken))
            throw new ConflictException("A product recognition backfill is already running.");

        try
        {
            var batchSize = Math.Clamp(requestedBatchSize ?? _options.BackfillBatchSize, 1, 100);
            var products = await _dbContext.Products
                .Where(product => product.IsActive
                    && ((product.ThumbnailUrl != null
                            && (product.ThumbnailVisualEmbedding == null
                                || product.ThumbnailVisualEmbeddingVersion != _embeddingProvider.Version))
                        || product.Images.Any(image => image.IsActive
                            && (image.VisualEmbedding == null
                                || image.VisualEmbeddingVersion != _embeddingProvider.Version))))
                .Include(product => product.Images)
                .AsSplitQuery()
                .OrderBy(product => product.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var processed = 0;
            var failed = 0;
            var warnings = new List<string>();
            foreach (var product in products)
            {
                if (!string.IsNullOrWhiteSpace(product.ThumbnailUrl)
                    && !HasCurrentEmbedding(
                        product.ThumbnailVisualEmbedding,
                        product.ThumbnailVisualEmbeddingVersion))
                {
                    var embedding = await TryCreateStoredEmbeddingAsync(
                        product.ThumbnailUrl,
                        product.Id,
                        "thumbnail",
                        warnings,
                        cancellationToken);
                    if (embedding is null)
                        failed++;
                    else
                    {
                        product.SetThumbnail(
                            product.ThumbnailUrl,
                            embedding,
                            _embeddingProvider.Version);
                        processed++;
                    }
                }

                foreach (var image in product.Images.Where(image => image.IsActive
                    && !HasCurrentEmbedding(image.VisualEmbedding, image.VisualEmbeddingVersion)))
                {
                    var embedding = await TryCreateStoredEmbeddingAsync(
                        image.ImageUrl,
                        product.Id,
                        $"image {image.Id}",
                        warnings,
                        cancellationToken);
                    if (embedding is null)
                        failed++;
                    else
                    {
                        image.SetVisualEmbedding(embedding, _embeddingProvider.Version);
                        processed++;
                    }
                }
            }

            if (processed > 0)
                await _dbContext.SaveChangesAsync(cancellationToken);

            var remainingProducts = await _dbContext.Products
                .AsNoTracking()
                .Where(product => product.IsActive)
                .Include(product => product.Images)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            return new ProductRecognitionBackfillResultDto(
                processed,
                failed,
                PendingImageCount(remainingProducts),
                warnings.Take(20).ToList());
        }
        finally
        {
            BackfillGate.Release();
        }
    }

    private async Task<byte[]?> TryCreateStoredEmbeddingAsync(
        string imageUrl,
        int productId,
        string sourceLabel,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var stored = await _fileReader.ReadPublicAsync(imageUrl, cancellationToken);
            if (stored is null)
                throw new InvalidDataException("stored file is unavailable");
            await using var source = new MemoryStream(stored.Bytes, writable: false);
            return await _embeddingProvider.CreateAsync(source, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not create visual embedding for product {ProductId} {SourceLabel}",
                productId,
                sourceLabel);
            warnings.Add($"Sản phẩm {productId}, {sourceLabel}: không thể đọc hoặc phân tích ảnh.");
            return null;
        }
    }

    private double ProductScore(byte[] queryEmbedding, Product product)
    {
        var embeddings = product.Images
            .Where(image => image.IsActive
                && HasCurrentEmbedding(image.VisualEmbedding, image.VisualEmbeddingVersion))
            .Select(image => image.VisualEmbedding!)
            .ToList();
        if (HasCurrentEmbedding(
                product.ThumbnailVisualEmbedding,
                product.ThumbnailVisualEmbeddingVersion))
        {
            embeddings.Add(product.ThumbnailVisualEmbedding!);
        }

        return embeddings.Count == 0
            ? 0
            : embeddings.Max(embedding => _embeddingProvider.CosineSimilarity(queryEmbedding, embedding));
    }

    private bool HasCurrentEmbedding(Product product) =>
        HasCurrentEmbedding(
            product.ThumbnailVisualEmbedding,
            product.ThumbnailVisualEmbeddingVersion)
        || product.Images.Any(image => image.IsActive
            && HasCurrentEmbedding(image.VisualEmbedding, image.VisualEmbeddingVersion));

    private bool HasCurrentEmbedding(byte[]? embedding, string? version) =>
        embedding is { Length: > 0 }
        && string.Equals(version, _embeddingProvider.Version, StringComparison.Ordinal);

    private int PendingImageCount(IEnumerable<Product> products) => products.Sum(product =>
        (!string.IsNullOrWhiteSpace(product.ThumbnailUrl)
            && !HasCurrentEmbedding(
                product.ThumbnailVisualEmbedding,
                product.ThumbnailVisualEmbeddingVersion) ? 1 : 0)
        + product.Images.Count(image => image.IsActive
            && !HasCurrentEmbedding(image.VisualEmbedding, image.VisualEmbeddingVersion)));

    private static string PreferredProductName(Product product) =>
        product.Translations
            .OrderBy(item => string.Equals(item.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => string.Equals(item.LanguageCode, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.LanguageCode)
            .Select(item => item.Name)
            .FirstOrDefault() ?? product.Sku;

    private static string? PreferredImageUrl(Product product) =>
        product.ThumbnailUrl
        ?? product.Images
            .Where(image => image.IsActive)
            .OrderBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .Select(image => image.ImageUrl)
            .FirstOrDefault();

    private static ProductCatalogSettingsDto ToSettingsDto(ProductCatalogSettings setting) =>
        new(
            setting.PriceDeviationPercent,
            new DateTimeOffset(DateTime.SpecifyKind(setting.UpdatedAt, DateTimeKind.Utc)));
}
