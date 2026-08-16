namespace Lamie.API.Services;

public sealed class ChatScreenshotAnalysisOptions
{
    public const string SectionName = "ChatScreenshotAnalysis";
    public bool Enabled { get; init; } = true;
    public string TessdataPath { get; init; } = "Data/Tessdata";
    public string TesseractExecutablePath { get; init; } = "tesseract";
    public int MaximumFiles { get; init; } = 10;
    public long MaximumFileBytes { get; init; } = 10 * 1024 * 1024;
    public long MaximumImagePixels { get; init; } = 30_000_000;
    public int MaximumImageDimension { get; init; } = 16_384;
    public decimal MaximumImageAspectRatio { get; init; } = 8m;
    public long MaximumWorkingImagePixels { get; init; } = 8_000_000;
    public int MaximumWorkingImageDimension { get; init; } = 4_096;
    public int OcrTimeoutSeconds { get; init; } = 20;
    public int PlatformSignalTargetWidth { get; init; } = 1_800;
    public int HeaderTargetWidth { get; init; } = 2_200;
    public decimal PlatformMinimumScore { get; init; } = .48m;
    public decimal PlatformMinimumMargin { get; init; } = .14m;
}
