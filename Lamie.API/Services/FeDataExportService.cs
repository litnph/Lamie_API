using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lamie.API.Options;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.FeData;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public sealed class FeDataExportService : IFeDataExportService
{
    public const string SchemaVersion = "1.1";

    private static readonly SemaphoreSlim ExportGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppDbContext _dbContext;
    private readonly IPublicFileReader _fileReader;
    private readonly IAccessAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;
    private readonly string _targetDirectory;

    public FeDataExportService(
        AppDbContext dbContext,
        IPublicFileReader fileReader,
        IAccessAuditWriter auditWriter,
        TimeProvider timeProvider,
        IOptions<FeDataExportOptions> options,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _fileReader = fileReader;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
        _targetDirectory = ResolveTargetDirectory(
            options.Value.TargetDirectory,
            options.Value.AllowedRoot,
            environment.ContentRootPath);
    }

    public async Task<FeDataExportResultDto> ExportAsync(CancellationToken cancellationToken)
    {
        if (!await ExportGate.WaitAsync(0, cancellationToken))
            throw new ConflictException("An FE Data export is already running.");

        var parent = Path.GetDirectoryName(_targetDirectory)
            ?? throw new InvalidOperationException("FE Data target parent could not be resolved.");
        var operationId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, $".fe-data.staging-{operationId}");
        var backup = Path.Combine(parent, $".fe-data.backup-{operationId}");
        var generatedAt = _timeProvider.GetUtcNow();
        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(Path.Combine(staging, "images", "products"));

            var snapshot = await LoadSnapshotAsync(cancellationToken);
            var warnings = new List<string>();
            var issues = new List<string>();
            var exportedProducts = new List<ExportProduct>(snapshot.Products.Count);
            var eligibleProductIds = snapshot.Products.Select(product => product.Id).ToHashSet();
            var productSlugs = new HashSet<string>(StringComparer.Ordinal);
            var imageCount = 0;

            foreach (var product in snapshot.Products)
            {
                var translation = PreferredTranslation(product.Translations);
                var slug = SafeSlug(translation?.Slug, $"product-{product.Id}");
                if (!productSlugs.Add(slug))
                {
                    issues.Add(
                        $"Sản phẩm {product.Id} ({translation?.Name ?? product.Sku}): slug '{slug}' bị trùng trong catalog.");
                }
                var category = snapshot.Categories.GetValueOrDefault(product.CategoryId);
                var categoryName = category?.Name ?? "Sản phẩm";
                var categorySlug = SafeSlug(category?.Name, $"category-{product.CategoryId}");
                var productLine = product.ProductTypeId is int productTypeId
                    && snapshot.ProductTypes.TryGetValue(productTypeId, out var productTypeName)
                    ? new ExportProductLine(
                        productTypeId.ToString(CultureInfo.InvariantCulture),
                        productTypeName)
                    : null;
                var sourceImages = product.Images
                    .Where(image => image.IsActive)
                    .OrderBy(image => image.SortOrder)
                    .ThenBy(image => image.Id)
                    .Select(image => new SourceImage(image.Id.ToString(CultureInfo.InvariantCulture), image.ImageUrl, image.SortOrder))
                    .ToList();
                if (!string.IsNullOrWhiteSpace(product.ThumbnailUrl)
                    && sourceImages.All(image => !string.Equals(
                        image.Url,
                        product.ThumbnailUrl,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    sourceImages.Insert(0, new SourceImage("thumbnail", product.ThumbnailUrl, -1));
                }

                var images = new List<ExportImage>(sourceImages.Count);
                foreach (var sourceImage in sourceImages)
                {
                    var stored = await _fileReader.ReadPublicAsync(sourceImage.Url, cancellationToken);
                    if (stored is null || !IsValidImage(stored))
                    {
                        issues.Add($"Sản phẩm {product.Id} ({translation?.Name ?? product.Sku}): ảnh '{sourceImage.Url}' bị thiếu hoặc hỏng.");
                        continue;
                    }

                    var hash = Convert.ToHexString(SHA256.HashData(stored.Bytes)).ToLowerInvariant();
                    var extension = SafeExtension(stored.ContentType);
                    var fileName = $"{product.Id}-{SafeFileToken(sourceImage.Id)}-{hash[..12]}{extension}";
                    var relativeUrl = $"images/products/{fileName}";
                    await File.WriteAllBytesAsync(
                        Path.Combine(staging, "images", "products", fileName),
                        stored.Bytes,
                        cancellationToken);
                    images.Add(new ExportImage(
                        sourceImage.Id,
                        relativeUrl,
                        translation?.Name ?? product.Sku,
                        images.Count));
                    imageCount++;
                }

                if (sourceImages.Count == 0)
                    warnings.Add($"Sản phẩm {product.Id} ({translation?.Name ?? product.Sku}) chưa có ảnh; FE sẽ dùng ảnh dự phòng.");

                exportedProducts.Add(new ExportProduct(
                    product.Id.ToString(CultureInfo.InvariantCulture),
                    product.Sku,
                    slug,
                    translation?.Name ?? product.Sku,
                    translation?.Description ?? string.Empty,
                    product.Price,
                    product.SalePrice,
                    new ExportCategory(
                        product.CategoryId.ToString(CultureInfo.InvariantCulture),
                        categoryName,
                        categorySlug),
                    productLine,
                    images,
                    new ExportAttributes(
                        Names(product.Tags.Select(item => item.TagId), snapshot.Tags),
                        Names(product.Colors.Select(item => item.ColorId), snapshot.Colors),
                        Names(product.Collections.Select(item => item.CollectionId), snapshot.Collections),
                        Names(product.Occasions.Select(item => item.OccasionId), snapshot.Occasions),
                        Names(product.Styles.Select(item => item.StyleId), snapshot.Styles)),
                    product.SimilarProducts
                        .Select(item => item.SimilarProductId)
                        .Where(id => id != product.Id && eligibleProductIds.Contains(id))
                        .Distinct()
                        .OrderBy(id => id)
                        .Select(id => id.ToString(CultureInfo.InvariantCulture))
                        .ToList()));
            }

            if (issues.Count > 0)
                throw new FeDataExportException(
                    "FE Data was not published because catalog validation failed.",
                    issues);

            var catalog = new ExportCatalogDocument(
                SchemaVersion,
                generatedAt,
                new ExportCatalogSettings(snapshot.PriceDeviationPercent),
                exportedProducts);
            var canonicalCatalogJson = JsonSerializer.Serialize(catalog, JsonOptions);
            var catalogVersion = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalCatalogJson))).ToLowerInvariant();
            var manifest = new ExportManifest(
                SchemaVersion,
                generatedAt,
                new ExportCatalogReference(
                    "products.json",
                    catalogVersion,
                    exportedProducts.Count),
                imageCount);

            await WriteValidatedJsonAsync(
                Path.Combine(staging, "products.json"),
                catalog,
                cancellationToken);
            await WriteValidatedJsonAsync(
                Path.Combine(staging, "manifest.json"),
                manifest,
                cancellationToken);
            ValidateStaging(staging, imageCount);
            bool backupCleaned;
            try
            {
                backupCleaned = PublishStaging(staging, backup, _targetDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new FeDataExportException(
                    "FE Data was generated but could not replace the storefront files.",
                    ["Không thể cập nhật thư mục FE Data. Hãy kiểm tra quyền ghi và dừng tiến trình đang khóa thư mục rồi thử lại."]);
            }
            if (!backupCleaned)
            {
                warnings.Add(
                    "FE Data đã được xuất thành công nhưng không thể dọn bản sao lưu tạm; hãy kiểm tra quyền filesystem của máy chủ.");
            }

            var result = new FeDataExportResultDto(
                generatedAt,
                exportedProducts.Count,
                imageCount,
                catalogVersion,
                warnings);
            try
            {
                _auditWriter.Record(
                    "fe-data.export.success",
                    "FeDataExport",
                    catalogVersion,
                    null,
                    result);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                _dbContext.ChangeTracker.Clear();
                warnings.Add("FE Data đã được xuất nhưng không thể lưu nhật ký audit; hãy kiểm tra log máy chủ.");
            }
            return result;
        }
        catch (Exception exception)
        {
            TryDeleteDirectory(staging, parent);
            try
            {
                _auditWriter.Record(
                    "fe-data.export.failed",
                    "FeDataExport",
                    operationId,
                    null,
                    new
                    {
                        error = exception.Message,
                        issues = exception is FeDataExportException export
                            ? export.Issues
                            : Array.Empty<string>()
                    });
                await _dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch
            {
                // Audit persistence must not replace the actionable export error.
            }
            throw;
        }
        finally
        {
            ExportGate.Release();
        }
    }

    public static string ResolveTargetDirectory(
        string configuredPath,
        string configuredAllowedRoot,
        string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("FeDataExport:TargetDirectory is required.");
        if (string.IsNullOrWhiteSpace(configuredAllowedRoot))
            throw new InvalidOperationException("FeDataExport:AllowedRoot is required.");
        if (string.IsNullOrWhiteSpace(contentRootPath))
            throw new InvalidOperationException("The application content root is required.");

        var resolved = ResolveConfiguredPath(configuredPath, contentRootPath);
        var allowedRoot = ResolveConfiguredPath(configuredAllowedRoot, contentRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(
            Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            "fe-data",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("FE Data target must be a dedicated directory named 'fe-data'.");
        }
        if (Path.GetPathRoot(resolved)?.TrimEnd(Path.DirectorySeparatorChar) == resolved.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException("FE Data target cannot be a filesystem root.");
        if (Path.GetPathRoot(allowedRoot)?.TrimEnd(Path.DirectorySeparatorChar) == allowedRoot)
            throw new InvalidOperationException("FE Data allowed root cannot be a filesystem root.");

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var allowedPrefix = allowedRoot + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(allowedPrefix, comparison))
        {
            throw new InvalidOperationException(
                "FE Data target must be contained by FeDataExport:AllowedRoot.");
        }
        return resolved;
    }

    private static string ResolveConfiguredPath(string configuredPath, string contentRootPath) =>
        Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRootPath, configuredPath));

    private async Task<ExportSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.IsVisibleOnFE)
            .Include(product => product.Translations)
            .Include(product => product.Images)
            .Include(product => product.Tags)
            .Include(product => product.Colors)
            .Include(product => product.Collections)
            .Include(product => product.Occasions)
            .Include(product => product.Styles)
            .Include(product => product.SimilarProducts)
            .AsSplitQuery()
            .OrderBy(product => product.Id)
            .ToListAsync(cancellationToken);

        var categoryIds = products.Select(product => product.CategoryId).Distinct().ToList();
        var categories = await _dbContext.CategoryTranslations
            .AsNoTracking()
            .Where(item => categoryIds.Contains(item.CategoryId))
            .ToListAsync(cancellationToken);
        var tags = await _dbContext.TagTranslations.AsNoTracking().ToListAsync(cancellationToken);
        var colors = await _dbContext.ColorTranslations.AsNoTracking().ToListAsync(cancellationToken);
        var collections = await _dbContext.CollectionTranslations.AsNoTracking().ToListAsync(cancellationToken);
        var occasions = await _dbContext.OccasionTranslations.AsNoTracking().ToListAsync(cancellationToken);
        var styles = await _dbContext.StyleTranslations.AsNoTracking().ToListAsync(cancellationToken);
        var productTypes = await _dbContext.ProductTypeTranslations.AsNoTracking().ToListAsync(cancellationToken);
        var priceDeviationPercent = await _dbContext.ProductCatalogSettings
            .AsNoTracking()
            .Where(item => item.Id == ProductCatalogSettings.SingletonId)
            .Select(item => (decimal?)item.PriceDeviationPercent)
            .SingleOrDefaultAsync(cancellationToken)
            ?? ProductCatalogSettings.DefaultPriceDeviationPercent;
        await transaction.CommitAsync(cancellationToken);

        return new ExportSnapshot(
            products,
            categories.GroupBy(item => item.CategoryId).ToDictionary(
                group => group.Key,
                group => new LocalizedValue(PreferredName(group.Select(item => (item.LanguageCode, item.Name))))),
            tags.GroupBy(item => item.TagId).ToDictionary(
                group => group.Key,
                group => PreferredName(group.Select(item => (item.LanguageCode, item.Name)))),
            colors.GroupBy(item => item.ColorId).ToDictionary(
                group => group.Key,
                group => PreferredName(group.Select(item => (item.LanguageCode, item.Name)))),
            collections.GroupBy(item => item.CollectionId).ToDictionary(
                group => group.Key,
                group => PreferredName(group.Select(item => (item.LanguageCode, item.Name)))),
            occasions.GroupBy(item => item.OccasionId).ToDictionary(
                group => group.Key,
                group => PreferredName(group.Select(item => (item.LanguageCode, item.Name)))),
            styles.GroupBy(item => item.StyleId).ToDictionary(
                group => group.Key,
                group => PreferredName(group.Select(item => (item.LanguageCode, item.Name)))),
            productTypes.GroupBy(item => item.ProductTypeId).ToDictionary(
                group => group.Key,
                group => PreferredName(group.Select(item => (item.LanguageCode, item.Name)))),
            priceDeviationPercent);
    }

    private static ProductTranslation? PreferredTranslation(IEnumerable<ProductTranslation> translations) =>
        translations
            .OrderBy(item => string.Equals(item.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => string.Equals(item.LanguageCode, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.LanguageCode)
            .FirstOrDefault();

    private static string PreferredName(IEnumerable<(string LanguageCode, string Name)> values) =>
        values
            .OrderBy(item => string.Equals(item.LanguageCode, "vi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => string.Equals(item.LanguageCode, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.LanguageCode)
            .Select(item => item.Name)
            .FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<string> Names(
        IEnumerable<int> ids,
        IReadOnlyDictionary<int, string> lookup) =>
        ids.Distinct()
            .Select(id => lookup.GetValueOrDefault(id))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList()!;

    private static async Task WriteValidatedJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using (JsonDocument.Parse(json))
        {
        }
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
        await using var stream = File.OpenRead(path);
        using var parsed = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Generated JSON '{Path.GetFileName(path)}' is invalid.");
    }

    private static void ValidateStaging(string staging, int expectedImages)
    {
        if (!File.Exists(Path.Combine(staging, "manifest.json"))
            || !File.Exists(Path.Combine(staging, "products.json")))
            throw new InvalidOperationException("Generated FE Data is incomplete.");
        var actualImages = Directory.EnumerateFiles(
            Path.Combine(staging, "images", "products"),
            "*",
            SearchOption.TopDirectoryOnly).Count();
        if (actualImages != expectedImages)
            throw new InvalidOperationException("Generated FE Data image count is inconsistent.");
    }

    private static bool PublishStaging(string staging, string backup, string target)
    {
        var movedExisting = false;
        try
        {
            if (Directory.Exists(target))
            {
                Directory.Move(target, backup);
                movedExisting = true;
            }
            Directory.Move(staging, target);
        }
        catch
        {
            if (!Directory.Exists(target) && movedExisting && Directory.Exists(backup))
                Directory.Move(backup, target);
            throw;
        }

        if (!movedExisting)
            return true;
        try
        {
            Directory.Delete(backup, recursive: true);
            return true;
        }
        catch
        {
            // Publication has already completed. A cleanup failure must not report the live export as failed.
            return false;
        }
    }

    private static void TryDeleteDirectory(string path, string expectedParent)
    {
        try
        {
            var resolved = Path.GetFullPath(path);
            var parent = Path.GetFullPath(expectedParent).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith(".fe-data.", StringComparison.Ordinal)
                && Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; preserve the export result/error.
        }
    }

    private static bool IsValidImage(StoredPublicFile file)
    {
        var bytes = file.Bytes;
        return file.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            "image/gif" => bytes.Length >= 6 && (bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8)),
            "image/webp" => bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }

    private static string SafeExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => throw new InvalidOperationException("Unsupported product image type.")
    };

    private static string SafeFileToken(string value)
    {
        var token = new string(value.ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(token) ? "image" : token;
    }

    private static string SafeSlug(string? value, string fallback)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            var ascii = character is 'đ' or 'Đ'
                ? 'd'
                : char.ToLowerInvariant(character);
            if (ascii is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(ascii);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }
        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }

    private sealed record SourceImage(string Id, string Url, int SortOrder);
    private sealed record LocalizedValue(string Name);
    private sealed record ExportSnapshot(
        IReadOnlyList<Product> Products,
        IReadOnlyDictionary<int, LocalizedValue> Categories,
        IReadOnlyDictionary<int, string> Tags,
        IReadOnlyDictionary<int, string> Colors,
        IReadOnlyDictionary<int, string> Collections,
        IReadOnlyDictionary<int, string> Occasions,
        IReadOnlyDictionary<int, string> Styles,
        IReadOnlyDictionary<int, string> ProductTypes,
        decimal PriceDeviationPercent);

    private sealed record ExportManifest(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        ExportCatalogReference Catalog,
        int ImageCount);

    private sealed record ExportCatalogReference(
        string Href,
        string Version,
        int ProductCount);

    private sealed record ExportCatalogDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        ExportCatalogSettings Settings,
        IReadOnlyList<ExportProduct> Products);

    private sealed record ExportCatalogSettings(decimal PriceDeviationPercent);

    private sealed record ExportProduct(
        string Id,
        string Sku,
        string Slug,
        string Name,
        string Description,
        decimal Price,
        decimal? SalePrice,
        ExportCategory Category,
        ExportProductLine? ProductLine,
        IReadOnlyList<ExportImage> Images,
        ExportAttributes Attributes,
        IReadOnlyList<string> SimilarProductIds);

    private sealed record ExportCategory(string Id, string Name, string Slug);
    private sealed record ExportProductLine(string Id, string Name);
    private sealed record ExportImage(string Id, string Url, string Alt, int SortOrder);
    private sealed record ExportAttributes(
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Colors,
        IReadOnlyList<string> Collections,
        IReadOnlyList<string> Occasions,
        IReadOnlyList<string> Styles);
}
