using Lamie.API.Services;
using Lamie.API.Options;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Content;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ContentView)]
[Route("api/content")]
public sealed class ContentController : ControllerBase
{
    private readonly IContentPublishingService _contentService;
    private readonly OpenAIContentOptions _options;

    public ContentController(
        IContentPublishingService contentService,
        IOptions<OpenAIContentOptions> options)
    {
        _contentService = contentService;
        _options = options.Value;
    }

    [HttpGet("configuration")]
    public ActionResult<ContentConfigurationDto> Configuration() =>
        Ok(_contentService.GetConfiguration());

    [HttpGet("footers")]
    public Task<IReadOnlyList<ContentFooterSettingDto>> Footers(
        CancellationToken cancellationToken) =>
        _contentService.GetFootersAsync(cancellationToken);

    [HttpPut("footers/{platform}")]
    [Authorize(Policy = PermissionNames.ContentManage)]
    public Task<ContentFooterSettingDto> UpsertFooter(
        ContentPlatform platform,
        UpdateContentFooterRequest request,
        CancellationToken cancellationToken) =>
        _contentService.UpsertFooterAsync(platform, request, cancellationToken);

    [HttpPost("generate")]
    [Authorize(Policy = PermissionNames.ContentManage)]
    [EnableRateLimiting("content-generation")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(42 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 42 * 1024 * 1024)]
    public async Task<ActionResult<ContentGenerationDto>> Generate(
        [FromForm] GenerateContentForm form,
        CancellationToken cancellationToken)
    {
        if (form.Images.Count > _options.MaximumImages)
            return InvalidRequest("images", $"Chỉ được tải tối đa {_options.MaximumImages} ảnh.");

        var uploads = new List<ContentUpload>(form.Images.Count);
        foreach (var file in form.Images)
        {
            if (!ImageUploadPolicy.HasAllowedMetadata(file)
                || !await ImageUploadPolicy.HasValidSignatureAsync(file, cancellationToken)
                || string.Equals(file.ContentType, "image/gif", StringComparison.OrdinalIgnoreCase))
            {
                return InvalidRequest(
                    "images",
                    $"Ảnh '{Path.GetFileName(file.FileName)}' không hợp lệ. Chỉ dùng JPG, PNG hoặc WebP không quá 10 MB.");
            }

            await using var input = file.OpenReadStream();
            await using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, cancellationToken);
            uploads.Add(new ContentUpload(
                Path.GetFileName(file.FileName),
                file.ContentType,
                buffer.ToArray()));
        }

        var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var values)
            ? values.ToString()
            : null;
        var result = await _contentService.GenerateAsync(
            new GenerateContentRequest(form.ProductId, form.Brief, uploads, idempotencyKey),
            cancellationToken);
        return Ok(result);
    }

    [HttpPut("generations/{generationId:guid}/save")]
    [Authorize(Policy = PermissionNames.ContentManage)]
    public Task<ContentGenerationDto> Save(
        Guid generationId,
        SaveContentRequest request,
        CancellationToken cancellationToken) =>
        _contentService.SaveAsync(generationId, request, cancellationToken);

    [HttpGet("generations/{generationId:guid}")]
    public Task<ContentGenerationDto> Get(
        Guid generationId,
        CancellationToken cancellationToken) =>
        _contentService.GetAsync(generationId, cancellationToken);

    [HttpGet("history")]
    public Task<PagedContentHistoryDto> History(
        [FromQuery] ContentHistoryQuery query,
        CancellationToken cancellationToken) =>
        _contentService.GetHistoryAsync(query, cancellationToken);

    private BadRequestObjectResult InvalidRequest(string field, string message) => BadRequest(new
    {
        success = false,
        code = "VALIDATION_ERROR",
        message = "Validation failed",
        errors = new Dictionary<string, string[]> { [field] = new[] { message } }
    });
}

public sealed class GenerateContentForm
{
    public int? ProductId { get; init; }
    public string? Brief { get; init; }
    public List<IFormFile> Images { get; init; } = [];
}
