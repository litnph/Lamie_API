using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;
using Lamie.Application.Common.Uploads;

namespace Lamie.Application.Settings.Products.Commands;

public sealed class UpdateProductHandler : IRequestHandler<UpdateProductCommand>
{
    private readonly IProductRepository _repository;
    private readonly IFileStorage _fileStorage;
    private readonly IProductTypeRepository _productTypeRepository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public UpdateProductHandler(
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

    public async Task Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(command.Id);
        if (product is null)
        {
            throw new NotFoundException("Product", command.Id);
        }

        // SKU identifies already-watermarked catalog media and is immutable after creation.
        if (!string.Equals(command.Sku?.Trim(), product.Sku, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException(new Dictionary<string, string[]> { ["sku"] = ["SKU cannot be changed after product creation."] });
        command.Sku = product.Sku;

        var productType = await _productTypeRepository.GetByIdAsync(command.ProductTypeId);
        if (productType is null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["productTypeId"] = ["ProductTypeId does not exist"]
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

        product.UpdateDetails(
            command.Sku,
            command.Stock,
            command.CategoryId,
            command.ProductTypeId,
            command.TracksInventory ?? product.TracksInventory);
        product.UpdatePricing(command.Price, command.SalePrice);
        if (command.IsFullReplacement)
        {
            product.ReplaceTranslations(command.Translations.Select(translation => (
                translation.LanguageCode,
                translation.Name,
                translation.Slug,
                translation.Description ?? string.Empty)));
            product.ReplaceTagIds(command.TagIds);
            product.ReplaceColorIds(command.ColorIds);
            product.ReplaceCollectionIds(command.CollectionIds);
            product.ReplaceStyleIds(command.StyleIds);
            product.ReplaceOccasionIds(command.OccasionIds);
        }

        var obsoleteUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uploadedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await UpdateThumbnailAsync(product, command, obsoleteUrls, uploadedUrls, cancellationToken);
            await UpdateImagesAsync(product, command, obsoleteUrls, uploadedUrls, cancellationToken);

            if (string.IsNullOrWhiteSpace(product.ThumbnailUrl))
            {
                var firstImage = product.Images
                    .Where(image => image.IsActive)
                    .OrderBy(image => image.SortOrder)
                    .FirstOrDefault();
                product.SetThumbnail(firstImage?.ImageUrl);
            }

            await _repository.UpdateAsync(product);
        }
        catch
        {
            await CleanupUploadsAsync(uploadedUrls, CancellationToken.None);
            throw;
        }

        var retainedUrls = product.Images
            .Where(image => image.IsActive)
            .Select(image => image.ImageUrl)
            .Append(product.ThumbnailUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var obsoleteUrl in obsoleteUrls.Where(url => !retainedUrls.Contains(url)))
        {
            await _fileStorage.DeleteAsync(obsoleteUrl, cancellationToken);
        }
    }

    private async Task UpdateThumbnailAsync(
        Product product,
        UpdateProductCommand command,
        ISet<string> obsoleteUrls,
        ISet<string> uploadedUrls,
        CancellationToken cancellationToken)
    {
        var previousUrl = product.ThumbnailUrl;
        if (command.ThumbnailFile is { Length: > 0 })
        {
            var objectPath = BuildProductObjectPath(
                command.Sku,
                command.ThumbnailFile.FileName,
                "thumbnail");
            await using var source = command.ThumbnailFile.OpenReadStream();
            await using var stream = await ProductImageWatermarker.ApplyAsync(source, product.Sku, command.ThumbnailFile.ContentType ?? "image/jpeg", cancellationToken);
            var url = await _fileStorage.UploadPublicAsync(
                stream,
                objectPath,
                command.ThumbnailFile.ContentType ?? "application/octet-stream",
                cancellationToken);
            uploadedUrls.Add(url);
            product.SetThumbnail(url);
        }
        else if (command.IsFullReplacement || !string.IsNullOrWhiteSpace(command.ThumbnailUrl))
        {
            product.SetThumbnail(command.ThumbnailUrl);
        }

        if (!string.IsNullOrWhiteSpace(previousUrl)
            && !string.Equals(previousUrl, product.ThumbnailUrl, StringComparison.OrdinalIgnoreCase))
        {
            obsoleteUrls.Add(previousUrl);
        }
    }

    private async Task UpdateImagesAsync(
        Product product,
        UpdateProductCommand command,
        ISet<string> obsoleteUrls,
        ISet<string> uploadedUrls,
        CancellationToken cancellationToken)
    {
        if (!command.IsFullReplacement && command.Images.Count == 0)
        {
            return;
        }

        var existingImages = product.Images.ToDictionary(image => image.Id);
        var incomingIds = command.Images
            .Where(image => image.Id.HasValue)
            .Select(image => image.Id!.Value)
            .ToHashSet();

        foreach (var existingImage in existingImages.Values.Where(image => !incomingIds.Contains(image.Id)))
        {
            if (existingImage.IsActive)
            {
                obsoleteUrls.Add(existingImage.ImageUrl);
            }
            product.DeactivateImage(existingImage.Id);
        }

        foreach (var (imageDto, index) in command.Images.Select((image, index) => (image, index)))
        {
            var sortOrder = imageDto.SortOrder ?? index;
            if (imageDto.Id.HasValue)
            {
                if (!existingImages.TryGetValue(imageDto.Id.Value, out var existingImage))
                {
                    throw new ValidationException(new Dictionary<string, string[]>
                    {
                        ["Images"] = [$"Image with id {imageDto.Id.Value} does not belong to product {product.Id}."]
                    });
                }

                if (imageDto.ImageFile is { Length: > 0 })
                {
                    var url = await UploadImageAsync(
                        product.Sku,
                        imageDto.ImageFile,
                        sortOrder,
                        uploadedUrls,
                        cancellationToken);
                    obsoleteUrls.Add(existingImage.ImageUrl);
                    product.UpdateImage(existingImage.Id, url, sortOrder);
                }
                else
                {
                    if (!string.Equals(
                        existingImage.ImageUrl,
                        imageDto.ImageUrl,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        obsoleteUrls.Add(existingImage.ImageUrl);
                    }
                    product.UpdateImage(existingImage.Id, imageDto.ImageUrl!, sortOrder);
                }

                continue;
            }

            if (imageDto.ImageFile is { Length: > 0 })
            {
                var url = await UploadImageAsync(
                    product.Sku,
                    imageDto.ImageFile,
                    sortOrder,
                    uploadedUrls,
                    cancellationToken);
                product.AddImage(url, sortOrder);
            }
            else
            {
                product.AddImage(imageDto.ImageUrl!, sortOrder);
            }
        }
    }

    private async Task<string> UploadImageAsync(
        string sku,
        Microsoft.AspNetCore.Http.IFormFile file,
        int sortOrder,
        ISet<string> uploadedUrls,
        CancellationToken cancellationToken)
    {
        var objectPath = BuildProductObjectPath(sku, file.FileName, $"image-{sortOrder}");
        await using var source = file.OpenReadStream();
        await using var stream = await ProductImageWatermarker.ApplyAsync(source, sku, file.ContentType ?? "image/jpeg", cancellationToken);
        var url = await _fileStorage.UploadPublicAsync(
            stream,
            objectPath,
            file.ContentType ?? "application/octet-stream",
            cancellationToken);
        uploadedUrls.Add(url);
        return url;
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
