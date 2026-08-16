using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lamie.Application.ChatAnalysis;

[JsonConverter(typeof(ChatPlatformJsonConverter))]
public enum ChatPlatform
{
    Unknown = 0,
    Zalo = 1,
    Meta = 2,
    TikTok = 3
}

public sealed class ChatPlatformJsonConverter : JsonConverter<ChatPlatform>
{
    public override ChatPlatform Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Chat platform must be a string.");

        return reader.GetString()?.Trim().ToUpperInvariant() switch
        {
            "UNKNOWN" => ChatPlatform.Unknown,
            "ZALO" => ChatPlatform.Zalo,
            "META" => ChatPlatform.Meta,
            "TIKTOK" => ChatPlatform.TikTok,
            _ => throw new JsonException("Unsupported chat platform.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ChatPlatform value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ChatPlatform.Unknown => "UNKNOWN",
            ChatPlatform.Zalo => "ZALO",
            ChatPlatform.Meta => "META",
            ChatPlatform.TikTok => "TIKTOK",
            _ => throw new JsonException("Unsupported chat platform.")
        });
}

public sealed record ChatScreenshotInput(
    string ScreenshotId,
    string FileName,
    string ContentType,
    byte[] Content)
{
    public ChatScreenshotInput(string fileName, string contentType, byte[] content)
        : this(fileName, fileName, contentType, content)
    {
    }
}

public enum ChatOcrRegion
{
    PlatformSignals = 1,
    ConversationHeader = 2
}

public sealed record ChatOcrInput(
    string ScreenshotId,
    ChatOcrRegion Region,
    byte[] PngContent,
    int Width,
    int Height);

public sealed record OcrTextLine(
    string Text,
    decimal Confidence,
    int Left,
    int Top,
    int Width,
    int Height,
    int ImageWidth,
    int ImageHeight);

public sealed record ChatScreenshotItemAnalysis(
    string ScreenshotId,
    string FileName,
    ChatPlatform DetectedPlatform,
    decimal PlatformConfidence,
    string? DetectedOrdererName,
    decimal NameConfidence,
    IReadOnlyList<string> DetectedTexts,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed record ChatScreenshotAnalysis(
    ChatPlatform DetectedPlatform,
    decimal PlatformConfidence,
    string? DetectedOrdererName,
    decimal NameConfidence,
    IReadOnlyList<ChatScreenshotItemAnalysis> Screenshots,
    IReadOnlyList<string> Warnings);

public interface IChatOcrProvider
{
    Task<IReadOnlyList<OcrTextLine>> ReadAsync(
        ChatOcrInput image,
        CancellationToken cancellationToken);
}

public interface IChatScreenshotAnalyzer
{
    Task<ChatScreenshotAnalysis> AnalyzeAsync(
        IReadOnlyList<ChatScreenshotInput> images,
        CancellationToken cancellationToken);
}
