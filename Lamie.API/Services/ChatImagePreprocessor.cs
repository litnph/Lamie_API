using Lamie.Application.ChatAnalysis;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lamie.API.Services;

public readonly record struct NormalizedImageRegion(decimal X, decimal Y, decimal Width, decimal Height);

public sealed record ChatVisualFeatures(
    decimal TopDarkRatio,
    decimal BottomDarkRatio,
    decimal FullDarkRatio,
    decimal BlueRatio,
    decimal PurpleRatio);

public sealed record PreparedChatImage(ChatOcrInput OcrInput, ChatVisualFeatures VisualFeatures);

public interface IChatImagePreprocessor
{
    Task<PreparedChatImage> PreparePlatformSignalsAsync(
        ChatScreenshotInput input,
        CancellationToken cancellationToken);

    Task<ChatOcrInput> PrepareHeaderAsync(
        ChatScreenshotInput input,
        ChatPlatform platform,
        CancellationToken cancellationToken);
}

public sealed class ChatImagePreprocessor : IChatImagePreprocessor
{
    private static readonly NormalizedImageRegion PlatformTopRegion = new(0m, .035m, 1m, .17m);
    private static readonly NormalizedImageRegion PlatformBottomRegion = new(0m, .79m, 1m, .21m);
    private static readonly DecoderOptions FirstFrameDecoderOptions = new()
    {
        MaxFrames = 1
    };

    private readonly ChatScreenshotAnalysisOptions _options;

    public ChatImagePreprocessor(IOptions<ChatScreenshotAnalysisOptions> options)
    {
        _options = options.Value;
    }

    public async Task<PreparedChatImage> PreparePlatformSignalsAsync(
        ChatScreenshotInput input,
        CancellationToken cancellationToken)
    {
        using var source = LoadOriented(input.Content);
        var visualFeatures = ReadVisualFeatures(source);
        using var top = PrepareCrop(source, PlatformTopRegion, _options.PlatformSignalTargetWidth, 1.22f);
        using var bottom = PrepareCrop(source, PlatformBottomRegion, _options.PlatformSignalTargetWidth, 1.22f);
        var separatorHeight = Math.Max(16, top.Width / 75);
        FitStackedCropsToWorkingBounds(top, bottom, ref separatorHeight);
        var combinedWidth = Math.Max(top.Width, bottom.Width);
        var combinedHeight = checked(top.Height + separatorHeight + bottom.Height);
        EnsureWorkingDimensions(combinedWidth, combinedHeight);
        using var combined = new Image<Rgba32>(
            combinedWidth,
            combinedHeight,
            Color.White);
        combined.Mutate(context =>
        {
            context.DrawImage(top, new Point(0, 0), 1f);
            context.DrawImage(bottom, new Point(0, top.Height + separatorHeight), 1f);
        });

        return new PreparedChatImage(
            await EncodeAsync(input.ScreenshotId, ChatOcrRegion.PlatformSignals, combined, cancellationToken),
            visualFeatures);
    }

    public async Task<ChatOcrInput> PrepareHeaderAsync(
        ChatScreenshotInput input,
        ChatPlatform platform,
        CancellationToken cancellationToken)
    {
        using var source = LoadOriented(input.Content);
        using var header = PrepareColorHeaderCrop(source, HeaderRegion(platform), _options.HeaderTargetWidth);
        return await EncodeAsync(input.ScreenshotId, ChatOcrRegion.ConversationHeader, header, cancellationToken);
    }

