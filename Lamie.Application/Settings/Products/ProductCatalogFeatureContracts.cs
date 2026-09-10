namespace Lamie.Application.Settings.Products;

public sealed record ProductCatalogSettingsDto(
    decimal PriceDeviationPercent,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateProductCatalogSettingsRequest(decimal PriceDeviationPercent);

public sealed record ProductRecognitionUpload(
    string FileName,
    string ContentType,
    byte[] Bytes);

public sealed record ProductRecognitionResultDto(
    int ProductId,
    string Sku,
    string Name,
    string? ThumbnailUrl,
    decimal SimilarityPercent);

public sealed record ProductRecognitionResponseDto(
    IReadOnlyList<ProductRecognitionResultDto> Results,
    int IndexedProductCount,
    int ActiveProductCount,
    decimal MinimumSimilarityPercent,
    string EmbeddingVersion);

public sealed record ProductRecognitionIndexStatusDto(
    int IndexedProductCount,
    int ActiveProductCount,
    int PendingImageCount,
    string EmbeddingVersion);

public sealed record ProductRecognitionBackfillResultDto(
    int ProcessedImageCount,
    int FailedImageCount,
    int RemainingImageCount,
    IReadOnlyList<string> Warnings);

public interface IProductCatalogFeatureService
{
    Task<ProductCatalogSettingsDto> GetSettingsAsync(CancellationToken cancellationToken);

    Task<ProductCatalogSettingsDto> UpdateSettingsAsync(
        UpdateProductCatalogSettingsRequest request,
        CancellationToken cancellationToken);

    Task<ProductRecognitionResponseDto> RecognizeAsync(
        ProductRecognitionUpload upload,
        CancellationToken cancellationToken);

    Task<ProductRecognitionIndexStatusDto> GetRecognitionStatusAsync(
        CancellationToken cancellationToken);

    Task<ProductRecognitionBackfillResultDto> BackfillRecognitionAsync(
        int? requestedBatchSize,
        CancellationToken cancellationToken);
}
