using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lamie.Application.ChatAnalysis;

namespace Lamie.API.Services;

public sealed record ChatPlatformDetectionContext(string NormalizedSignalText, ChatVisualFeatures VisualFeatures);

public sealed record ChatPlatformDetection(
    ChatPlatform Platform,
    decimal Score,
    IReadOnlyList<string> Evidence);

public interface IChatPlatformDetector
{
    ChatPlatform Platform { get; }
    ChatPlatformDetection Detect(ChatPlatformDetectionContext context);
}

public abstract class ChatPlatformDetectorBase : IChatPlatformDetector
{
    public abstract ChatPlatform Platform { get; }

    public ChatPlatformDetection Detect(ChatPlatformDetectionContext context)
    {
        var evidence = new List<string>();
        var score = Score(context, evidence);
        return new ChatPlatformDetection(Platform, Math.Clamp(score, 0m, 1.35m), evidence);
    }

    protected abstract decimal Score(ChatPlatformDetectionContext context, List<string> evidence);

    protected static decimal Anchor(
        string text,
        string anchor,
        decimal weight,
        string evidenceText,
        ICollection<string> evidence)
    {
        if (!text.Contains(anchor, StringComparison.Ordinal))
            return 0m;
        evidence.Add(evidenceText);
        return weight;
    }
}

public sealed class ZaloChatPlatformDetector : ChatPlatformDetectorBase
{
    public override ChatPlatform Platform => ChatPlatform.Zalo;

    protected override decimal Score(ChatPlatformDetectionContext context, List<string> evidence)
    {
        var text = context.NormalizedSignalText;
        var score = 0m;
        score += Anchor(text, "zalo", .92m, "Nhận diện nhãn Zalo.", evidence);
        score += Anchor(text, "hieu ung", .48m, "Có action Hiệu ứng trong composer Zalo.", evidence);
        score += Anchor(text, "eu ung", .30m, "OCR đọc được phần sau của action Hiệu ứng.", evidence);
        score += Anchor(text, "thiep", .38m, "Có action Thiệp trong composer Zalo.", evidence);
        score += Anchor(text, "nhat ky", .30m, "Có nhãn Nhật ký của Zalo.", evidence);
        score += Anchor(text, "truy cap", .12m, "Có trạng thái truy cập của Zalo.", evidence);
        if ((text.Contains("hieu ung", StringComparison.Ordinal)
             || text.Contains("eu ung", StringComparison.Ordinal))
            && text.Contains("thiep", StringComparison.Ordinal))
        {
            score += .22m;
            evidence.Add("Cặp action Hiệu ứng/Thiệp khớp composer Zalo.");
        }
        else if (text.Contains("thiep", StringComparison.Ordinal)
                 && text.Contains("nhan tin", StringComparison.Ordinal))
        {
            score += .18m;
            evidence.Add("Action Thiệp kết hợp composer Nhắn tin hỗ trợ nhận diện Zalo.");
        }
        if (context.VisualFeatures.PurpleRatio >= .012m)
        {
            score += .05m;
            evidence.Add("Màu action/bubble chỉ được dùng làm bằng chứng phụ.");
        }
        return score;
    }
}

public sealed class MetaMessengerChatPlatformDetector : ChatPlatformDetectorBase
{
    public override ChatPlatform Platform => ChatPlatform.Meta;

    protected override decimal Score(ChatPlatformDetectionContext context, List<string> evidence)
    {
        var text = context.NormalizedSignalText;
        var score = 0m;
        score += Anchor(text, "messenger", .90m, "Nhận diện nhãn Messenger.", evidence);
        score += Anchor(text, "facebook", .72m, "Nhận diện nhãn Facebook.", evidence);
        score += Anchor(text, "marketplace", .50m, "Có hành động Marketplace.", evidence);
        score += Anchor(text, "tao don dat hang", .68m, "Có shortcut tạo đơn đặt hàng của Messenger.", evidence);
        score += Anchor(text, "mau tim kiem khach", .42m, "Có shortcut mẫu tìm kiếm khách hàng.", evidence);
        score += Anchor(text, "business suite", .46m, "Có nhãn Meta Business Suite.", evidence);
        score += Anchor(text, "dang hoat dong", .18m, "Có trạng thái hoạt động của Messenger.", evidence);
        if (text.Contains("tao don dat hang", StringComparison.Ordinal)
            && text.Contains("mau tim kiem", StringComparison.Ordinal))
        {
            score += .18m;
            evidence.Add("Cụm shortcut bán hàng khớp giao diện Messenger.");
        }
        if (context.VisualFeatures.BlueRatio >= .025m)
        {
            score += .07m;
            evidence.Add("Màu bubble/action chỉ được dùng làm bằng chứng phụ.");
        }
        return score;
    }
}

public sealed class TikTokChatPlatformDetector : ChatPlatformDetectorBase
{
    public override ChatPlatform Platform => ChatPlatform.TikTok;

    protected override decimal Score(ChatPlatformDetectionContext context, List<string> evidence)
    {
        var text = context.NormalizedSignalText;
        var score = 0m;
        score += Anchor(text, "tiktok", .92m, "Nhận diện nhãn TikTok.", evidence);
        score += Anchor(text, "nguoi la", .34m, "Có nhãn liên hệ người lạ ở header TikTok.", evidence);
        score += Anchor(text, "dang cho duoc dong y ket ban", .52m, "Có thanh trạng thái chờ đồng ý kết bạn.", evidence);
        score += Anchor(text, "dong y", .10m, "Có hành động đồng ý kết bạn.", evidence);
        score += Anchor(text, "tiktok shop", .58m, "Có nhãn TikTok Shop.", evidence);
        if (text.Contains("nguoi la", StringComparison.Ordinal)
            && text.Contains("ket ban", StringComparison.Ordinal))
        {
            score += .16m;
            evidence.Add("Cấu trúc header và trạng thái kết bạn khớp TikTok.");
        }
        if (context.VisualFeatures.TopDarkRatio >= .55m
            && context.VisualFeatures.BottomDarkRatio >= .45m)
        {
            score += .06m;
            evidence.Add("Bố cục header/composer dark-theme hỗ trợ nhận diện.");
        }
        return score;
    }
}

public static partial class ChatTextNormalization
{
    public static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
        }

        return NonTextCharacters().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ")
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonTextCharacters();
}
