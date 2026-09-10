namespace Lamie.API.Options;

public sealed class ProductRecognitionOptions
{
    public const string SectionName = "ProductRecognition";

    public decimal MinimumSimilarity { get; init; } = 0.62m;
    public int BackfillBatchSize { get; init; } = 40;
}
