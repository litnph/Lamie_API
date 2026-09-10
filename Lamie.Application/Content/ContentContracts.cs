using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Entities;

namespace Lamie.Application.Content;

public sealed record ContentUpload(
    string FileName,
    string ContentType,
    byte[] Bytes);

public sealed record GenerateContentRequest(
    int? ProductId,
    string? Brief,
    IReadOnlyList<ContentUpload> Images,
    string? IdempotencyKey);

public sealed record ContentAiProductContext(
    int Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    decimal? SalePrice,
    string? CategoryName);

public sealed record ContentAiImage(
    string FileName,
    string ContentType,
    byte[] Bytes);

public sealed record ContentAiRequest(
    ContentAiProductContext? Product,
    string? Brief,
    string StyleId,
    string StyleName,
    int StyleSeed,
    IReadOnlyList<ContentAiImage> Images);

public sealed record ContentAiResult(
    string Facebook,
    string Instagram,
    string TikTok,
    string Provider,
    string Model,
    string PromptVersion);

public interface IContentAiProvider
{
    bool IsConfigured { get; }
    string Model { get; }

    Task<ContentAiResult> GenerateAsync(
        ContentAiRequest request,
        CancellationToken cancellationToken);
}

public sealed record ContentFooterSettingDto(
    Guid? Id,
    ContentPlatform Platform,
    string? Content,
    string? Hashtags,
    bool IsActive,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateContentFooterRequest(
    string? Content,
    string? Hashtags,
    bool IsActive);

public sealed record ContentItemDto(
    Guid Id,
    ContentPlatform Platform,
    string Body,
    string? FooterSnapshot,
    string? HashtagsSnapshot,
    string FullContent);

public sealed record ContentAssetDto(
    Guid Id,
    string Url,
    string FileName,
    string ContentType,
    int SortOrder);

public sealed record ContentGenerationDto(
    Guid Id,
    ContentSourceType SourceType,
    int? ProductId,
    string? ProductNameSnapshot,
    string? ProductImageUrlSnapshot,
    string? Brief,
    string StyleId,
    string StyleName,
    int StyleSeed,
    ContentGenerationStatus Status,
    string Provider,
    string Model,
    string PromptVersion,
    Guid? ParentGenerationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SavedAt,
    IReadOnlyList<ContentItemDto> Items,
    IReadOnlyList<ContentAssetDto> Assets);

public sealed record SaveContentItemRequest(
    ContentPlatform Platform,
    string Body);

public sealed record SaveContentRequest(
    IReadOnlyList<SaveContentItemRequest> Items);

public sealed class ContentHistoryQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int? ProductId { get; init; }
    public ContentPlatform? Platform { get; init; }
    public ContentGenerationStatus? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record ContentHistoryItemDto(
    Guid Id,
    ContentSourceType SourceType,
    int? ProductId,
    string? ProductNameSnapshot,
    string StyleName,
    ContentGenerationStatus Status,
    IReadOnlyList<ContentPlatform> Platforms,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SavedAt,
    Guid? ParentGenerationId);

public sealed record PagedContentHistoryDto(
    IReadOnlyList<ContentHistoryItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasNext,
    bool HasPrevious);

public sealed record ContentConfigurationDto(
    bool IsAiConfigured,
    string Model,
    IReadOnlyList<ContentStyleDto> Styles);

public sealed record ContentStyleDto(string Id, string Name);

public interface IContentPublishingService
{
    ContentConfigurationDto GetConfiguration();

    Task<IReadOnlyList<ContentFooterSettingDto>> GetFootersAsync(
        CancellationToken cancellationToken);

    Task<ContentFooterSettingDto> UpsertFooterAsync(
        ContentPlatform platform,
        UpdateContentFooterRequest request,
        CancellationToken cancellationToken);

    Task<ContentGenerationDto> GenerateAsync(
        GenerateContentRequest request,
        CancellationToken cancellationToken);

    Task<ContentGenerationDto> SaveAsync(
        Guid generationId,
        SaveContentRequest request,
        CancellationToken cancellationToken);

    Task<ContentGenerationDto> GetAsync(
        Guid generationId,
        CancellationToken cancellationToken);

    Task<PagedContentHistoryDto> GetHistoryAsync(
        ContentHistoryQuery query,
        CancellationToken cancellationToken);
}

public sealed class ContentAiNotConfiguredException()
    : BaseException(
        "OpenAI content generation is not configured. Set the server-side API key and try again.",
        "AI_NOT_CONFIGURED");

public sealed class ContentAiProviderException(string message, bool isTemporary = false)
    : BaseException(message, isTemporary ? "AI_PROVIDER_TEMPORARY_ERROR" : "AI_PROVIDER_ERROR")
{
    public bool IsTemporary { get; } = isTemporary;
}
