using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lamie.API.Options;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Content;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public sealed class ContentPublishingService : IContentPublishingService
{
    private const int MaximumConcurrentGenerations = 4;
    private static readonly SemaphoreSlim GenerationGate =
        new(MaximumConcurrentGenerations, MaximumConcurrentGenerations);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessIdempotencyLocks =
        new(StringComparer.Ordinal);
    private static readonly IReadOnlyList<ContentStyleDto> StyleCatalog =
    [
        new("emotional-story", "Kể chuyện cảm xúc"),
        new("premium-minimal", "Tối giản cao cấp"),
        new("gentle-romance", "Dịu dàng, lãng mạn"),
        new("youthful", "Trẻ trung, vui vẻ"),
        new("subtle-direct", "Bán hàng trực tiếp nhưng tinh tế"),
        new("occasion-guide", "Gợi ý theo dịp tặng"),
        new("editorial-kj", "Editorial Korean–Japanese minimal")
    ];

    private readonly AppDbContext _dbContext;
    private readonly IContentAiProvider _aiProvider;
    private readonly IFileStorage _fileStorage;
    private readonly IPublicFileReader _fileReader;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAccessAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;
    private readonly OpenAIContentOptions _options;

    public ContentPublishingService(
        AppDbContext dbContext,
        IContentAiProvider aiProvider,
        IFileStorage fileStorage,
        IPublicFileReader fileReader,
        IHttpContextAccessor httpContextAccessor,
        IAccessAuditWriter auditWriter,
        TimeProvider timeProvider,
        IOptions<OpenAIContentOptions> options)
    {
        _dbContext = dbContext;
        _aiProvider = aiProvider;
        _fileStorage = fileStorage;
        _fileReader = fileReader;
        _httpContextAccessor = httpContextAccessor;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public ContentConfigurationDto GetConfiguration() =>
        new(_aiProvider.IsConfigured, _aiProvider.Model, StyleCatalog);

    public async Task<IReadOnlyList<ContentFooterSettingDto>> GetFootersAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _dbContext.Set<ContentFooterSetting>()
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Platform, cancellationToken);

        return Enum.GetValues<ContentPlatform>()
            .Select(platform => settings.TryGetValue(platform, out var setting)
                ? ToFooterDto(setting)
                : new ContentFooterSettingDto(null, platform, null, null, false, null))
            .ToList();
    }

    public async Task<ContentFooterSettingDto> UpsertFooterAsync(
        ContentPlatform platform,
        UpdateContentFooterRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(platform))
            throw Validation(nameof(platform), "Platform is invalid.");

        var now = UtcNow();
        var actor = ActorUserId();
        var setting = await _dbContext.Set<ContentFooterSetting>()
            .SingleOrDefaultAsync(item => item.Platform == platform, cancellationToken);
        object? before = setting is null ? null : ToFooterDto(setting);
        if (setting is null)
        {
            setting = new ContentFooterSetting(
                platform,
                request.Content,
                request.Hashtags,
                request.IsActive,
                now,
                actor);
            _dbContext.Add(setting);
        }
        else
        {
            setting.Update(
                request.Content,
                request.Hashtags,
                request.IsActive,
                now,
                actor);
        }

        var after = ToFooterDto(setting);
        _auditWriter.Record("content.footer.upsert", nameof(ContentFooterSetting), setting.Id.ToString(), before, after);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return after;
    }

    public async Task<ContentGenerationDto> GenerateAsync(
        GenerateContentRequest request,
        CancellationToken cancellationToken)
    {
        ValidateGenerateRequest(request);
        var actor = ActorUserId();
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim().ToUpperInvariant();
        var requestFingerprint = BuildGenerationFingerprint(request);
        if (idempotencyKey is not null)
        {
            var completed = await GenerationQuery()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    generation => generation.CreatedBy == actor
                        && generation.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (completed is not null)
            {
                if (!string.Equals(
                        completed.RequestFingerprint,
                        requestFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        "The Idempotency-Key was already used for a different content request.");
                }
                return ToGenerationDto(completed);
            }
        }

        if (!await GenerationGate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
        {
            throw new ContentAiProviderException(
                "Content generation is busy. Retry after the current requests finish.",
                true);
        }
        try
        {
            await using var idempotencyLease = idempotencyKey is null
                ? null
                : await AcquireIdempotencyLeaseAsync(actor, idempotencyKey, cancellationToken);
            if (idempotencyKey is not null)
            {
                var existing = await GenerationQuery()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        generation => generation.CreatedBy == actor
                            && generation.IdempotencyKey == idempotencyKey,
                        cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(
                            existing.RequestFingerprint,
                            requestFingerprint,
                            StringComparison.Ordinal))
                    {
                        throw new ConflictException(
                            "The Idempotency-Key was already used for a different content request.");
                    }
                    return ToGenerationDto(existing);
                }
            }

            return await GenerateCoreAsync(
                request,
                actor,
                idempotencyKey,
                requestFingerprint,
                cancellationToken);
        }
        finally
        {
            GenerationGate.Release();
        }
    }

    private async Task<ContentGenerationDto> GenerateCoreAsync(
        GenerateContentRequest request,
        Guid? actor,
        string? idempotencyKey,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        var sourceType = request.ProductId.HasValue
            ? ContentSourceType.Product
            : ContentSourceType.UploadedImages;
        var product = request.ProductId.HasValue
            ? await LoadProductContextAsync(request.ProductId.Value, cancellationToken)
            : null;
        var sourceImages = request.Images
            .Select(image => new ContentAiImage(image.FileName, image.ContentType, image.Bytes))
            .ToList();
        if (product is not null)
        {
            var thumbnailUrl = await _dbContext.Products
                .AsNoTracking()
                .Where(item => item.Id == product.Id)
                .Select(item => item.ThumbnailUrl)
                .SingleAsync(cancellationToken);
            var imageUrls = await _dbContext.ProductImages
                .AsNoTracking()
                .Where(image => image.ProductId == product.Id && image.IsActive)
                .OrderBy(image => image.SortOrder)
                .Select(image => image.ImageUrl)
                .Take(_options.MaximumImages)
                .ToListAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(thumbnailUrl)
                && imageUrls.All(url => !string.Equals(
                    url,
                    thumbnailUrl,
                    StringComparison.OrdinalIgnoreCase)))
            {
                imageUrls.Insert(0, thumbnailUrl);
                if (imageUrls.Count > _options.MaximumImages)
                    imageUrls.RemoveAt(imageUrls.Count - 1);
            }
            foreach (var url in imageUrls)
            {
                var stored = await _fileReader.ReadPublicAsync(url, cancellationToken);
                if (stored is not null && IsSupportedContentImage(stored))
                    sourceImages.Add(new ContentAiImage(stored.FileName, stored.ContentType, stored.Bytes));
            }
        }

        var (style, styleSeed) = await SelectStyleAsync(request.ProductId, cancellationToken);
        var aiResult = await _aiProvider.GenerateAsync(
            new ContentAiRequest(
                product,
                request.Brief,
                style.Id,
                style.Name,
                styleSeed,
                sourceImages),
            cancellationToken);
        var footers = await _dbContext.Set<ContentFooterSetting>()
            .AsNoTracking()
            .Where(setting => setting.IsActive)
            .ToDictionaryAsync(setting => setting.Platform, cancellationToken);
        var itemSeeds = new[]
        {
            ItemSeed(ContentPlatform.Facebook, aiResult.Facebook, footers),
            ItemSeed(ContentPlatform.Instagram, aiResult.Instagram, footers),
            ItemSeed(ContentPlatform.TikTok, aiResult.TikTok, footers)
        };

        var generationId = Guid.NewGuid();
        string? productImageUrl = null;
        var uploadedUrls = new List<string>();
        var assetSeeds = new List<ContentAssetSeed>();
        try
        {
            if (sourceType == ContentSourceType.UploadedImages)
            {
                for (var index = 0; index < request.Images.Count; index++)
                {
                    var upload = request.Images[index];
                    var extension = SafeImageExtension(upload.FileName, upload.ContentType);
                    await using var stream = new MemoryStream(upload.Bytes, writable: false);
                    var url = await _fileStorage.UploadPublicAsync(
                        stream,
                        $"content/{generationId:N}/{index + 1:00}{extension}",
                        upload.ContentType,
                        cancellationToken);
                    uploadedUrls.Add(url);
                    assetSeeds.Add(new ContentAssetSeed(
                        url,
                        Path.GetFileName(upload.FileName),
                        upload.ContentType,
                        index));
                }
            }
            else if (sourceImages.Count > 0)
            {
                var sourceImage = sourceImages[0];
                var extension = SafeImageExtension(sourceImage.FileName, sourceImage.ContentType);
                await using var stream = new MemoryStream(sourceImage.Bytes, writable: false);
                productImageUrl = await _fileStorage.UploadPublicAsync(
                    stream,
                    $"content/{generationId:N}/product-source-01{extension}",
                    sourceImage.ContentType,
                    cancellationToken);
                uploadedUrls.Add(productImageUrl);
                assetSeeds.Add(new ContentAssetSeed(
                    productImageUrl,
                    Path.GetFileName(sourceImage.FileName),
                    sourceImage.ContentType,
                    0));
            }

            var now = UtcNow();
            var generation = new ContentGeneration(
                sourceType,
                request.ProductId,
                product?.Name,
                productImageUrl,
                request.Brief,
                style.Id,
                style.Name,
                styleSeed,
                aiResult.Provider,
                aiResult.Model,
                aiResult.PromptVersion,
                idempotencyKey,
                requestFingerprint,
                null,
                actor,
                now,
                itemSeeds,
                assetSeeds,
                generationId);
            _dbContext.Add(generation);
            _auditWriter.Record(
                "content.generate",
                nameof(ContentGeneration),
                generation.Id.ToString(),
                null,
                new { generation.Id, generation.SourceType, generation.ProductId, generation.StyleId });
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToGenerationDto(generation);
        }
        catch
        {
            try
            {
                _dbContext.ChangeTracker.Clear();
                var committed = await GenerationQuery()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.Id == generationId,
                        CancellationToken.None);
                if (committed is not null)
                    return ToGenerationDto(committed);
            }
            catch
            {
                // The original operation failed and commit state could not be confirmed.
            }

            foreach (var url in uploadedUrls)
            {
                try
                {
                    await _fileStorage.DeleteAsync(url, CancellationToken.None);
                }
                catch
                {
                    // Preserve the original failure; an orphaned generated upload can be cleaned safely later.
                }
            }
            throw;
        }
    }

    public async Task<ContentGenerationDto> SaveAsync(
        Guid generationId,
        SaveContentRequest request,
        CancellationToken cancellationToken)
    {
        var bodies = ValidateSaveRequest(request);
        var generation = await GenerationQuery()
            .SingleOrDefaultAsync(item => item.Id == generationId, cancellationToken)
            ?? throw new NotFoundException(nameof(ContentGeneration), generationId);
        if (generation.Status == ContentGenerationStatus.Deleted)
            throw new NotFoundException(nameof(ContentGeneration), generationId);

        var now = UtcNow();
        if (generation.Status == ContentGenerationStatus.Draft)
        {
            generation.Save(bodies, now);
            _auditWriter.Record(
                "content.save",
                nameof(ContentGeneration),
                generation.Id.ToString(),
                new { status = ContentGenerationStatus.Draft },
                new { status = ContentGenerationStatus.Saved });
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return ToGenerationDto(generation);
            }
            catch (DbUpdateConcurrencyException)
            {
                _dbContext.ChangeTracker.Clear();
                generation = await GenerationQuery()
                    .SingleOrDefaultAsync(item => item.Id == generationId, cancellationToken)
                    ?? throw new NotFoundException(nameof(ContentGeneration), generationId);
                if (generation.Status == ContentGenerationStatus.Deleted)
                    throw new NotFoundException(nameof(ContentGeneration), generationId);
                if (generation.Status != ContentGenerationStatus.Saved)
                {
                    throw new ConflictException(
                        "Content changed while it was being saved. Reload and try again.");
                }
                now = UtcNow();
            }
        }

        if (BodiesMatch(generation, bodies))
        {
            return ToGenerationDto(generation);
        }

        return await CreateSavedVersionAsync(generation, bodies, now, cancellationToken);
    }

    private async Task<ContentGenerationDto> CreateSavedVersionAsync(
        ContentGeneration generation,
        IReadOnlyDictionary<ContentPlatform, string> bodies,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var versionFingerprint = BuildSavedContentFingerprint(bodies);
        var existingVersion = await GenerationQuery()
            .AsNoTracking()
            .Where(item => item.ParentGenerationId == generation.Id
                && item.RequestFingerprint == versionFingerprint
                && item.Status == ContentGenerationStatus.Saved)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingVersion is not null)
            return ToGenerationDto(existingVersion);

        var versionId = Guid.NewGuid();
        var version = new ContentGeneration(
            generation.SourceType,
            generation.ProductId,
            generation.ProductNameSnapshot,
            generation.ProductImageUrlSnapshot,
            generation.Brief,
            generation.StyleId,
            generation.StyleName,
            generation.StyleSeed,
            generation.Provider,
            generation.Model,
            generation.PromptVersion,
            null,
            versionFingerprint,
            generation.Id,
            ActorUserId(),
            now,
            generation.Items.Select(item => new ContentItemSeed(
                item.Platform,
                bodies[item.Platform],
                item.FooterSnapshot,
                item.HashtagsSnapshot)),
            generation.Assets.Select(asset => new ContentAssetSeed(
                asset.PublicUrl,
                asset.FileName,
                asset.ContentType,
                asset.SortOrder)),
            versionId);
        version.Save(bodies, now);
        _dbContext.Add(version);
        _auditWriter.Record(
            "content.version.create",
            nameof(ContentGeneration),
            version.Id.ToString(),
            new { parentGenerationId = generation.Id },
            new { version.Id, version.Status });
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToGenerationDto(version);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            existingVersion = await GenerationQuery()
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ParentGenerationId == generation.Id
                    && item.RequestFingerprint == versionFingerprint
                    && item.Status == ContentGenerationStatus.Saved,
                    cancellationToken);
            if (existingVersion is not null)
                return ToGenerationDto(existingVersion);
            throw;
        }
    }

    private static bool BodiesMatch(
        ContentGeneration generation,
        IReadOnlyDictionary<ContentPlatform, string> bodies) =>
        generation.Items.All(item =>
            string.Equals(item.Body, bodies[item.Platform].Trim(), StringComparison.Ordinal));

    private static string BuildGenerationFingerprint(GenerateContentRequest request)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            productId = request.ProductId,
            brief = string.IsNullOrWhiteSpace(request.Brief) ? null : request.Brief.Trim(),
            images = request.Images.Select((image, index) => new
            {
                index,
                fileName = Path.GetFileName(image.FileName),
                contentType = image.ContentType.ToLowerInvariant(),
                sha256 = Convert.ToHexString(SHA256.HashData(image.Bytes)).ToLowerInvariant()
            })
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private static string BuildSavedContentFingerprint(
        IReadOnlyDictionary<ContentPlatform, string> bodies)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(bodies
            .OrderBy(item => item.Key)
            .Select(item => new { platform = (int)item.Key, body = item.Value.Trim() }));
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    private async Task<IAsyncDisposable> AcquireIdempotencyLeaseAsync(
        Guid? actor,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var resourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{actor?.ToString("N") ?? "unknown"}:{idempotencyKey}"))).ToLowerInvariant();
        var resource = $"lamie:content:{resourceHash}";

        if (!_dbContext.Database.IsSqlServer())
        {
            var semaphore = ProcessIdempotencyLocks.GetOrAdd(
                resource,
                _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);
            return new AsyncLease(() =>
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            });
        }

        var connection = _dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await _dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @resource,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = @lockTimeout;
                SELECT @result;
                """;
            AddParameter(command, "@resource", resource);
            var lockTimeout = Math.Min(
                int.MaxValue,
                (_options.TimeoutSeconds * (_options.MaximumRetries + 1) + 30) * 1000L);
            AddParameter(command, "@lockTimeout", lockTimeout);
            var result = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            if (result < 0)
                throw new ConflictException("Another request with this Idempotency-Key is still running.");

            return new AsyncLease(async () =>
            {
                try
                {
                    await using var release = connection.CreateCommand();
                    release.CommandText =
                        "EXEC sys.sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
                    AddParameter(release, "@resource", resource);
                    await release.ExecuteNonQueryAsync(CancellationToken.None);
                }
                finally
                {
                    if (openedHere)
                        await _dbContext.Database.CloseConnectionAsync();
                }
            });
        }
        catch
        {
            if (openedHere)
                await _dbContext.Database.CloseConnectionAsync();
            throw;
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public async Task<ContentGenerationDto> GetAsync(
        Guid generationId,
        CancellationToken cancellationToken)
    {
        var generation = await GenerationQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == generationId && item.Status != ContentGenerationStatus.Deleted,
                cancellationToken)
            ?? throw new NotFoundException(nameof(ContentGeneration), generationId);
        return ToGenerationDto(generation);
    }

    public async Task<PagedContentHistoryDto> GetHistoryAsync(
        ContentHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ValidateHistoryQuery(query);
        var generations = _dbContext.Set<ContentGeneration>()
            .AsNoTracking()
            .Where(generation => generation.Status != ContentGenerationStatus.Deleted);
        if (query.ProductId.HasValue)
            generations = generations.Where(generation => generation.ProductId == query.ProductId);
        if (query.Platform.HasValue)
            generations = generations.Where(generation => generation.Items.Any(item => item.Platform == query.Platform));
        if (query.Status.HasValue)
            generations = generations.Where(generation => generation.Status == query.Status);

        if (query.From.HasValue || query.To.HasValue)
        {
            var from = query.From ?? query.To!.Value;
            var to = query.To ?? query.From!.Value;
            var (startUtc, endUtc) = BusinessDayUtc(from, to);
            generations = query.Status == ContentGenerationStatus.Saved
                ? generations.Where(generation => generation.SavedAt >= startUtc
                    && generation.SavedAt < endUtc)
                : generations.Where(generation => generation.CreatedAt >= startUtc
                    && generation.CreatedAt < endUtc);
        }

        var totalCount = await generations.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)query.PageSize));
        var page = Math.Min(query.Page, totalPages);
        var orderedGenerations = query.Status == ContentGenerationStatus.Saved
            ? generations.OrderByDescending(generation => generation.SavedAt)
            : generations.OrderByDescending(generation => generation.CreatedAt);
        var rows = await orderedGenerations
            .ThenByDescending(generation => generation.Id)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(generation => new ContentHistoryItemDto(
                generation.Id,
                generation.SourceType,
                generation.ProductId,
                generation.ProductNameSnapshot,
                generation.StyleName,
                generation.Status,
                generation.Items.OrderBy(item => item.Platform).Select(item => item.Platform).ToList(),
                AsUtcOffset(generation.CreatedAt),
                generation.SavedAt.HasValue ? AsUtcOffset(generation.SavedAt.Value) : null,
                generation.ParentGenerationId))
            .ToListAsync(cancellationToken);

        return new PagedContentHistoryDto(
            rows,
            totalCount,
            page,
            query.PageSize,
            totalPages,
            page < totalPages,
            page > 1);
    }

    private IQueryable<ContentGeneration> GenerationQuery() =>
        _dbContext.Set<ContentGeneration>()
            .Include(generation => generation.Items)
            .Include(generation => generation.Assets);

    private async Task<ContentAiProductContext> LoadProductContextAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(item => item.Id == productId)
            .Select(item => new
            {
                item.Id,
                item.Sku,
                item.Price,
                item.SalePrice,
                item.CategoryId,
                Translations = item.Translations
                    .Select(translation => new
                    {
                        translation.LanguageCode,
                        translation.Name,
                        translation.Description
                    })
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Product), productId);
        var translation = product.Translations
            .OrderBy(item => item.LanguageCode.Equals("vi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.LanguageCode)
            .FirstOrDefault();
        var categoryName = await _dbContext.CategoryTranslations
            .AsNoTracking()
            .Where(item => item.CategoryId == product.CategoryId)
            .OrderBy(item => item.LanguageCode == "vi" ? 0 : 1)
            .Select(item => item.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return new ContentAiProductContext(
            product.Id,
            product.Sku,
            translation?.Name ?? product.Sku,
            translation?.Description ?? string.Empty,
            product.Price,
            product.SalePrice,
            categoryName);
    }

    private async Task<(ContentStyleDto Style, int Seed)> SelectStyleAsync(
        int? productId,
        CancellationToken cancellationToken)
    {
        var lastStyleId = await _dbContext.Set<ContentGeneration>()
            .AsNoTracking()
            .Where(generation => generation.ProductId == productId
                && generation.Status != ContentGenerationStatus.Deleted)
            .OrderByDescending(generation => generation.CreatedAt)
            .Select(generation => generation.StyleId)
            .FirstOrDefaultAsync(cancellationToken);
        var candidates = StyleCatalog.Where(style => style.Id != lastStyleId).ToList();
        if (candidates.Count == 0)
            candidates = StyleCatalog.ToList();
        var seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        return (candidates[seed % candidates.Count], seed);
    }

    private static ContentItemSeed ItemSeed(
        ContentPlatform platform,
        string body,
        IReadOnlyDictionary<ContentPlatform, ContentFooterSetting> footers)
    {
        footers.TryGetValue(platform, out var footer);
        return new ContentItemSeed(platform, body, footer?.Content, footer?.Hashtags);
    }

    private static IReadOnlyDictionary<ContentPlatform, string> ValidateSaveRequest(
        SaveContentRequest request)
    {
        if (request.Items is null || request.Items.Count != 3)
            throw Validation("items", "Exactly three platform items are required.");
        if (request.Items.Any(item => !Enum.IsDefined(item.Platform)))
            throw Validation("items", "A platform is invalid.");
        if (request.Items.Select(item => item.Platform).Distinct().Count() != 3)
            throw Validation("items", "Each platform must appear exactly once.");
        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.Body)))
            throw Validation("items", "Every platform body is required.");
        return request.Items.ToDictionary(item => item.Platform, item => item.Body);
    }

    private void ValidateGenerateRequest(GenerateContentRequest request)
    {
        var hasProduct = request.ProductId.HasValue;
        var hasImages = request.Images is { Count: > 0 };
        if (hasProduct == hasImages)
            throw Validation("source", "Choose either one product or uploaded images.");
        if (request.ProductId is <= 0)
            throw Validation("productId", "Product is invalid.");
        if (request.Images.Count > _options.MaximumImages)
            throw Validation("images", $"At most {_options.MaximumImages} images are allowed.");
        if (request.Images.Any(image => image.Bytes.LongLength is <= 0
                || image.Bytes.LongLength > _options.MaximumImageBytes))
            throw Validation("images", "An image is empty or exceeds the configured size limit.");
        if (request.Images.Any(image => image.ContentType.ToLowerInvariant()
                is not ("image/jpeg" or "image/png" or "image/webp")))
        {
            throw Validation("images", "Only JPEG, PNG, and WebP content images are supported.");
        }
        if (request.Images.Any(image => !HasValidContentImageSignature(
                image.ContentType,
                image.Bytes)))
        {
            throw Validation("images", "An image does not match its declared content type.");
        }
        if (request.Brief?.Trim().Length > 4000)
            throw Validation("brief", "Brief cannot exceed 4000 characters.");
        if (request.IdempotencyKey?.Trim().Length > 120)
            throw Validation("idempotencyKey", "Idempotency key cannot exceed 120 characters.");
    }

    private static void ValidateHistoryQuery(ContentHistoryQuery query)
    {
        if (query.Page < 1)
            throw Validation("page", "Page must be at least 1.");
        if (query.PageSize is < 1 or > 100)
            throw Validation("pageSize", "Page size must be between 1 and 100.");
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            throw Validation("from", "From date cannot be after to date.");
        if (query.Platform.HasValue && !Enum.IsDefined(query.Platform.Value))
            throw Validation("platform", "Platform is invalid.");
        if (query.Status.HasValue
            && query.Status is not ContentGenerationStatus.Draft and not ContentGenerationStatus.Saved)
            throw Validation("status", "Status is invalid.");
    }

    private static (DateTime StartUtc, DateTime EndUtc) BusinessDayUtc(DateOnly from, DateOnly to)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }

        var localStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, zone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
    }

    private static ContentFooterSettingDto ToFooterDto(ContentFooterSetting setting) =>
        new(
            setting.Id,
            setting.Platform,
            setting.Content,
            setting.Hashtags,
            setting.IsActive,
            AsUtcOffset(setting.UpdatedAt));

    private static ContentGenerationDto ToGenerationDto(ContentGeneration generation) =>
        new(
            generation.Id,
            generation.SourceType,
            generation.ProductId,
            generation.ProductNameSnapshot,
            generation.ProductImageUrlSnapshot,
            generation.Brief,
            generation.StyleId,
            generation.StyleName,
            generation.StyleSeed,
            generation.Status,
            generation.Provider,
            generation.Model,
            generation.PromptVersion,
            generation.ParentGenerationId,
            AsUtcOffset(generation.CreatedAt),
            AsUtcOffset(generation.UpdatedAt),
            generation.SavedAt.HasValue ? AsUtcOffset(generation.SavedAt.Value) : null,
            generation.Items
                .OrderBy(item => item.Platform)
                .Select(item => new ContentItemDto(
                    item.Id,
                    item.Platform,
                    item.Body,
                    item.FooterSnapshot,
                    item.HashtagsSnapshot,
                    item.FullContent))
                .ToList(),
            generation.Assets
                .OrderBy(asset => asset.SortOrder)
                .Select(asset => new ContentAssetDto(
                    asset.Id,
                    asset.PublicUrl,
                    asset.FileName,
                    asset.ContentType,
                    asset.SortOrder))
                .ToList());

    private static string SafeImageExtension(string fileName, string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw Validation("images", $"Image '{Path.GetFileName(fileName)}' has an unsupported type.")
        };

    private bool IsSupportedContentImage(StoredPublicFile file) =>
        file.Bytes.LongLength is > 0
            && file.Bytes.LongLength <= _options.MaximumImageBytes
            && HasValidContentImageSignature(file.ContentType, file.Bytes);

    private static bool HasValidContentImageSignature(string contentType, byte[] bytes) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytes.Length >= 3
                && bytes[0] == 0xff
                && bytes[1] == 0xd8
                && bytes[2] == 0xff,
            "image/png" => bytes.Length >= 8
                && bytes.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            "image/webp" => bytes.Length >= 12
                && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };

    private Guid? ActorUserId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var value = principal?.FindFirstValue("sub")
            ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private sealed class AsyncLease(Func<ValueTask> release) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _disposed, 1) == 0
                ? release()
                : ValueTask.CompletedTask;
    }
}