    private Image<Rgba32> LoadOriented(byte[] content)
    {
        var info = Image.Identify(FirstFrameDecoderOptions, content)
            ?? throw new InvalidDataException("The screenshot cannot be decoded.");
        EnsureSourceDimensions(info.Width, info.Height);

        var image = Image.Load<Rgba32>(FirstFrameDecoderOptions, content);
        try
        {
            image.Mutate(context => context.AutoOrient());
            EnsureSourceDimensions(image.Width, image.Height);
            if (image.Frames.Count != 1)
                throw new InvalidDataException("Only the first screenshot frame may be analyzed.");
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private Image<Rgba32> PrepareCrop(
        Image<Rgba32> source,
        NormalizedImageRegion normalized,
        int targetWidth,
        float contrast)
    {
        var rectangle = ToRectangle(source.Width, source.Height, normalized);
        var crop = source.Clone(context => context.Crop(rectangle));
        try
        {
            var size = BoundWorkingSize(
                Math.Max(1, targetWidth),
                Math.Max(1d, crop.Height * (double)Math.Max(1, targetWidth) / crop.Width));
            if (crop.Width != size.Width || crop.Height != size.Height)
            {
                crop.Mutate(context => context.Resize(
                    size.Width,
                    size.Height,
                    KnownResamplers.Lanczos3));
            }

            var invert = IsPredominantlyDark(crop);
            crop.Mutate(context =>
            {
                context.Grayscale().Contrast(contrast);
                if (invert)
                    context.Invert();
            });
            return crop;
        }
        catch
        {
            crop.Dispose();
            throw;
        }
    }

    private static NormalizedImageRegion HeaderRegion(ChatPlatform platform) => platform switch
    {
        ChatPlatform.Zalo => new(.17m, .045m, .41m, .050m),
        ChatPlatform.Meta => new(.18m, .044m, .42m, .050m),
        ChatPlatform.TikTok => new(.10m, .042m, .43m, .045m),
        _ => new(.10m, .042m, .48m, .052m)
    };

    private Image<Rgba32> PrepareColorHeaderCrop(
        Image<Rgba32> source,
        NormalizedImageRegion normalized,
        int maximumWidth)
    {
        var crop = source.Clone(context => context.Crop(ToRectangle(source.Width, source.Height, normalized)));
        try
        {
            var requestedWidth = Math.Min(Math.Max(1L, maximumWidth), checked((long)crop.Width * 3L));
            var size = BoundWorkingSize(
                requestedWidth,
                Math.Max(1d, crop.Height * (double)requestedWidth / crop.Width));
            if (crop.Width != size.Width || crop.Height != size.Height)
            {
                crop.Mutate(context => context.Resize(
                    size.Width,
                    size.Height,
                    KnownResamplers.Bicubic));
            }
            return crop;
        }
        catch
        {
            crop.Dispose();
            throw;
        }
    }

    private void EnsureSourceDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0
            || width > _options.MaximumImageDimension
            || height > _options.MaximumImageDimension)
            throw new InvalidDataException("The screenshot dimensions are not supported.");

        var pixels = checked((long)width * height);
        var aspectRatio = (decimal)Math.Max(width, height) / Math.Min(width, height);
        if (pixels > _options.MaximumImagePixels
            || aspectRatio > _options.MaximumImageAspectRatio)
            throw new InvalidDataException("The screenshot dimensions or aspect ratio are not supported.");
    }

    private Size BoundWorkingSize(double desiredWidth, double desiredHeight)
    {
        if (!double.IsFinite(desiredWidth) || !double.IsFinite(desiredHeight)
            || desiredWidth < 1d || desiredHeight < 1d)
            throw new InvalidDataException("The derived screenshot dimensions are not supported.");

        var maximumDimension = Math.Max(1, _options.MaximumWorkingImageDimension);
        var maximumPixels = Math.Max(1L, _options.MaximumWorkingImagePixels);
        var scale = Math.Min(
            1d,
            Math.Min(
                maximumDimension / desiredWidth,
                Math.Min(
                    maximumDimension / desiredHeight,
                    Math.Sqrt(maximumPixels / (desiredWidth * desiredHeight)))));
        var width = Math.Max(1, checked((int)(scale == 1d
            ? Math.Round(desiredWidth)
            : Math.Floor(desiredWidth * scale))));
        var height = Math.Max(1, checked((int)(scale == 1d
            ? Math.Round(desiredHeight)
            : Math.Floor(desiredHeight * scale))));
        EnsureWorkingDimensions(width, height);
        return new Size(width, height);
    }

    private void FitStackedCropsToWorkingBounds(
        Image<Rgba32> top,
        Image<Rgba32> bottom,
        ref int separatorHeight)
    {
        var desiredWidth = Math.Max(top.Width, bottom.Width);
        var desiredHeight = checked(top.Height + separatorHeight + bottom.Height);
        var bounded = BoundWorkingSize(desiredWidth, desiredHeight);
        if (bounded.Width == desiredWidth && bounded.Height == desiredHeight)
            return;

        var scale = Math.Min(
            (double)bounded.Width / desiredWidth,
            (double)bounded.Height / desiredHeight);
        top.Mutate(context => context.Resize(
            Math.Max(1, (int)Math.Floor(top.Width * scale)),
            Math.Max(1, (int)Math.Floor(top.Height * scale)),
            KnownResamplers.Lanczos3));
        bottom.Mutate(context => context.Resize(
            Math.Max(1, (int)Math.Floor(bottom.Width * scale)),
            Math.Max(1, (int)Math.Floor(bottom.Height * scale)),
            KnownResamplers.Lanczos3));
        separatorHeight = Math.Max(1, (int)Math.Floor(separatorHeight * scale));
    }

