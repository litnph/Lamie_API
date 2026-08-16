using System.Text.RegularExpressions;
using Lamie.Application.ChatAnalysis;

namespace Lamie.API.Services;

public sealed record ConversationHeaderExtraction(
    string? Name,
    decimal Confidence,
    IReadOnlyList<string> HeaderTexts,
    IReadOnlyList<string> Warnings);

public interface IConversationHeaderExtractor
{
    Task<ConversationHeaderExtraction> ExtractAsync(
        ChatScreenshotInput input,
        ChatPlatform platform,
        CancellationToken cancellationToken);
}

public sealed partial class ConversationHeaderExtractor : IConversationHeaderExtractor
{
    private static readonly string[] ExcludedTerms =
    [
        "zalo", "messenger", "facebook", "meta", "tiktok", "online", "active now",
        "dang hoat dong", "truy cap", "goi dien", "video", "tim kiem", "thong tin",
        "tro lai", "tin nhan", "cuoc tro chuyen", "business suite", "follow", "followers",
        "nguoi la", "dang cho duoc dong y ket ban", "dong y", "ket ban"
    ];

    private readonly IChatOcrProvider _ocr;
    private readonly IChatImagePreprocessor _preprocessor;

    public ConversationHeaderExtractor(IChatOcrProvider ocr, IChatImagePreprocessor preprocessor)
    {
        _ocr = ocr;
        _preprocessor = preprocessor;
    }

    public async Task<ConversationHeaderExtraction> ExtractAsync(
        ChatScreenshotInput input,
        ChatPlatform platform,
        CancellationToken cancellationToken)
    {
        var prepared = await _preprocessor.PrepareHeaderAsync(input, platform, cancellationToken);
        var lines = await _ocr.ReadAsync(prepared, cancellationToken);
        var headerTexts = lines
            .Select(line => CleanName(line.Text))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToList();

        var candidate = lines
            .Select(line => new
            {
                Line = line,
                Text = CleanName(line.Text),
                Normalized = ChatTextNormalization.Normalize(line.Text)
            })
            .Where(candidate => candidate.Text.Length is >= 2 and <= 80)
            .Where(candidate => candidate.Text.Any(char.IsLetter))
            .Where(candidate => candidate.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8)
            .Where(candidate => !ExcludedTerms.Any(term =>
                candidate.Normalized.Contains(term, StringComparison.Ordinal)))
            .Where(candidate => !TimeOrDeviceStatus().IsMatch(candidate.Text))
            .Select(candidate => new
            {
                candidate.Text,
                Score = NameScore(candidate.Line)
            })
            .OrderByDescending(candidate => candidate.Score)
            .FirstOrDefault();

        if (candidate is null || candidate.Score < .42m)
        {
            return new ConversationHeaderExtraction(
                null,
                candidate?.Score ?? 0m,
                headerTexts,
                ["Không đọc được tên chắc chắn trong vùng tiêu đề cuộc trò chuyện; hệ thống không lấy tên từ nội dung chat."]);
        }

        return new ConversationHeaderExtraction(
            candidate.Text,
            Math.Min(.98m, candidate.Score),
            headerTexts,
            []);
    }

    private static decimal NameScore(OcrTextLine line)
    {
        var heightTarget = Math.Max(1m, line.ImageHeight * .20m);
        var heightScore = Math.Clamp(line.Height / heightTarget, 0m, 1m);
        var center = (line.Left + line.Width / 2m) / Math.Max(1m, line.ImageWidth);
        var positionScore = Math.Clamp(1m - Math.Abs(center - .38m) / .62m, 0m, 1m);
        return Math.Clamp(
            line.Confidence * .68m + heightScore * .20m + positionScore * .12m,
            0m,
            .98m);
    }

    private static string CleanName(string value)
    {
        var collapsed = Whitespace().Replace(value.Trim(), " ");
        return EdgeNoise().Replace(collapsed, string.Empty).Trim();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(^[^\p{L}\p{N}]+)|([^\p{L}\p{N}]+$)", RegexOptions.CultureInvariant)]
    private static partial Regex EdgeNoise();

    [GeneratedRegex(@"^(\d{1,2}[:.]\d{2}|\d{1,3}%|\d+[gG]|online|active|đang hoạt động|truy cập|wifi|lte|5g).*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeOrDeviceStatus();
}
