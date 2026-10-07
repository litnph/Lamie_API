using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;

namespace Lamie.API.Services;

public sealed class ConfiguredProductImageWatermarkService : IProductImageWatermarkService
{
    private readonly AppDbContext _dbContext;
    private readonly IPublicFileReader _fileReader;
    private readonly ILogger<ConfiguredProductImageWatermarkService> _logger;

    public ConfiguredProductImageWatermarkService(
        AppDbContext dbContext,
        IPublicFileReader fileReader,
        ILogger<ConfiguredProductImageWatermarkService> logger)
    {
        _dbContext = dbContext;
        _fileReader = fileReader;
        _logger = logger;
    }

    public async Task<MemoryStream> ApplyAsync(
        Stream source,
        string sku,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.ProductCatalogSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == ProductCatalogSettings.SingletonId, cancellationToken);
        if (settings is null)
            return await ProductImageWatermarker.ApplySkuAsync(source, sku, contentType, cancellationToken);
        if (!settings.WatermarkEnabled)
            return await ProductImageWatermarker.CopyAsync(source, cancellationToken);
        if (settings.WatermarkSource == ProductWatermarkSource.Sku)
            return await ProductImageWatermarker.ApplySkuAsync(source, sku, contentType, cancellationToken);

        var assetUrl = settings.WatermarkSource == ProductWatermarkSource.ShopLogo
            ? settings.ShopLogoUrl
            : settings.WatermarkImageUrl;
        var asset = await _fileReader.ReadPublicAsync(assetUrl, cancellationToken);
        if (asset is null)
        {
            _logger.LogWarning(
                "Product watermark source {Source} has no readable configured asset; the clean product image is retained.",
                settings.WatermarkSource);
            return await ProductImageWatermarker.CopyAsync(source, cancellationToken);
        }

        try
        {
            using var decodedWatermark = Image.Load(asset.Bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Product watermark source {Source} is not a readable image; the clean product image is retained.",
                settings.WatermarkSource);
            return await ProductImageWatermarker.CopyAsync(source, cancellationToken);
        }

        await using var watermark = new MemoryStream(asset.Bytes, writable: false);
        return await ProductImageWatermarker.ApplyImageAsync(source, watermark, contentType, cancellationToken);
    }
}
