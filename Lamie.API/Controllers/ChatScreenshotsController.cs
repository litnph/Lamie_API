using Lamie.API.Services;
using Lamie.Application.ChatAnalysis;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.OrdersManage)]
[Route("api/admin/quick-import")]
public sealed class ChatScreenshotsController : ControllerBase
{
    private readonly IChatScreenshotAnalyzer _analyzer;
    private readonly ChatScreenshotAnalysisOptions _options;

    public ChatScreenshotsController(
        IChatScreenshotAnalyzer analyzer,
        IOptions<ChatScreenshotAnalysisOptions> options)
    {
        _analyzer = analyzer;
        _options = options.Value;
    }

    [HttpPost("analyze-screenshots")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(105 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 105 * 1024 * 1024)]
    public async Task<ActionResult<ChatScreenshotAnalysis>> Analyze(
        [FromForm] AnalyzeChatScreenshotsForm form,
        CancellationToken cancellationToken)
    {
        var files = form.Files;
        if (files.Count is < 1)
            return InvalidRequest("files", "Cần ít nhất một screenshot chat.");
        if (files.Count > _options.MaximumFiles)
            return InvalidRequest("files", $"Chỉ phân tích tối đa {_options.MaximumFiles} screenshot mỗi đơn.");
        if (form.ScreenshotIds.Count > 0 && form.ScreenshotIds.Count != files.Count)
            return InvalidRequest("screenshotIds", "Số screenshot id phải khớp với số file.");

        var inputs = new List<ChatScreenshotInput>(files.Count);
        var screenshotIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            if (file.Length <= 0 || file.Length > _options.MaximumFileBytes
                || !ImageUploadPolicy.HasAllowedMetadata(file)
                || !await ImageUploadPolicy.HasValidSignatureAsync(file, cancellationToken))
                return InvalidRequest("files", $"Ảnh '{Path.GetFileName(file.FileName)}' không hợp lệ.");

            var screenshotId = form.ScreenshotIds.Count == 0
                ? $"screenshot-{index + 1}"
                : form.ScreenshotIds[index].Trim();
            if (string.IsNullOrWhiteSpace(screenshotId) || screenshotId.Length > 120)
                return InvalidRequest("screenshotIds", "Screenshot id phải có từ 1 đến 120 ký tự.");
            if (!screenshotIds.Add(screenshotId))
                return InvalidRequest("screenshotIds", "Screenshot id không được trùng nhau.");

            await using var input = file.OpenReadStream();
            await using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, cancellationToken);
            inputs.Add(new ChatScreenshotInput(
                screenshotId,
                Path.GetFileName(file.FileName),
                file.ContentType,
                buffer.ToArray()));
        }

        return Ok(await _analyzer.AnalyzeAsync(inputs, cancellationToken));
    }

    private BadRequestObjectResult InvalidRequest(string field, string message) => BadRequest(new
    {
        success = false,
        code = "VALIDATION_ERROR",
        message = "Validation failed",
        errors = new Dictionary<string, string[]> { [field] = [message] }
    });
}

public sealed class AnalyzeChatScreenshotsForm
{
    public List<IFormFile> Files { get; init; } = [];
    public List<string> ScreenshotIds { get; init; } = [];
}