    private void EnsureWorkingDimensions(int width, int height)
    {
        var pixels = checked((long)width * height);
        if (width <= 0 || height <= 0
            || width > _options.MaximumWorkingImageDimension
            || height > _options.MaximumWorkingImageDimension
            || pixels > _options.MaximumWorkingImagePixels)
            throw new InvalidDataException("The screenshot working-copy dimensions are not supported.");
    }

    private static Rectangle ToRectangle(
        int imageWidth,
        int imageHeight,
        NormalizedImageRegion normalized)
    {
        var left = Math.Clamp((int)Math.Floor(imageWidth * normalized.X), 0, imageWidth - 1);
        var top = Math.Clamp((int)Math.Floor(imageHeight * normalized.Y), 0, imageHeight - 1);
        var right = Math.Clamp(
            (int)Math.Ceiling(imageWidth * (normalized.X + normalized.Width)),
            left + 1,
            imageWidth);
        var bottom = Math.Clamp(
            (int)Math.Ceiling(imageHeight * (normalized.Y + normalized.Height)),
            top + 1,
            imageHeight);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static bool IsPredominantlyDark(Image<Rgba32> image)
    {
        var (dark, sampled) = Sample(image, pixel => Luminance(pixel) < .38m);
        return sampled > 0 && (decimal)dark / sampled >= .52m;
    }

    private static ChatVisualFeatures ReadVisualFeatures(Image<Rgba32> image)
    {
        var top = ToRectangle(image.Width, image.Height, new NormalizedImageRegion(0m, 0m, 1m, .20m));
        var bottom = ToRectangle(image.Width, image.Height, new NormalizedImageRegion(0m, .78m, 1m, .22m));
        var full = new Rectangle(0, 0, image.Width, image.Height);
        return new ChatVisualFeatures(
            Ratio(image, top, pixel => Luminance(pixel) < .22m),
            Ratio(image, bottom, pixel => Luminance(pixel) < .22m),
            Ratio(image, full, pixel => Luminance(pixel) < .22m),
            Ratio(image, full, pixel => pixel.B > 125 && pixel.B > pixel.R + 35 && pixel.B > pixel.G + 12),
            Ratio(image, full, pixel => pixel.B > 115 && pixel.R > 85 && pixel.B > pixel.G + 25));
    }

    private static decimal Ratio(Image<Rgba32> image, Rectangle region, Func<Rgba32, bool> predicate)
    {
        var stride = Math.Max(1, Math.Min(region.Width, region.Height) / 90);
        var matched = 0;
        var sampled = 0;
        for (var y = region.Top; y < region.Bottom; y += stride)
        for (var x = region.Left; x < region.Right; x += stride)
        {
            sampled++;
            if (predicate(image[x, y]))
                matched++;
        }
        return sampled == 0 ? 0m : (decimal)matched / sampled;
    }

    private static (int Matched, int Sampled) Sample(Image<Rgba32> image, Func<Rgba32, bool> predicate)
    {
        var stride = Math.Max(1, Math.Min(image.Width, image.Height) / 80);
        var matched = 0;
        var sampled = 0;
        for (var y = 0; y < image.Height; y += stride)
        for (var x = 0; x < image.Width; x += stride)
        {
            sampled++;
            if (predicate(image[x, y]))
                matched++;
        }
        return (matched, sampled);
    }

    private static decimal Luminance(Rgba32 pixel) =>
        (.2126m * pixel.R + .7152m * pixel.G + .0722m * pixel.B) / 255m;

    private static async Task<ChatOcrInput> EncodeAsync(
        string screenshotId,
        ChatOcrRegion region,
        Image<Rgba32> image,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await image.SaveAsync(buffer, new PngEncoder
        {
            CompressionLevel = PngCompressionLevel.Level3
        }, cancellationToken);
        return new ChatOcrInput(screenshotId, region, buffer.ToArray(), image.Width, image.Height);
    }
}
