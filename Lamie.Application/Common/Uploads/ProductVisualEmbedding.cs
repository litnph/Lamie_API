using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lamie.Application.Common.Uploads;

public interface IProductVisualEmbeddingProvider
{
    string Version { get; }

    Task<byte[]> CreateAsync(
        Stream source,
        CancellationToken cancellationToken = default);

    double CosineSimilarity(byte[] left, byte[] right);
}

/// <summary>
/// Produces a deterministic visual descriptor from image colour, luminance and edge layout.
/// The bottom edge is excluded so clean uploads can be compared with catalog images that
/// already contain Lamie's SKU stamp. No text or product metadata participates in the score.
/// </summary>
public sealed class ProductVisualEmbeddingProvider : IProductVisualEmbeddingProvider
{
    public const string CurrentVersion = "lamie-visual-v1";
    private const int WorkingSize = 64;
    private const int GridSize = 8;
    private const int HistogramBins = 16;
    private const long MaximumPixels = 40_000_000;

    public string Version => CurrentVersion;

    public async Task<byte[]> CreateAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new InvalidDataException("Product image stream is not readable.");

        using var image = await Image.LoadAsync<Rgba32>(source, cancellationToken);
        if ((long)image.Width * image.Height > MaximumPixels)
            throw new InvalidDataException("Product image dimensions are too large for recognition.");

        image.Mutate(context => context.AutoOrient());
        var contentHeight = Math.Max(1, (int)Math.Floor(image.Height * 0.82));
        image.Mutate(context => context
            .Crop(new Rectangle(0, 0, image.Width, contentHeight))
            .Resize(new ResizeOptions
            {
                Size = new Size(WorkingSize, WorkingSize),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Bicubic
            }));

        var cellFeatureCount = GridSize * GridSize * 7;
        var features = new float[cellFeatureCount + HistogramBins * 3];
        var cellPixels = (WorkingSize / GridSize) * (WorkingSize / GridSize);
        var histogramOffset = cellFeatureCount;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < WorkingSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                var previousRow = accessor.GetRowSpan(Math.Max(0, y - 1));
                for (var x = 0; x < WorkingSize; x++)
                {
                    var pixel = row[x];
                    var red = pixel.R / 255f;
                    var green = pixel.G / 255f;
                    var blue = pixel.B / 255f;
                    var max = Math.Max(red, Math.Max(green, blue));
                    var min = Math.Min(red, Math.Min(green, blue));
                    var saturation = max <= 0 ? 0 : (max - min) / max;
                    var luminance = 0.2126f * red + 0.7152f * green + 0.0722f * blue;
                    var left = row[Math.Max(0, x - 1)];
                    var above = previousRow[x];
                    var leftLuminance = Luminance(left);
                    var aboveLuminance = Luminance(above);

                    var cellX = x * GridSize / WorkingSize;
                    var cellY = y * GridSize / WorkingSize;
                    var offset = (cellY * GridSize + cellX) * 7;
                    features[offset] += red * 2f - 1f;
                    features[offset + 1] += green * 2f - 1f;
                    features[offset + 2] += blue * 2f - 1f;
                    features[offset + 3] += saturation;
                    features[offset + 4] += luminance * 2f - 1f;
                    features[offset + 5] += Math.Abs(luminance - leftLuminance);
                    features[offset + 6] += Math.Abs(luminance - aboveLuminance);

                    features[histogramOffset + Bin(pixel.R)] += 1f;
                    features[histogramOffset + HistogramBins + Bin(pixel.G)] += 1f;
                    features[histogramOffset + HistogramBins * 2 + Bin(pixel.B)] += 1f;
                }
            }
        });

        for (var index = 0; index < cellFeatureCount; index++)
            features[index] /= cellPixels;
        for (var index = histogramOffset; index < features.Length; index++)
            features[index] /= WorkingSize * WorkingSize;

        Normalize(features);
        var bytes = new byte[features.Length * sizeof(float)];
        for (var index = 0; index < features.Length; index++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), features[index]);
        return bytes;
    }

    public double CosineSimilarity(byte[] left, byte[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Length == 0 || left.Length != right.Length || left.Length % sizeof(float) != 0)
            return 0;

        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var offset = 0; offset < left.Length; offset += sizeof(float))
        {
            var leftValue = BinaryPrimitives.ReadSingleLittleEndian(left.AsSpan(offset, sizeof(float)));
            var rightValue = BinaryPrimitives.ReadSingleLittleEndian(right.AsSpan(offset, sizeof(float)));
            if (!float.IsFinite(leftValue) || !float.IsFinite(rightValue))
                return 0;
            dot += leftValue * rightValue;
            leftMagnitude += leftValue * leftValue;
            rightMagnitude += rightValue * rightValue;
        }

        if (leftMagnitude <= 0 || rightMagnitude <= 0)
            return 0;
        return Math.Clamp(dot / Math.Sqrt(leftMagnitude * rightMagnitude), 0d, 1d);
    }

    private static float Luminance(Rgba32 pixel) =>
        (0.2126f * pixel.R + 0.7152f * pixel.G + 0.0722f * pixel.B) / 255f;

    private static int Bin(byte value) => Math.Min(HistogramBins - 1, value * HistogramBins / 256);

    private static void Normalize(float[] features)
    {
        var magnitude = Math.Sqrt(features.Sum(value => value * value));
        if (magnitude <= 0)
            throw new InvalidDataException("Product image did not contain usable visual information.");

        for (var index = 0; index < features.Length; index++)
            features[index] = (float)(features[index] / magnitude);
    }
}
