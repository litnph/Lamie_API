using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public enum ContentPlatform
{
    Facebook = 1,
    Instagram = 2,
    TikTok = 3
}

public enum ContentSourceType
{
    Product = 1,
    UploadedImages = 2
}

public enum ContentGenerationStatus
{
    Draft = 1,
    Saved = 2,
    Deleted = 99
}

public sealed class ContentFooterSetting
{
    private ContentFooterSetting()
    {
    }

    public ContentFooterSetting(
        ContentPlatform platform,
        string? content,
        string? hashtags,
        bool isActive,
        DateTime nowUtc,
        Guid? actorUserId)
    {
        Id = Guid.NewGuid();
        Platform = ValidatePlatform(platform);
        CreatedAt = nowUtc;
        CreatedBy = actorUserId;
        Update(content, hashtags, isActive, nowUtc, actorUserId);
    }

    public Guid Id { get; private set; }
    public ContentPlatform Platform { get; private set; }
    public string? Content { get; private set; }
    public string? Hashtags { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Update(
        string? content,
        string? hashtags,
        bool isActive,
        DateTime nowUtc,
        Guid? actorUserId)
    {
        Content = NormalizeOptional(content, 2000, "Footer content");
        Hashtags = NormalizeOptional(hashtags, 1000, "Footer hashtags");
        IsActive = isActive;
        UpdatedAt = nowUtc;
        UpdatedBy = actorUserId;
    }

    private static ContentPlatform ValidatePlatform(ContentPlatform platform) =>
        Enum.IsDefined(platform)
            ? platform
            : throw new DomainException("Content platform is invalid.");

    internal static string? NormalizeOptional(string? value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters.");
        return normalized;
    }
}

public sealed record ContentItemSeed(
    ContentPlatform Platform,
    string Body,
    string? FooterSnapshot,
    string? HashtagsSnapshot);

public sealed record ContentAssetSeed(
    string PublicUrl,
    string FileName,
    string ContentType,
    int SortOrder);

public sealed class ContentGeneration
{
    private readonly List<ContentItem> _items = [];
    private readonly List<ContentAsset> _assets = [];

    private ContentGeneration()
    {
    }

    public ContentGeneration(
        ContentSourceType sourceType,
        int? productId,
        string? productNameSnapshot,
        string? productImageUrlSnapshot,
        string? brief,
        string styleId,
        string styleName,
        int styleSeed,
        string provider,
        string model,
        string promptVersion,
        string? idempotencyKey,
        string? requestFingerprint,
        Guid? parentGenerationId,
        Guid? createdBy,
        DateTime nowUtc,
        IEnumerable<ContentItemSeed> items,
        IEnumerable<ContentAssetSeed>? assets = null,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        SourceType = ValidateSource(sourceType, productId);
        ProductId = productId;
        ProductNameSnapshot = ContentFooterSetting.NormalizeOptional(
            productNameSnapshot,
            300,
            "Product name snapshot");
        ProductImageUrlSnapshot = ContentFooterSetting.NormalizeOptional(
            productImageUrlSnapshot,
            2048,
            "Product image URL snapshot");
        Brief = ContentFooterSetting.NormalizeOptional(brief, 4000, "Content brief");
        StyleId = Required(styleId, 80, "Style id");
        StyleName = Required(styleName, 160, "Style name");
        StyleSeed = styleSeed;
        Provider = Required(provider, 80, "Content provider");
        Model = Required(model, 120, "Content model");
        PromptVersion = Required(promptVersion, 80, "Prompt version");
        IdempotencyKey = ContentFooterSetting.NormalizeOptional(
            idempotencyKey,
            120,
            "Idempotency key");
        RequestFingerprint = ContentFooterSetting.NormalizeOptional(
            requestFingerprint,
            64,
            "Request fingerprint");
        if (IdempotencyKey is not null && RequestFingerprint is null)
            throw new DomainException("An idempotent content generation requires a request fingerprint.");
        ParentGenerationId = parentGenerationId;
        CreatedBy = createdBy;
        CreatedAt = nowUtc;
        UpdatedAt = nowUtc;
        Status = ContentGenerationStatus.Draft;

        var itemList = items?.ToList() ?? [];
        var expectedPlatforms = Enum.GetValues<ContentPlatform>();
        if (itemList.Count != expectedPlatforms.Length
            || itemList.Select(item => item.Platform).Distinct().Count() != expectedPlatforms.Length
            || expectedPlatforms.Any(platform => itemList.All(item => item.Platform != platform)))
        {
            throw new DomainException("A content generation must contain exactly one item for every platform.");
        }

        foreach (var item in itemList.OrderBy(item => item.Platform))
            _items.Add(new ContentItem(Id, item, nowUtc));

        foreach (var asset in assets?.OrderBy(asset => asset.SortOrder)
                 ?? Enumerable.Empty<ContentAssetSeed>())
            _assets.Add(new ContentAsset(Id, asset, nowUtc));

        if (SourceType == ContentSourceType.UploadedImages && _assets.Count == 0)
            throw new DomainException("At least one uploaded image is required.");
    }

