using Lamie.Application.Common.Uploads;
using Lamie.Application.Settings.Products.Commands;
using Lamie.Domain.Products;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Reflection;
using Xunit;

namespace Lamie.Tests.Products;

public sealed class ProductSkuAndWatermarkTests
{
    [Fact]
    public void Create_product_contract_allows_omitted_sku_for_server_generation()
    {
        var property = typeof(CreateProductCommand).GetProperty(nameof(CreateProductCommand.Sku));

        Assert.NotNull(property);
        Assert.Equal(NullabilityState.Nullable, new NullabilityInfoContext().Create(property!).WriteState);
    }

    [Fact]
    public void Generated_sku_is_four_uppercase_alphanumeric_characters()
    {
        for (var index = 0; index < 100; index++) Assert.True(ProductSku.IsValidNewSku(ProductSku.Generate()));
    }

    [Fact]
    public async Task Sku_generation_retries_after_collision()
    {
        var candidates = new Queue<string>(["A7K2", "B93Q"]);
        var result = await ProductSku.GenerateUniqueAsync(
            (candidate, _) => Task.FromResult(candidate == "A7K2"),
            candidateFactory: () => candidates.Dequeue());
        Assert.Equal("B93Q", result);
    }

    [Theory]
    [InlineData("A7K2", true)] [InlineData("9XAM", true)] [InlineData("OLD-1234", false)] [InlineData("a7k2", false)]
    public void New_sku_format_is_strict_while_legacy_values_can_remain_in_storage(string sku, bool expected) =>
        Assert.Equal(expected, ProductSku.IsValidNewSku(sku));

    [Fact]
    public async Task Product_watermark_produces_readable_image_output()
    {
        using var sourceImage = new Image<Rgba32>(320, 240, new Rgba32(240, 240, 240));
        await using var source = new MemoryStream();
        await sourceImage.SaveAsPngAsync(source);
        source.Position = 0;
        await using var output = await ProductImageWatermarker.ApplyAsync(source, "A7K2", "image/png");
        using var result = await Image.LoadAsync<Rgba32>(output);
        Assert.Equal(320, result.Width);
        Assert.Equal(240, result.Height);
        Assert.True(output.Length > 0);
        Assert.NotEqual(new Rgba32(240, 240, 240), result[160, 230]);
    }

