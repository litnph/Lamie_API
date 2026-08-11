using Lamie.Application.Common.Uploads;
using Lamie.Domain.Products;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Lamie.Tests.Products;

public sealed class ProductSkuAndWatermarkTests
{
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
        Assert.NotEqual(new Rgba32(240, 240, 240), result[310, 230]);
    }

    [Fact]
    public async Task Invalid_product_image_fails_before_storage()
    {
        await using var invalid = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAnyAsync<Exception>(() => ProductImageWatermarker.ApplyAsync(invalid, "A7K2", "image/png"));
    }
}