    public Guid Id { get; private set; }
    public ContentSourceType SourceType { get; private set; }
    public int? ProductId { get; private set; }
    public string? ProductNameSnapshot { get; private set; }
    public string? ProductImageUrlSnapshot { get; private set; }
    public string? Brief { get; private set; }
    public string StyleId { get; private set; } = string.Empty;
    public string StyleName { get; private set; } = string.Empty;
    public int StyleSeed { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public string? IdempotencyKey { get; private set; }
    public string? RequestFingerprint { get; private set; }
    public Guid? ParentGenerationId { get; private set; }
    public ContentGenerationStatus Status { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? SavedAt { get; private set; }
    public IReadOnlyCollection<ContentItem> Items => _items;
    public IReadOnlyCollection<ContentAsset> Assets => _assets;

    public void Save(
        IReadOnlyDictionary<ContentPlatform, string> editedBodies,
        DateTime nowUtc)
    {
        if (Status != ContentGenerationStatus.Draft)
            throw new DomainException("Only a draft content generation can be saved.");
        if (editedBodies.Count != _items.Count
            || _items.Any(item => !editedBodies.ContainsKey(item.Platform)))
        {
            throw new DomainException("Saved content must include every generated platform.");
        }

        foreach (var item in _items)
            item.UpdateBody(editedBodies[item.Platform], nowUtc);

        Status = ContentGenerationStatus.Saved;
        SavedAt = nowUtc;
        UpdatedAt = nowUtc;
    }

    public void Delete(DateTime nowUtc)
    {
        Status = ContentGenerationStatus.Deleted;
        UpdatedAt = nowUtc;
    }

    private static ContentSourceType ValidateSource(ContentSourceType sourceType, int? productId)
    {
        if (!Enum.IsDefined(sourceType))
            throw new DomainException("Content source is invalid.");
        if (sourceType == ContentSourceType.Product && (!productId.HasValue || productId <= 0))
            throw new DomainException("A product source requires a product.");
        if (sourceType == ContentSourceType.UploadedImages && productId.HasValue)
            throw new DomainException("An uploaded-image source cannot reference a product.");
        return sourceType;
    }

    private static string Required(string? value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters.");
        return normalized;
    }
}

public sealed class ContentItem
{
    private ContentItem()
    {
    }

    internal ContentItem(Guid generationId, ContentItemSeed seed, DateTime nowUtc)
    {
        if (!Enum.IsDefined(seed.Platform))
            throw new DomainException("Content platform is invalid.");
        Id = Guid.NewGuid();
        GenerationId = generationId;
        Platform = seed.Platform;
        FooterSnapshot = ContentFooterSetting.NormalizeOptional(
            seed.FooterSnapshot,
            2000,
            "Footer snapshot");
        HashtagsSnapshot = ContentFooterSetting.NormalizeOptional(
            seed.HashtagsSnapshot,
            1000,
            "Hashtag snapshot");
        CreatedAt = nowUtc;
        UpdateBody(seed.Body, nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid GenerationId { get; private set; }
    public ContentPlatform Platform { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public string? FooterSnapshot { get; private set; }
    public string? HashtagsSnapshot { get; private set; }
    public string FullContent { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    internal void UpdateBody(string body, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("Content body is required.");
        var normalized = body.Trim();
        if (normalized.Length > 10000)
            throw new DomainException("Content body cannot exceed 10000 characters.");
        Body = normalized;
        FullContent = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { Body, FooterSnapshot, HashtagsSnapshot }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));
        UpdatedAt = nowUtc;
    }
}

public sealed class ContentAsset
{
    private ContentAsset()
    {
    }

    internal ContentAsset(Guid generationId, ContentAssetSeed seed, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        GenerationId = generationId;
        PublicUrl = Required(seed.PublicUrl, 2048, "Content asset URL");
        FileName = Required(seed.FileName, 260, "Content asset file name");
        ContentType = Required(seed.ContentType, 120, "Content asset type");
        if (seed.SortOrder < 0)
            throw new DomainException("Content asset sort order cannot be negative.");
        SortOrder = seed.SortOrder;
        CreatedAt = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid GenerationId { get; private set; }
    public string PublicUrl { get; private set; } = string.Empty;
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private static string Required(string? value, int maximumLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"{label} cannot exceed {maximumLength} characters.");
        return normalized;
    }
}
