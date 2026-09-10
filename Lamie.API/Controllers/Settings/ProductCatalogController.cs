using Lamie.API.Services;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Identity;
using Lamie.Application.Settings.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ProductsView)]
[Route("api/settings/products/catalog")]
public sealed class ProductCatalogController : ControllerBase
{
    private readonly IProductCatalogFeatureService _service;

    public ProductCatalogController(IProductCatalogFeatureService service)
    {
        _service = service;
    }

    [HttpGet("settings")]
    public Task<ProductCatalogSettingsDto> GetSettings(CancellationToken cancellationToken) =>
        _service.GetSettingsAsync(cancellationToken);

    [HttpPut("settings")]
    [Authorize(Policy = PermissionNames.ProductsManage)]
    public Task<ProductCatalogSettingsDto> UpdateSettings(
        UpdateProductCatalogSettingsRequest request,
        CancellationToken cancellationToken) =>
        _service.UpdateSettingsAsync(request, cancellationToken);

    [HttpGet("recognition/status")]
    public Task<ProductRecognitionIndexStatusDto> RecognitionStatus(
        CancellationToken cancellationToken) =>
        _service.GetRecognitionStatusAsync(cancellationToken);

    [HttpPost("recognition")]
    [Authorize(Policy = PermissionNames.ProductsManage)]
    [EnableRateLimiting("product-recognition")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ImageUploadPolicy.MaximumFileBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageUploadPolicy.MaximumFileBytes + 1024 * 1024)]
    public async Task<ActionResult<ProductRecognitionResponseDto>> Recognize(
        [FromForm] ProductRecognitionForm form,
        CancellationToken cancellationToken)
    {
        if (form.Image is null
            || !ImageUploadPolicy.HasAllowedMetadata(form.Image)
            || !await ImageUploadPolicy.HasValidSignatureAsync(form.Image, cancellationToken)
            || string.Equals(form.Image.ContentType, "image/gif", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidImage(
                "Ảnh không hợp lệ. Chỉ dùng JPG, PNG hoặc WebP không quá 10 MB.");
        }

        await using var input = form.Image.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var response = await _service.RecognizeAsync(
            new ProductRecognitionUpload(
                Path.GetFileName(form.Image.FileName),
                form.Image.ContentType,
                buffer.ToArray()),
            cancellationToken);
        return Ok(response);
    }

    [HttpPost("recognition/backfill")]
    [Authorize(Policy = PermissionNames.ProductsManage)]
    public Task<ProductRecognitionBackfillResultDto> BackfillRecognition(
        [FromQuery] int? batchSize,
        CancellationToken cancellationToken) =>
        _service.BackfillRecognitionAsync(batchSize, cancellationToken);

    private BadRequestObjectResult InvalidImage(string message) => BadRequest(new
    {
        success = false,
        code = "VALIDATION_ERROR",
        message = "Validation failed",
        errors = new Dictionary<string, string[]> { ["image"] = [message] }
    });
}

public sealed class ProductRecognitionForm
{
    public IFormFile? Image { get; init; }
}
