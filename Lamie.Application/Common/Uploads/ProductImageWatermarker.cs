using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lamie.Application.Common.Uploads;

/// <summary>
/// Renders a single compact SKU metadata label on a newly supplied original product image.
/// Callers must retain already-saved URLs instead of passing processed output back through this method.
/// </summary>
public static class ProductImageWatermarker
{
    private const byte BackgroundAlpha = 138;

    public static async Task<MemoryStream> ApplyAsync(
        Stream source,
        string sku,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        var text = sku.Trim().ToUpperInvariant();
        if (text.Length == 0)
            throw new ArgumentException("SKU is required for a product watermark.", nameof(sku));

        var shortEdge = Math.Min(image.Width, image.Height);
        var margin = Math.Clamp((int)Math.Round(shortEdge * .02), 5, 36);
        var fontSize = Math.Clamp((float)(shortEdge * .025), 10f, 28f);
        var horizontalPadding = Math.Clamp((int)Math.Round(shortEdge * .012), 5, 16);
        var verticalPadding = Math.Clamp((int)Math.Round(shortEdge * .006), 3, 9);
        var radius = Math.Clamp((int)Math.Round(shortEdge * .005), 2, 7);
        var font = CreateFont(fontSize);
        var textOptions = new TextOptions(font) { KerningMode = KerningMode.Standard };
        var measured = TextMeasurer.MeasureSize(text, textOptions);
        var maximumTextWidth = Math.Max(1, image.Width - margin * 2 - horizontalPadding * 2);
        if (measured.Width > maximumTextWidth)
        {
            fontSize = Math.Max(6f, fontSize * maximumTextWidth / measured.Width);
            font = CreateFont(fontSize);
            textOptions = new TextOptions(font) { KerningMode = KerningMode.Standard };
            measured = TextMeasurer.MeasureSize(text, textOptions);
        }
        var width = Math.Min(image.Width, (int)Math.Ceiling(measured.Width) + horizontalPadding * 2);
        var height = Math.Min(image.Height, (int)Math.Ceiling(measured.Height) + verticalPadding * 2);
        var left = Math.Max(0, image.Width - width - margin);
        var top = Math.Max(0, image.Height - height - margin);

        FillRoundedRectangle(image, left, top, width, height, radius, new Rgba32(0, 0, 0, BackgroundAlpha));
        var textLeft = left + (width - measured.Width) / 2f;
        var textTop = top + (height - measured.Height) / 2f;
        image.Mutate(context => context.DrawText(text, font, Color.White, new PointF(textLeft, textTop)));

        var output = new MemoryStream();
        await image.SaveAsync(output, Encoder(contentType), cancellationToken);
        output.Position = 0;
        return output;
    }

    private static Font CreateFont(float size)
    {
        foreach (var familyName in new[] { "Segoe UI Semibold", "Inter", "Arial", "DejaVu Sans" })
        {
            if (SystemFonts.TryGet(familyName, out var family))
                return family.CreateFont(size, FontStyle.Regular);
        }

        var families = SystemFonts.Families.ToList();
        if (families.Count == 0)
            throw new InvalidOperationException("No sans-serif font is available for product watermark rendering.");
        return families[0].CreateFont(size, FontStyle.Regular);
    }

    private static void FillRoundedRectangle(
        Image<Rgba32> image,
        int left,
        int top,
        int width,
        int height,
        int radius,
        Rgba32 overlay)
    {
        var right = Math.Min(image.Width, left + width);
        var bottom = Math.Min(image.Height, top + height);
        var safeRadius = Math.Min(radius, Math.Min(width, height) / 2);
        foreach (var frame in image.Frames)
        {
            frame.ProcessPixelRows(accessor =>
            {
                for (var y = top; y < bottom; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = left; x < right; x++)
                    {
                        var localX = x - left;
                        var localY = y - top;
                        var cornerX = localX < safeRadius
                            ? safeRadius - localX
                            : localX >= width - safeRadius ? localX - (width - safeRadius - 1) : 0;
                        var cornerY = localY < safeRadius
                            ? safeRadius - localY
                            : localY >= height - safeRadius ? localY - (height - safeRadius - 1) : 0;
                        if (cornerX > 0 && cornerY > 0 && cornerX * cornerX + cornerY * cornerY > safeRadius * safeRadius)
                            continue;
                        row[x] = Blend(row[x], overlay);
                    }
                }
            });
        }
    }

    private static IImageEncoder Encoder(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => new PngEncoder(),
        "image/webp" => new WebpEncoder(),
        "image/gif" => new GifEncoder(),
        _ => new JpegEncoder { Quality = 90 }
    };

    private static Rgba32 Blend(Rgba32 source, Rgba32 overlay)
    {
        var alpha = overlay.A / 255f;
        return new Rgba32(
            (byte)Math.Round(source.R * (1 - alpha) + overlay.R * alpha),
            (byte)Math.Round(source.G * (1 - alpha) + overlay.G * alpha),
            (byte)Math.Round(source.B * (1 - alpha) + overlay.B * alpha),
            source.A);
    }
}
