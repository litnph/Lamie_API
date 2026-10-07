namespace Lamie.Application.Common.Uploads;

public interface IProductImageWatermarkService
{
    Task<MemoryStream> ApplyAsync(
        Stream source,
        string sku,
        string contentType,
        CancellationToken cancellationToken = default);
}

public sealed class SkuProductImageWatermarkService : IProductImageWatermarkService
{
    public Task<MemoryStream> ApplyAsync(
        Stream source,
        string sku,
        string contentType,
        CancellationToken cancellationToken = default) =>
        ProductImageWatermarker.ApplySkuAsync(source, sku, contentType, cancellationToken);
}
