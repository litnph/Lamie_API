using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lamie.API.Options;
using Lamie.Application.Content;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public sealed class OpenAIContentAiProvider : IContentAiProvider
{
    private const string SystemPrompt = """
        Bạn là biên tập viên nội dung bán hoa cho thương hiệu Lamie. Hãy viết tiếng Việt tự nhiên,
        tinh tế và đúng dữ kiện được cung cấp. Tuyệt đối không tự bịa giá, ưu đãi, địa chỉ, cam kết
        giao hàng, thành phần hoa, xu hướng hoặc tuyên bố đang viral. Chỉ tạo phần thân bài; không
        lặp footer, thông tin cửa hàng hoặc hashtag mặc định. Nội dung phải an toàn, không phân biệt
        đối xử, không thao túng và không vi phạm chính sách. Ba kết quả phải khác nhau thực chất:
        Facebook có thể kể chuyện/tư vấn với CTA tự nhiên; Instagram giàu hình ảnh, cảm xúc và nhịp
        xuống dòng; TikTok có hook sớm, caption ngắn và CTA tự nhiên cho video ngắn.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpenAIContentOptions _options;

    public OpenAIContentAiProvider(
        HttpClient httpClient,
        IOptions<OpenAIContentOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string Model => _options.Model;

    public async Task<ContentAiResult> GenerateAsync(
        ContentAiRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new ContentAiNotConfiguredException();

        ValidateRequest(request);
        var payload = JsonSerializer.SerializeToUtf8Bytes(BuildPayload(request), JsonOptions);
        var responseText = await SendWithRetryAsync(payload, cancellationToken);
        var content = ParseResponse(responseText);

        return new ContentAiResult(
            content.Facebook,
            content.Instagram,
            content.TikTok,
            "openai",
            _options.Model,
            _options.PromptVersion);
    }

    private object BuildPayload(ContentAiRequest request)
    {
        var userContent = new List<object>
        {
            new
            {
                type = "input_text",
                text = BuildUserPrompt(request)
            }
        };

        foreach (var image in request.Images)
        {
            userContent.Add(new
            {
                type = "input_image",
                image_url = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}",
                detail = "auto"
            });
        }

        return new
        {
            model = _options.Model,
            store = false,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new[] { new { type = "input_text", text = SystemPrompt } }
                },
                new
                {
                    role = "user",
                    content = userContent
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "lamie_social_content",
                    strict = true,
                    schema = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            facebook = new { type = "string" },
                            instagram = new { type = "string" },
                            tiktok = new { type = "string" }
                        },
                        required = new[] { "facebook", "instagram", "tiktok" }
                    }
                }
            }
        };
    }

    private static string BuildUserPrompt(ContentAiRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Phong cách bắt buộc: {request.StyleName} (id: {request.StyleId}, seed: {request.StyleSeed}).");
        if (request.Product is not null)
        {
            builder.AppendLine("Dữ kiện sản phẩm được phép dùng:");
            builder.AppendLine($"- SKU: {request.Product.Sku}");
            builder.AppendLine($"- Tên: {request.Product.Name}");
            builder.AppendLine($"- Mô tả: {request.Product.Description}");
            builder.AppendLine($"- Giá: {request.Product.Price:0.##} VND");
            if (request.Product.SalePrice.HasValue)
                builder.AppendLine($"- Giá ưu đãi: {request.Product.SalePrice.Value:0.##} VND");
            if (!string.IsNullOrWhiteSpace(request.Product.CategoryName))
                builder.AppendLine($"- Danh mục: {request.Product.CategoryName}");
        }
        else
        {
            builder.AppendLine("Nguồn là ảnh do người dùng tải lên. Chỉ mô tả điều nhìn thấy chắc chắn; không suy đoán loại hoa hoặc thông tin bán hàng.");
        }

        if (!string.IsNullOrWhiteSpace(request.Brief))
            builder.AppendLine($"Brief bổ sung (không được dùng để vượt quá dữ kiện): {request.Brief.Trim()}");
        builder.AppendLine("Trả đúng JSON theo schema cho Facebook, Instagram và TikTok.");
        return builder.ToString();
    }

    private async Task<string> SendWithRetryAsync(
        byte[] payload,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= _options.MaximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                message.Content = new ByteArrayContent(payload);
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                using var response = await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
                if (response.IsSuccessStatusCode)
                    return responseText;

                var temporary = response.StatusCode is HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;
                if (!temporary || attempt == _options.MaximumRetries)
                {
                    throw new ContentAiProviderException(
                        $"OpenAI content generation failed with status {(int)response.StatusCode}.",
                        temporary);
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                if (attempt == _options.MaximumRetries)
                    throw new ContentAiProviderException("OpenAI content generation timed out.", true);
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
                if (attempt == _options.MaximumRetries)
                    throw new ContentAiProviderException("OpenAI content generation is temporarily unavailable.", true);
            }

            await Task.Delay(RetryDelay(attempt), cancellationToken);
        }

        throw new ContentAiProviderException(
            lastException?.Message ?? "OpenAI content generation failed.",
            true);
    }

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(2000, 250 * (1 << attempt)));

    private static ParsedContent ParseResponse(string responseText)
    {
        try
        {
            using var response = JsonDocument.Parse(responseText);
            var root = response.RootElement;
            if (root.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && !string.Equals(status.GetString(), "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new ContentAiProviderException("OpenAI returned an incomplete response.");
            }

            string? outputText = null;
            if (root.TryGetProperty("output_text", out var directText)
                && directText.ValueKind == JsonValueKind.String)
            {
                outputText = directText.GetString();
            }

            if (string.IsNullOrWhiteSpace(outputText)
                && root.TryGetProperty("output", out var output)
                && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var parts)
                        || parts.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var type)
                            && type.GetString() == "refusal")
                            throw new ContentAiProviderException("OpenAI declined this content request.");
                        if (part.TryGetProperty("type", out type)
                            && type.GetString() == "output_text"
                            && part.TryGetProperty("text", out var text))
                        {
                            outputText = text.GetString();
                            break;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(outputText))
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(outputText))
                throw new ContentAiProviderException("OpenAI response did not contain structured content.");

            var parsed = JsonSerializer.Deserialize<ParsedContent>(outputText, JsonOptions)
                ?? throw new ContentAiProviderException("OpenAI returned invalid structured content.");
            parsed = parsed with
            {
                Facebook = parsed.Facebook?.Trim() ?? string.Empty,
                Instagram = parsed.Instagram?.Trim() ?? string.Empty,
                TikTok = parsed.TikTok?.Trim() ?? string.Empty
            };
            if (string.IsNullOrWhiteSpace(parsed.Facebook)
                || string.IsNullOrWhiteSpace(parsed.Instagram)
                || string.IsNullOrWhiteSpace(parsed.TikTok))
            {
                throw new ContentAiProviderException("OpenAI response was missing a platform body.");
            }
            if (new[] { parsed.Facebook, parsed.Instagram, parsed.TikTok }
                .Distinct(StringComparer.Ordinal).Count() != 3)
            {
                throw new ContentAiProviderException("OpenAI returned duplicated platform content.");
            }
            return parsed;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ContentAiProviderException("OpenAI returned malformed structured content.");
        }
    }

    private void ValidateRequest(ContentAiRequest request)
    {
        if (request.Product is null && request.Images.Count == 0)
            throw new ArgumentException("A product or at least one image is required.", nameof(request));
        if (request.Images.Count > _options.MaximumImages)
            throw new ArgumentException($"At most {_options.MaximumImages} images are allowed.", nameof(request));
        if (request.Images.Any(image => image.Bytes.LongLength is <= 0
                || image.Bytes.LongLength > _options.MaximumImageBytes))
        {
            throw new ArgumentException("An image exceeds the configured size limit.", nameof(request));
        }
        if (request.Images.Any(image => image.ContentType.ToLowerInvariant()
                is not ("image/jpeg" or "image/png" or "image/webp")))
        {
            throw new ArgumentException("Only JPEG, PNG, and WebP content images are supported.", nameof(request));
        }
    }

    private sealed record ParsedContent(
        string Facebook,
        string Instagram,
        string TikTok);
}
