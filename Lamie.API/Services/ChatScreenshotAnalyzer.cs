using Lamie.Application.ChatAnalysis;
using Microsoft.Extensions.Options;

namespace Lamie.API.Services;

public sealed class ChatScreenshotAnalyzer : IChatScreenshotAnalyzer
{
    private readonly IChatOcrProvider _ocr;
    private readonly IChatImagePreprocessor _preprocessor;
    private readonly IConversationHeaderExtractor _headerExtractor;
    private readonly IReadOnlyList<IChatPlatformDetector> _platformDetectors;
    private readonly ChatScreenshotAnalysisOptions _options;

    public ChatScreenshotAnalyzer(
        IChatOcrProvider ocr,
        IChatImagePreprocessor preprocessor,
        IConversationHeaderExtractor headerExtractor,
        IEnumerable<IChatPlatformDetector> platformDetectors,
        IOptions<ChatScreenshotAnalysisOptions> options)
    {
        _ocr = ocr;
        _preprocessor = preprocessor;
        _headerExtractor = headerExtractor;
        _platformDetectors = platformDetectors.ToList();
        _options = options.Value;
        if (_platformDetectors.Count == 0)
            throw new InvalidOperationException("At least one chat platform detector is required.");
    }

    public ChatScreenshotAnalyzer(IChatOcrProvider ocr)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ChatScreenshotAnalysisOptions());
        var preprocessor = new ChatImagePreprocessor(options);
        _ocr = ocr;
        _preprocessor = preprocessor;
        _headerExtractor = new ConversationHeaderExtractor(ocr, preprocessor);
        _platformDetectors =
        [
            new ZaloChatPlatformDetector(),
            new MetaMessengerChatPlatformDetector(),
            new TikTokChatPlatformDetector()
        ];
        _options = options.Value;
    }

    public async Task<ChatScreenshotAnalysis> AnalyzeAsync(
        IReadOnlyList<ChatScreenshotInput> images,
        CancellationToken cancellationToken)
    {
        var items = new List<ChatScreenshotItemAnalysis>(images.Count);
        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                items.Add(await AnalyzeOneAsync(image, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                items.Add(new ChatScreenshotItemAnalysis(
                    image.ScreenshotId,
                    image.FileName,
                    ChatPlatform.Unknown,
                    0m,
                    null,
                    0m,
                    [],
                    [],
                    ["Không thể phân tích ảnh này. Bạn vẫn có thể nhập thông tin đơn hàng thủ công."]));
            }
        }

        return Aggregate(items);
    }

    private async Task<ChatScreenshotItemAnalysis> AnalyzeOneAsync(
        ChatScreenshotInput image,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var prepared = await _preprocessor.PreparePlatformSignalsAsync(image, cancellationToken);
        IReadOnlyList<OcrTextLine> signalLines;
        try
        {
            signalLines = await _ocr.ReadAsync(prepared.OcrInput, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            signalLines = [];
            warnings.Add("Không đọc được các dấu hiệu nền tảng bằng OCR; kết quả nguồn chat có thể không chắc chắn.");
        }

        var context = new ChatPlatformDetectionContext(
            ChatTextNormalization.Normalize(string.Join(' ', signalLines.Select(line => line.Text))),
            prepared.VisualFeatures);
        var detections = _platformDetectors
            .Select(detector => detector.Detect(context))
            .OrderByDescending(detection => detection.Score)
            .ToList();
        var best = detections[0];
        var secondScore = detections.Count > 1 ? detections[1].Score : 0m;
        var margin = best.Score - secondScore;
        var platform = best.Score >= _options.PlatformMinimumScore
            && margin >= _options.PlatformMinimumMargin
                ? best.Platform
                : ChatPlatform.Unknown;
        var platformConfidence = platform == ChatPlatform.Unknown
            ? Math.Min(.49m, best.Score * .65m)
            : Math.Min(.96m, .52m + Math.Min(1m, best.Score) * .38m + Math.Min(.06m, margin * .10m));
        if (platform == ChatPlatform.Unknown)
            warnings.Add("Không đủ bằng chứng kết hợp để xác định nguồn chat.");

        ConversationHeaderExtraction header;
        try
        {
            header = await _headerExtractor.ExtractAsync(image, platform, cancellationToken);
            warnings.AddRange(header.Warnings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            header = new ConversationHeaderExtraction(
                null,
                0m,
                [],
                ["Không thể đọc vùng tiêu đề cuộc trò chuyện; hệ thống không đoán tên từ nội dung chat."]);
            warnings.AddRange(header.Warnings);
        }

        return new ChatScreenshotItemAnalysis(
            image.ScreenshotId,
            image.FileName,
            platform,
            platformConfidence,
            header.Name,
            header.Confidence,
            header.HeaderTexts,
            platform == ChatPlatform.Unknown ? [] : best.Evidence,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    private static ChatScreenshotAnalysis Aggregate(IReadOnlyList<ChatScreenshotItemAnalysis> items)
    {
        var warnings = items.SelectMany(item => item.Warnings).Distinct(StringComparer.Ordinal).ToList();
        var platforms = items
            .Where(item => item.DetectedPlatform != ChatPlatform.Unknown)
            .GroupBy(item => item.DetectedPlatform)
            .ToList();
        var platform = ChatPlatform.Unknown;
        var platformConfidence = 0m;
        if (platforms.Count == 1)
        {
            platform = platforms[0].Key;
            platformConfidence = CombineConfidence(platforms[0].Select(item => item.PlatformConfidence));
        }
        else if (platforms.Count > 1)
        {
            warnings.Add("Các ảnh có thông tin nguồn chat không đồng nhất. Vui lòng chọn kênh bán thủ công.");
        }

        var names = items
            .Where(item => item.DetectedOrdererName is not null)
            .GroupBy(item => ChatTextNormalization.Normalize(item.DetectedOrdererName!), StringComparer.Ordinal)
            .ToList();
        string? name = null;
        var nameConfidence = 0m;
        if (names.Count == 1)
        {
            name = names[0].OrderByDescending(item => item.NameConfidence).First().DetectedOrdererName;
            nameConfidence = CombineConfidence(names[0].Select(item => item.NameConfidence));
        }
        else if (names.Count > 1)
        {
            warnings.Add("Các ảnh có tiêu đề cuộc trò chuyện không đồng nhất. Vui lòng kiểm tra Người đặt.");
        }

        return new ChatScreenshotAnalysis(
            platform,
            platformConfidence,
            name,
            nameConfidence,
            items,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    private static decimal CombineConfidence(IEnumerable<decimal> values)
    {
        var inverse = values.Aggregate(
            1m,
            (current, value) => current * (1m - Math.Clamp(value, 0m, .99m)));
        return Math.Min(.99m, 1m - inverse);
    }
}