    [Theory]
    [InlineData(1600, 900, 245, 245, 245, "A7K2")]
    [InlineData(900, 1600, 24, 24, 24, "A7K2")]
    [InlineData(800, 800, 245, 245, 245, "OLD-1234")]
    [InlineData(160, 120, 24, 24, 24, "A7K2")]
    [InlineData(2400, 1800, 245, 245, 245, "A7K2")]
    public async Task Product_identification_stamp_is_compact_for_common_aspect_ratios_and_backgrounds(
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        string sku)
    {
        var background = new Rgba32(red, green, blue);
        using var sourceImage = new Image<Rgba32>(width, height, background);
        await using var source = new MemoryStream();
        await sourceImage.SaveAsPngAsync(source);
        source.Position = 0;

        await using var output = await ProductImageWatermarker.ApplyAsync(source, sku, "image/png");
        using var result = await Image.LoadAsync<Rgba32>(output);
        var changed = 0L;
        result.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    if (row[x] != background) changed++;
            }
        });

        Assert.InRange(changed, 1, (long)width * height * 3 / 20);
        Assert.Equal(background, result[width - 1, height - 1]);

        var previewDirectory = Environment.GetEnvironmentVariable("LAMIE_WATERMARK_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(previewDirectory, $"watermark-{width}x{height}-{red}-{sku.Replace('-', '_')}.png"),
                output.ToArray());
        }
    }

    [Theory]
    [InlineData(320, 240)]
    [InlineData(900, 1600)]
    [InlineData(1600, 900)]
    public async Task Product_identification_stamp_is_centered_horizontally_at_the_bottom(
        int width,
        int height)
    {
        var background = new Rgba32(245, 245, 245);
        using var sourceImage = new Image<Rgba32>(width, height, background);
        await using var source = new MemoryStream();
        await sourceImage.SaveAsPngAsync(source);
        source.Position = 0;

        await using var output = await ProductImageWatermarker.ApplyAsync(source, "51FY", "image/png");
        using var result = await Image.LoadAsync<Rgba32>(output);
        var changedColumns = new HashSet<int>();
        result.ProcessPixelRows(accessor =>
        {
            for (var y = accessor.Height / 2; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    if (row[x] != background) changedColumns.Add(x);
            }
        });

        Assert.NotEmpty(changedColumns);
        var watermarkCenter = (changedColumns.Min() + changedColumns.Max()) / 2d;
        Assert.InRange(watermarkCenter, width / 2d - 1, width / 2d + 1);
    }

    [Theory]
    [InlineData(320, 240, "LAMIE-LONG-LEGACY-SKU-2026")]
    [InlineData(240, 320, "OLD-1234-LONG")]
    public async Task Product_identification_stamp_fits_the_complete_long_sku(
        int width,
        int height,
        string sku)
    {
        var background = new Rgba32(245, 245, 245);
        using var sourceImage = new Image<Rgba32>(width, height, background);
        await using var source = new MemoryStream();
        await sourceImage.SaveAsPngAsync(source);
        source.Position = 0;

        await using var output = await ProductImageWatermarker.ApplyAsync(source, sku, "image/png");
        using var result = await Image.LoadAsync<Rgba32>(output);

        var changedColumns = new HashSet<int>();
        result.ProcessPixelRows(accessor =>
        {
            for (var y = accessor.Height / 2; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    if (row[x] != background) changedColumns.Add(x);
            }
        });

        Assert.True(changedColumns.Count > width / 3);
        Assert.DoesNotContain(0, changedColumns);
        Assert.DoesNotContain(width - 1, changedColumns);
    }

    [Theory]
    [InlineData(245, 245, 245)]
    [InlineData(20, 20, 20)]
    public async Task Product_identification_remains_high_contrast_after_resize_and_chat_like_compression(
        byte red,
        byte green,
        byte blue)
    {
        using var sourceImage = new Image<Rgba32>(1600, 900, new Rgba32(red, green, blue));
        await using var source = new MemoryStream();
        await sourceImage.SaveAsPngAsync(source);
        source.Position = 0;

        await using var stamped = await ProductImageWatermarker.ApplyAsync(source, "A7K2", "image/png");
        using var resized = await Image.LoadAsync<Rgba32>(stamped);
        resized.Mutate(context => context.Resize(400, 225));
        await using var compressed = new MemoryStream();
        await resized.SaveAsJpegAsync(compressed, new JpegEncoder { Quality = 55 });
        compressed.Position = 0;
        using var result = await Image.LoadAsync<Rgba32>(compressed);

        var beigePixels = 0;
        var lightPixels = 0;
        result.ProcessPixelRows(accessor =>
        {
            for (var y = accessor.Height * 3 / 4; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = row.Length / 2; x < row.Length; x++)
                {
                    if (row[x].R is >= 70 and <= 165
                        && row[x].G is >= 55 and <= 145
                        && row[x].B is >= 45 and <= 135
                        && row[x].R > row[x].G
                        && row[x].G >= row[x].B)
                        beigePixels++;
                    var luminance = (row[x].R + row[x].G + row[x].B) / 3;
                    if (luminance > 190) lightPixels++;
                }
            }
        });

        Assert.True(beigePixels > 50);
        Assert.True(lightPixels > 10);
    }

    [Fact]
    public async Task Reprocessing_the_same_clean_original_is_deterministic()
    {
        using var sourceImage = new Image<Rgba32>(600, 600, new Rgba32(230, 230, 230));
        await using var firstSource = new MemoryStream();
        await using var secondSource = new MemoryStream();
        await sourceImage.SaveAsPngAsync(firstSource);
        await sourceImage.SaveAsPngAsync(secondSource);
        firstSource.Position = 0;
        secondSource.Position = 0;

        await using var first = await ProductImageWatermarker.ApplyAsync(firstSource, "A7K2", "image/png");
        await using var second = await ProductImageWatermarker.ApplyAsync(secondSource, "A7K2", "image/png");

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public async Task Animated_product_image_receives_the_same_label_on_every_frame()
    {
        var light = new Rgba32(240, 240, 240);
        var dark = new Rgba32(20, 20, 20);
        using var sourceImage = new Image<Rgba32>(320, 240, light);
        using var darkFrame = new Image<Rgba32>(320, 240, dark);
        sourceImage.Frames.AddFrame(darkFrame.Frames.RootFrame);
        await using var source = new MemoryStream();
        await sourceImage.SaveAsGifAsync(source);
        source.Position = 0;

        await using var output = await ProductImageWatermarker.ApplyAsync(source, "A7K2", "image/gif");
        using var result = await Image.LoadAsync<Rgba32>(output);

        Assert.Equal(2, result.Frames.Count);
        Assert.True(CountChangedPixelsInBottomRight(result.Frames[0], light) > 20);
        Assert.True(CountChangedPixelsInBottomRight(result.Frames[1], dark) > 20);
    }

    private static int CountChangedPixelsInBottomRight(ImageFrame<Rgba32> frame, Rgba32 background)
    {
        var changed = 0;
        frame.ProcessPixelRows(accessor =>
        {
            for (var y = accessor.Height / 2; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = row.Length / 2; x < row.Length; x++)
                    if (row[x] != background) changed++;
            }
        });
        return changed;
    }

    [Fact]
    public async Task Invalid_product_image_fails_before_storage()
    {
        await using var invalid = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<Exception>(() => ProductImageWatermarker.ApplyAsync(invalid, "A7K2", "image/png"));
    }
}
