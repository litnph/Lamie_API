using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;
using Lamie.Domain.Products;
using Lamie.Application.Common.Uploads;

namespace Lamie.Application.Settings.Products.Commands;

public sealed class CreateProductHandler : IRequestHandler<CreateProductCommand, int>
{
    private readonly IProductRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly IProductTypeRepository _productTypeRepository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public CreateProductHandler(
        IProductRepository repository,
        IFileStorage fileStorage,
        IProductTypeRepository productTypeRepository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _fileStorage = fileStorage;
        _productTypeRepository = productTypeRepository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task<int> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var sku = await ResolveSkuAsync(command.Sku, cancellationToken);
        command.Sku = sku;
        var productType = await _productTypeRepository.GetByIdAsync(command.ProductTypeId);
        if (productType is null || !productType.IsActive)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["productTypeId"] = ["ProductTypeId must reference an active product type"]
            });

        await _referentialIntegrity.ValidateProductReferencesAsync(
            new ProductReferenceSet(
                command.CategoryId,
                command.ProductTypeId,
                command.TagIds,
                command.ColorIds,
                command.CollectionIds,
                command.StyleIds,
                command.OccasionIds,
                command.Translations.Select(item => item.LanguageCode).ToArray()),
            cancellationToken);

        var product = new Product(
            sku,
            command.Price,
            command.Stock,
            command.CategoryId,
            command.ProductTypeId,
            command.TracksInventory);
        if (command.SalePrice.HasValue)
            product.SetSalePrice(command.SalePrice.Value);

        foreach (var translation in command.Translations)
        {
            product.AddTranslation(
                translation.LanguageCode,
                translation.Name,
                translation.Slug,
                translation.Description ?? string.Empty);
        }

        foreach (var id in command.TagIds.Distinct())
            product.AddTag(id);
        foreach (var id in command.ColorIds.Distinct())
            product.AddColor(id);
        foreach (var id in command.CollectionIds.Distinct())
            product.AddCollection(id);
        foreach (var id in command.StyleIds.Distinct())
            product.AddStyle(id);
        foreach (var id in command.OccasionIds.Distinct())
            product.AddOccasion(id);

        var uploadedUrls = new List<string>();
        try
        {
            if (command.ThumbnailFile is { Length: > 0 })
            {
                var objectPath = BuildProductObjectPath(sku, command.ThumbnailFile.FileName, "thumbnail");
                await using var source = command.ThumbnailFile.OpenReadStream();
                await using var stream = await ProductImageWatermarker.ApplyAsync(source, sku, command.ThumbnailFile.ContentType, cancellationToken);
                var url = await _fileStorage.UploadPublicAsync(
                    stream,
                    objectPath,
                    command.ThumbnailFile.ContentType,
                    cancellationToken);
                uploadedUrls.Add(url);
                product.SetThumbnail(url);
            }
            else if (!string.IsNullOrWhiteSpace(command.ThumbnailUrl))
            {
                product.SetThumbnail(command.ThumbnailUrl);
            }

            await AddImagesAsync(product, command, sku, uploadedUrls, cancellationToken);
            if (string.IsNullOrWhiteSpace(product.ThumbnailUrl))
            {
                product.SetThumbnail(product.Images.OrderBy(image => image.SortOrder).FirstOrDefault()?.ImageUrl);
            }

            await _repository.AddAsync(product);
            return product.Id;
        }
        catch
        {
            await CleanupUploadsAsync(uploadedUrls, CancellationToken.None);
            throw;
        }
    }

    private async Task<string> ResolveSkuAsync(string? requested, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var normalized = ProductSku.Normalize(requested);
            if (!ProductSku.IsValidNewSku(normalized))
                throw new ValidationException(new Dictionary<string, string[]> { ["sku"] = ["SKU must be exactly four uppercase letters or digits."] });
            if (await _repository.SkuExistsAsync(normalized, cancellationToken: cancellationToken))
                throw new ConflictException("SKU already exists.");
            return normalized;
        }

        try
        {
            return await ProductSku.GenerateUniqueAsync(
                (candidate, token) => _repository.SkuExistsAsync(candidate, cancellationToken: token),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new ConflictException("A unique four-character SKU could not be generated. Please retry.");
        }
    }

    private async Task AddImagesAsync(
        Product product,
        CreateProductCommand command,
        string sku,
        ICollection<string> uploadedUrls,
        CancellationToken cancellationToken)
    {
        foreach (var (image, index) in command.Images.Select((image, index) => (image, index)))
        {
            var sortOrder = image.SortOrder ?? index;
            if (image.ImageFile is { Length: > 0 })
            {
                var objectPath = BuildProductObjectPath(sku, image.ImageFile.FileName, $"image-{index}");
                await using var source = image.ImageFile.OpenReadStream();
                await using var stream = await ProductImageWatermarker.ApplyAsync(source, sku, image.ImageFile.ContentType, cancellationToken);
                var url = await _fileStorage.UploadPublicAsync(
                    stream,
                    objectPath,
                    image.ImageFile.ContentType,
                    cancellationToken);
                uploadedUrls.Add(url);
                product.AddImage(url, sortOrder);
            }
            else if (!string.IsNullOrWhiteSpace(image.ImageUrl))
            {
                product.AddImage(image.ImageUrl, sortOrder);
            }
        }
    }

    private async Task CleanupUploadsAsync(
        IEnumerable<string> uploadedUrls,
        CancellationToken cancellationToken)
    {
        foreach (var url in uploadedUrls)
        {
            try
            {
                await _fileStorage.DeleteAsync(url, cancellationToken);
            }
            catch
            {
                // Preserve the original operation failure.
            }
        }
    }

    private static string BuildProductObjectPath(string sku, string? originalFileName, string fallbackName)
    {
        var safeSku = string.IsNullOrWhiteSpace(sku) ? "unknown-sku" : SanitizeSegment(sku);
        var fileName = string.IsNullOrWhiteSpace(originalFileName) ? fallbackName : originalFileName;
        var safeName = SanitizeSegment(Path.GetFileName(fileName));
        var extension = Path.GetExtension(safeName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
        var stamp = Guid.NewGuid().ToString("N");
        var finalName = string.IsNullOrWhiteSpace(extension)
            ? $"{nameWithoutExtension}-{stamp}"
            : $"{nameWithoutExtension}-{stamp}{extension}";
        return $"products/{safeSku}/{finalName}";
    }

    private static string SanitizeSegment(string value)
    {
        var cleaned = value.Trim().Replace(' ', '-').Replace('\\', '-').Replace('/', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "x" : cleaned;
    }
}
