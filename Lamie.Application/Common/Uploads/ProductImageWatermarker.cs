using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Lamie.Application.Common.Uploads;

/// <summary>Applies one SKU watermark to a newly supplied product source image.</summary>
public static class ProductImageWatermarker
{
    private static readonly IReadOnlyDictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['A']=["01110","10001","10001","11111","10001","10001","10001"], ['B']=["11110","10001","10001","11110","10001","10001","11110"],
        ['C']=["01111","10000","10000","10000","10000","10000","01111"], ['D']=["11110","10001","10001","10001","10001","10001","11110"],
        ['E']=["11111","10000","10000","11110","10000","10000","11111"], ['F']=["11111","10000","10000","11110","10000","10000","10000"],
        ['G']=["01111","10000","10000","10111","10001","10001","01111"], ['H']=["10001","10001","10001","11111","10001","10001","10001"],
        ['I']=["11111","00100","00100","00100","00100","00100","11111"], ['J']=["00111","00010","00010","00010","10010","10010","01100"],
        ['K']=["10001","10010","10100","11000","10100","10010","10001"], ['L']=["10000","10000","10000","10000","10000","10000","11111"],
        ['M']=["10001","11011","10101","10101","10001","10001","10001"], ['N']=["10001","11001","10101","10011","10001","10001","10001"],
        ['O']=["01110","10001","10001","10001","10001","10001","01110"], ['P']=["11110","10001","10001","11110","10000","10000","10000"],
        ['Q']=["01110","10001","10001","10001","10101","10010","01101"], ['R']=["11110","10001","10001","11110","10100","10010","10001"],
        ['S']=["01111","10000","10000","01110","00001","00001","11110"], ['T']=["11111","00100","00100","00100","00100","00100","00100"],
        ['U']=["10001","10001","10001","10001","10001","10001","01110"], ['V']=["10001","10001","10001","10001","10001","01010","00100"],
        ['W']=["10001","10001","10001","10101","10101","10101","01010"], ['X']=["10001","10001","01010","00100","01010","10001","10001"],
        ['Y']=["10001","10001","01010","00100","00100","00100","00100"], ['Z']=["11111","00001","00010","00100","01000","10000","11111"],
        ['0']=["01110","10001","10011","10101","11001","10001","01110"], ['1']=["00100","01100","00100","00100","00100","00100","01110"],
        ['2']=["01110","10001","00001","00010","00100","01000","11111"], ['3']=["11110","00001","00001","01110","00001","00001","11110"],
        ['4']=["00010","00110","01010","10010","11111","00010","00010"], ['5']=["11111","10000","10000","11110","00001","00001","11110"],
        ['6']=["01110","10000","10000","11110","10001","10001","01110"], ['7']=["11111","00001","00010","00100","01000","01000","01000"],
        ['8']=["01110","10001","10001","01110","10001","10001","01110"], ['9']=["01110","10001","10001","01111","00001","00001","01110"]
    };

    public static async Task<MemoryStream> ApplyAsync(Stream source, string sku, string contentType, CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        var text = sku.Trim().ToUpperInvariant();
        var scale = Math.Max(1, Math.Min(image.Width, image.Height) / 180);
        var padding = Math.Max(4, scale * 3);
        var width = text.Length * 6 * scale - scale + padding * 2;
        var height = 7 * scale + padding * 2;
        var left = Math.Max(0, image.Width - width - padding);
        var top = Math.Max(0, image.Height - height - padding);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = top; y < Math.Min(image.Height, top + height); y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = left; x < Math.Min(image.Width, left + width); x++) row[x] = Blend(row[x], new Rgba32(0, 0, 0, 165));
            }
            for (var character = 0; character < text.Length; character++)
            {
                if (!Glyphs.TryGetValue(text[character], out var glyph)) continue;
                for (var gy = 0; gy < 7; gy++) for (var gx = 0; gx < 5; gx++) if (glyph[gy][gx] == '1')
                    for (var sy = 0; sy < scale; sy++)
                    {
                        var y = top + padding + gy * scale + sy;
                        if (y >= image.Height) continue;
                        var row = accessor.GetRowSpan(y);
                        for (var sx = 0; sx < scale; sx++)
                        {
                            var x = left + padding + (character * 6 + gx) * scale + sx;
                            if (x < image.Width) row[x] = new Rgba32(255, 255, 255, 255);
                        }
                    }
            }
        });

        var output = new MemoryStream();
        await image.SaveAsync(output, Encoder(contentType), cancellationToken);
        output.Position = 0;
        return output;
    }

    private static IImageEncoder Encoder(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => new PngEncoder(), "image/webp" => new WebpEncoder(), "image/gif" => new GifEncoder(), _ => new JpegEncoder { Quality = 90 }
    };

    private static Rgba32 Blend(Rgba32 source, Rgba32 overlay)
    {
        var alpha = overlay.A / 255f;
        return new Rgba32((byte)(source.R * (1-alpha)), (byte)(source.G * (1-alpha)), (byte)(source.B * (1-alpha)), source.A);
    }
}
