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
/// Renders a single human-readable SKU identification stamp on a newly supplied product image.
/// Callers must retain already-saved URLs instead of passing processed output back through this method.
/// </summary>
public static class ProductImageWatermarker
{
    private const byte BackgroundAlpha = 224;
    // Mirrors FE_Lamie's existing --lamie-mocha-500 brand token (#74645A).
    private static readonly Rgba32 WatermarkBackground = new(116, 100, 90, BackgroundAlpha);

    public static async Task<MemoryStream> ApplyAsync(
        Stream source,
        string sku,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        var text = sku.Trim().ToUpperInvariant();
        if (text.Length == 0)
            throw new ArgumentException("SKU is required for a product identification stamp.", nameof(sku));

        var shortEdge = Math.Min(image.Width, image.Height);
        var margin = Math.Min(
            Math.Clamp((int)Math.Round(shortEdge * .025), 4, 48),
            Math.Max(1, shortEdge / 8));
        var horizontalPadding = Math.Min(
            Math.Clamp((int)Math.Round(shortEdge * .022), 6, 32),
            Math.Max(1, (image.Width - margin * 2) / 4));
        var verticalPadding = Math.Min(
            Math.Clamp((int)Math.Round(shortEdge * .012), 4, 18),
            Math.Max(1, (image.Height - margin * 2) / 4));
        var radius = Math.Clamp((int)Math.Round(shortEdge * .009), 2, 14);
        var fontSize = Math.Clamp((float)(shortEdge * .048), 14f, 72f);
        var font = CreateFont(fontSize);
        var textOptions = new TextOptions(font) { KerningMode = KerningMode.Standard };
        var measured = TextMeasurer.MeasureSize(text, textOptions);
        var maximumTextWidth = Math.Max(1, image.Width - margin * 2 - horizontalPadding * 2);
        var maximumTextHeight = Math.Max(1, image.Height - margin * 2 - verticalPadding * 2);
        var scale = Math.Min(
            1f,
            Math.Min(maximumTextWidth / Math.Max(1, measured.Width), maximumTextHeight / Math.Max(1, measured.Height)));
        if (scale < 1f)
        {
            // Fitting takes precedence over a minimum font size: the complete SKU must remain on-image.
            fontSize = Math.Max(1f, fontSize * scale);
            font = CreateFont(fontSize);
            textOptions = new TextOptions(font) { KerningMode = KerningMode.Standard };
            measured = TextMeasurer.MeasureSize(text, textOptions);
        }
        var width = Math.Min(image.Width - margin * 2, (int)Math.Ceiling(measured.Width) + horizontalPadding * 2);
        var height = Math.Min(image.Height - margin * 2, (int)Math.Ceiling(measured.Height) + verticalPadding * 2);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var left = Math.Max(0, (image.Width - width) / 2);
        var top = Math.Max(0, image.Height - height - margin);

        FillRoundedRectangle(image, left, top, width, height, radius, WatermarkBackground);
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
                return family.CreateFont(size, FontStyle.Bold);
        }

        var families = SystemFonts.Families.ToList();
        if (families.Count == 0)
            throw new InvalidOperationException("No sans-serif font is available for product identification rendering.");
        return families[0].CreateFont(size, FontStyle.Bold);
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
