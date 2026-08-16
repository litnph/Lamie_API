using System.Text.Json;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.ChatAnalysis;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Lamie.Tests.Orders;

public sealed class ChatScreenshotAnalyzerTests
{
    [Theory]
    [InlineData("zalo.png", "Hiệu ứng Thiệp Nhắn tin", ChatPlatform.Zalo)]
    [InlineData("meta.png", "Tạo đơn đặt hàng Mẫu tìm kiếm khách hàng Aa", ChatPlatform.Meta)]
    [InlineData("tiktok.png", "NGƯỜI LẠ Đang chờ được đồng ý kết bạn Đồng ý", ChatPlatform.TikTok)]
    public async Task Detects_supported_platform_from_combined_structural_text_evidence(
        string fileName,
        string platformSignals,
        ChatPlatform expected)
    {
        var ocr = new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [(fileName, ChatOcrRegion.PlatformSignals)] = [Line(platformSignals, 20, 20, 350, 30)],
            [(fileName, ChatOcrRegion.ConversationHeader)] = [Line("Nguyễn Minh Anh", 65, 40, 230, 55)]
        });
        var analyzer = new ChatScreenshotAnalyzer(ocr);

        var result = await analyzer.AnalyzeAsync([Input(fileName)], CancellationToken.None);

        Assert.Equal(expected, result.DetectedPlatform);
        Assert.True(result.PlatformConfidence >= .7m);
        Assert.Equal("Nguyễn Minh Anh", result.DetectedOrdererName);
        Assert.True(result.NameConfidence >= .7m);
        Assert.NotEmpty(result.Screenshots[0].Evidence);
    }

    [Theory]
    [InlineData("Nguyễn Thị Minh Anh")]
    [InlineData("Trần Minh Khôi - Hoa và Quà")]
    [InlineData("Đỗ Ánh Dương")]
    public async Task Preserves_clear_long_and_vietnamese_header_names(string expectedName)
    {
        var fileName = $"{Guid.NewGuid():N}.png";
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [(fileName, ChatOcrRegion.PlatformSignals)] = [Line("Messenger Tạo đơn đặt hàng", 20, 20, 340, 30)],
            [(fileName, ChatOcrRegion.ConversationHeader)] = [Line($"_ '{expectedName}", 50, 38, 300, 58)]
        }));

        var result = await analyzer.AnalyzeAsync([Input(fileName)], CancellationToken.None);

        Assert.Equal(expectedName, result.DetectedOrdererName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Header_extraction_supports_light_and_dark_working_copies(bool darkTheme)
    {
        var fileName = darkTheme ? "dark.png" : "light.png";
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [(fileName, ChatOcrRegion.PlatformSignals)] = [Line("NGƯỜI LẠ Đang chờ được đồng ý kết bạn", 20, 20, 350, 30)],
            [(fileName, ChatOcrRegion.ConversationHeader)] = [Line("Minh Thành", 60, 38, 220, 60)]
        }));

        var result = await analyzer.AnalyzeAsync(
            [Input(fileName, darkTheme ? new Rgba32(24, 24, 24) : Color.White)],
            CancellationToken.None);

        Assert.Equal(ChatPlatform.TikTok, result.DetectedPlatform);
        Assert.Equal("Minh Thành", result.DetectedOrdererName);
    }

    [Fact]
    public async Task Uses_only_tight_header_result_for_orderer_name_not_message_body()
    {
        const string fileName = "body-name.png";
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [(fileName, ChatOcrRegion.PlatformSignals)] =
            [
                Line("Messenger Tạo đơn đặt hàng", 20, 20, 340, 30),
                Line("Nguyễn Tên Trong Tin Nhắn", 20, 130, 350, 48)
            ],
            [(fileName, ChatOcrRegion.ConversationHeader)] = [Line("Anh Duy", 70, 35, 180, 58)]
        }));

        var result = await analyzer.AnalyzeAsync([Input(fileName)], CancellationToken.None);

        Assert.Equal("Anh Duy", result.DetectedOrdererName);
        Assert.DoesNotContain("Nguyễn Tên Trong Tin Nhắn", result.Screenshots[0].DetectedTexts);
    }

    [Fact]
    public async Task Does_not_guess_name_from_header_status()
    {
        const string fileName = "status.png";
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [(fileName, ChatOcrRegion.PlatformSignals)] = [Line("NGƯỜI LẠ Đang chờ được đồng ý kết bạn", 20, 20, 350, 30)],
            [(fileName, ChatOcrRegion.ConversationHeader)] =
            [
                Line("NGƯỜI LẠ", 50, 35, 180, 45),
                Line("Đang hoạt động", 50, 90, 240, 36)
            ]
        }));

        var result = await analyzer.AnalyzeAsync([Input(fileName)], CancellationToken.None);

        Assert.Null(result.DetectedOrdererName);
        Assert.Contains(result.Warnings, warning => warning.Contains("không lấy tên", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unknown_platform_remains_unknown_when_evidence_is_weak_even_if_image_is_blue()
    {
        const string fileName = "unknown.png";
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [(fileName, ChatOcrRegion.ConversationHeader)] = [Line("Nguyễn Minh Anh", 60, 35, 230, 55)]
        }));

        var result = await analyzer.AnalyzeAsync(
            [Input(fileName, new Rgba32(30, 100, 225))],
            CancellationToken.None);

        Assert.Equal(ChatPlatform.Unknown, result.DetectedPlatform);
        Assert.True(result.PlatformConfidence < .5m);
        Assert.Equal("Nguyễn Minh Anh", result.DetectedOrdererName);
    }

    [Fact]
    public async Task Multiple_matching_screenshots_increase_confidence()
    {
        var lines = new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [("one.png", ChatOcrRegion.PlatformSignals)] = [Line("Tạo đơn đặt hàng Mẫu tìm kiếm khách", 20, 20, 350, 30)],
            [("one.png", ChatOcrRegion.ConversationHeader)] = [Line("Nguyễn Minh Anh", 60, 35, 230, 55)],
            [("two.png", ChatOcrRegion.PlatformSignals)] = [Line("Messenger Marketplace", 20, 20, 330, 30)],
            [("two.png", ChatOcrRegion.ConversationHeader)] = [Line("Nguyễn Minh Anh", 60, 35, 230, 55)]
        };
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(lines));

        var result = await analyzer.AnalyzeAsync([Input("one.png"), Input("two.png")], CancellationToken.None);

        Assert.Equal(ChatPlatform.Meta, result.DetectedPlatform);
        Assert.Equal("Nguyễn Minh Anh", result.DetectedOrdererName);
        Assert.True(result.PlatformConfidence > result.Screenshots[0].PlatformConfidence);
        Assert.True(result.NameConfidence > result.Screenshots[0].NameConfidence);
    }

    [Fact]
    public async Task Conflicting_screenshots_do_not_auto_select_platform_or_name()
    {
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>
        {
            [("one.png", ChatOcrRegion.PlatformSignals)] = [Line("NGƯỜI LẠ Đang chờ được đồng ý kết bạn", 20, 20, 350, 30)],
            [("one.png", ChatOcrRegion.ConversationHeader)] = [Line("Nguyễn A", 60, 35, 180, 55)],
            [("two.png", ChatOcrRegion.PlatformSignals)] = [Line("Hiệu ứng Thiệp", 20, 20, 250, 30)],
            [("two.png", ChatOcrRegion.ConversationHeader)] = [Line("Nguyễn B", 60, 35, 180, 55)]
        }));

        var result = await analyzer.AnalyzeAsync([Input("one.png"), Input("two.png")], CancellationToken.None);

        Assert.Equal(ChatPlatform.Unknown, result.DetectedPlatform);
        Assert.Null(result.DetectedOrdererName);
        Assert.Contains(result.Warnings, warning => warning.Contains("không đồng nhất", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ocr_failure_is_non_blocking_and_scoped_to_its_screenshot()
    {
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(
            new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>(),
            throwForMissing: true));

        var result = await analyzer.AnalyzeAsync([Input("low-quality.png")], CancellationToken.None);

        Assert.Single(result.Screenshots);
        Assert.Equal(ChatPlatform.Unknown, result.DetectedPlatform);
        Assert.Contains(result.Warnings, warning => warning.Contains("thủ công", StringComparison.OrdinalIgnoreCase)
            || warning.Contains("không thể đọc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Platform_json_contract_uses_explicit_uppercase_strings()
    {
        var json = JsonSerializer.Serialize(new { platform = ChatPlatform.Meta });
        using var document = JsonDocument.Parse(json);

        Assert.Equal("META", document.RootElement.GetProperty("platform").GetString());
        Assert.Equal(ChatPlatform.TikTok, JsonSerializer.Deserialize<ChatPlatform>("\"TIKTOK\""));
    }

    [Fact]
    public void Analyze_endpoint_contract_is_stable_and_requires_order_management()
    {
        var controllerType = typeof(ChatScreenshotsController);
        var route = Assert.Single(controllerType.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        var authorization = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var action = controllerType.GetMethod(nameof(ChatScreenshotsController.Analyze))!;
        var post = Assert.Single(action.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>());

        Assert.Equal("api/admin/quick-import", route.Template);
        Assert.Equal("analyze-screenshots", post.Template);
        Assert.Equal(PermissionNames.OrdersManage, authorization.Policy);
    }

    [Fact]
    public async Task Bundled_vietnamese_tesseract_provider_loads_in_the_local_runtime()
    {
        var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lamie.API"));
        using var provider = new TesseractChatOcrProvider(
            Options.Create(new ChatScreenshotAnalysisOptions
            {
                Enabled = true,
                TessdataPath = "Data/Tessdata"
            }),
            new TestWebHostEnvironment(apiRoot));
        var input = Input("blank.png");
        var prepared = await new ChatImagePreprocessor(Options.Create(new ChatScreenshotAnalysisOptions()))
            .PrepareHeaderAsync(input, ChatPlatform.Unknown, CancellationToken.None);

        var lines = await provider.ReadAsync(prepared, CancellationToken.None);

        Assert.NotNull(lines);
    }

    [Fact]
    public async Task Animated_input_is_decoded_as_one_frame_and_produces_a_bounded_working_copy()
    {
        using var animation = new Image<Rgba32>(160, 360, Color.White);
        using var secondFrame = new Image<Rgba32>(160, 360, Color.Black);
        using var thirdFrame = new Image<Rgba32>(160, 360, Color.Blue);
        animation.Frames.AddFrame(secondFrame.Frames.RootFrame);
        animation.Frames.AddFrame(thirdFrame.Frames.RootFrame);
        using var encoded = new MemoryStream();
        animation.SaveAsGif(encoded);
        var options = Options.Create(new ChatScreenshotAnalysisOptions
        {
            PlatformSignalTargetWidth = 3_000,
            MaximumWorkingImageDimension = 1_000,
            MaximumWorkingImagePixels = 500_000
        });
        var preprocessor = new ChatImagePreprocessor(options);

        var prepared = await preprocessor.PreparePlatformSignalsAsync(
            new ChatScreenshotInput("animated", "animated.gif", "image/gif", encoded.ToArray()),
            CancellationToken.None);

        Assert.InRange(prepared.OcrInput.Width, 1, options.Value.MaximumWorkingImageDimension);
        Assert.InRange(prepared.OcrInput.Height, 1, options.Value.MaximumWorkingImageDimension);
        Assert.True(
            (long)prepared.OcrInput.Width * prepared.OcrInput.Height
            <= options.Value.MaximumWorkingImagePixels);
        using var decodedWorkingCopy = Image.Load<Rgba32>(prepared.OcrInput.PngContent);
        Assert.Single(decodedWorkingCopy.Frames);
        var decoderOptions = typeof(ChatImagePreprocessor)
            .GetField("FirstFrameDecoderOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as DecoderOptions;
        Assert.NotNull(decoderOptions);
        Assert.Equal(1u, decoderOptions.MaxFrames);
    }

    [Theory]
    [InlineData(400, 900, 800, 8)]
    [InlineData(20, 1_000, 16_384, 8)]
    public async Task Invalid_source_dimensions_are_rejected_before_resize(
        int width,
        int height,
        int maximumDimension,
        int maximumAspectRatio)
    {
        using var image = new Image<Rgba32>(width, height, Color.White);
        using var encoded = new MemoryStream();
        image.SaveAsPng(encoded);
        var preprocessor = new ChatImagePreprocessor(Options.Create(new ChatScreenshotAnalysisOptions
        {
            MaximumImageDimension = maximumDimension,
            MaximumImageAspectRatio = maximumAspectRatio
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            preprocessor.PreparePlatformSignalsAsync(
                new ChatScreenshotInput("invalid-size", "invalid-size.png", "image/png", encoded.ToArray()),
                CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_signed_image_is_a_non_blocking_manual_fallback()
    {
        var analyzer = new ChatScreenshotAnalyzer(new FakeOcrProvider(
            new Dictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>>()));
        var malformedPng = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00
        };

        var result = await analyzer.AnalyzeAsync(
            [new ChatScreenshotInput("malformed", "malformed.png", "image/png", malformedPng)],
            CancellationToken.None);

        Assert.Equal(ChatPlatform.Unknown, result.DetectedPlatform);
        Assert.Null(result.DetectedOrdererName);
        Assert.Contains(result.Warnings, warning => warning.Contains("thủ công", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Opt_in_real_reference_fixtures_match_platform_and_header_name()
    {
        var fixtures = new[]
        {
            (Path: Environment.GetEnvironmentVariable("LAMIE_ZALO_SCREENSHOT"), Platform: ChatPlatform.Zalo, Name: "Mèo Anh"),
            (Path: Environment.GetEnvironmentVariable("LAMIE_META_SCREENSHOT"), Platform: ChatPlatform.Meta, Name: "Anh Duy"),
            (Path: Environment.GetEnvironmentVariable("LAMIE_TIKTOK_SCREENSHOT"), Platform: ChatPlatform.TikTok, Name: "Minh Thành")
        };
        if (fixtures.Any(fixture => string.IsNullOrWhiteSpace(fixture.Path) || !File.Exists(fixture.Path)))
            return;

        var apiRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lamie.API"));
        var runtimeOptions = Options.Create(new ChatScreenshotAnalysisOptions());
        using var provider = new TesseractChatOcrProvider(
            runtimeOptions,
            new TestWebHostEnvironment(apiRoot));
        var analyzer = new ChatScreenshotAnalyzer(provider);
        var preprocessor = new ChatImagePreprocessor(runtimeOptions);

        foreach (var fixture in fixtures)
        {
            var path = fixture.Path!;
            var input = new ChatScreenshotInput(
                Path.GetFileName(path),
                Path.GetFileName(path),
                "image/jpeg",
                await File.ReadAllBytesAsync(path));
            var signals = await provider.ReadAsync(
                (await preprocessor.PreparePlatformSignalsAsync(input, CancellationToken.None)).OcrInput,
                CancellationToken.None);
            var result = await analyzer.AnalyzeAsync(
                [input],
                CancellationToken.None);

            Console.WriteLine(
                $"{fixture.Platform}: platform={result.DetectedPlatform} " +
                $"platformConfidence={result.PlatformConfidence:F4} " +
                $"name='{result.DetectedOrdererName}' nameConfidence={result.NameConfidence:F4}");

            Assert.True(
                result.DetectedPlatform == fixture.Platform,
                $"Expected {fixture.Platform}, got {result.DetectedPlatform}. Signal OCR: {string.Join(" | ", signals.Select(line => line.Text))}. Evidence: {string.Join(" | ", result.Screenshots[0].Evidence)}. Warnings: {string.Join(" | ", result.Warnings)}");
            Assert.True(result.PlatformConfidence >= .6m);
            Assert.True(
                string.Equals(fixture.Name, result.DetectedOrdererName, StringComparison.Ordinal),
                $"Expected '{fixture.Name}', got '{result.DetectedOrdererName}'. Header OCR: {string.Join(" | ", result.Screenshots[0].DetectedTexts)}. Warnings: {string.Join(" | ", result.Warnings)}");
            Assert.True(result.NameConfidence >= .5m);
        }
    }

    private static OcrTextLine Line(string text, int left, int top, int width, int height) =>
        new(text, .91m, left, top, width, height, 400, 250);

    private static ChatScreenshotInput Input(string fileName, Rgba32? color = null)
    {
        using var image = new Image<Rgba32>(400, 900, color ?? Color.White);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return new ChatScreenshotInput(fileName, fileName, "image/png", stream.ToArray());
    }

    private sealed class FakeOcrProvider : IChatOcrProvider
    {
        private readonly IReadOnlyDictionary<(string ScreenshotId, ChatOcrRegion Region), IReadOnlyList<OcrTextLine>> _lines;
        private readonly bool _throwForMissing;

        public FakeOcrProvider(
            IReadOnlyDictionary<(string, ChatOcrRegion), IReadOnlyList<OcrTextLine>> lines,
            bool throwForMissing = false)
        {
            _lines = lines;
            _throwForMissing = throwForMissing;
        }

        public Task<IReadOnlyList<OcrTextLine>> ReadAsync(
            ChatOcrInput image,
            CancellationToken cancellationToken)
        {
            if (_lines.TryGetValue((image.ScreenshotId, image.Region), out var lines))
                return Task.FromResult(lines);
            if (_throwForMissing)
                throw new InvalidOperationException("Unreadable fixture.");
            return Task.FromResult<IReadOnlyList<OcrTextLine>>([]);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
        }

        public string ApplicationName { get; set; } = "Lamie.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
