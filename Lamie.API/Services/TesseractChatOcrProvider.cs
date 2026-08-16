using System.Diagnostics;
using System.Globalization;
using Lamie.Application.ChatAnalysis;
using Microsoft.Extensions.Options;
using Tesseract;

namespace Lamie.API.Services;

public sealed class TesseractChatOcrProvider : IChatOcrProvider, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TesseractEngine? _windowsEngine;
    private readonly bool _enabled;
    private readonly string _dataPath;
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;

    public TesseractChatOcrProvider(
        IOptions<ChatScreenshotAnalysisOptions> options,
        IWebHostEnvironment environment)
    {
        var settings = options.Value;
        _enabled = settings.Enabled;
        _dataPath = Path.IsPathRooted(settings.TessdataPath)
            ? settings.TessdataPath
            : Path.Combine(environment.ContentRootPath, settings.TessdataPath);
        _executablePath = settings.TesseractExecutablePath;
        _timeout = TimeSpan.FromSeconds(settings.OcrTimeoutSeconds);

        if (!_enabled || !HasLanguageData(_dataPath) || !OperatingSystem.IsWindows())
            return;

        try
        {
            _windowsEngine = new TesseractEngine(_dataPath, "vie+eng", EngineMode.LstmOnly)
            {
                DefaultPageSegMode = PageSegMode.SparseText
            };
            _windowsEngine.SetVariable("preserve_interword_spaces", "1");
        }
        catch (Exception exception) when (exception is TesseractException
            or DllNotFoundException
            or BadImageFormatException
            or TypeInitializationException)
        {
            _windowsEngine = null;
        }
    }

    public async Task<IReadOnlyList<OcrTextLine>> ReadAsync(
        ChatOcrInput image,
        CancellationToken cancellationToken)
    {
        if (!_enabled || !HasLanguageData(_dataPath))
            throw new InvalidOperationException("Vietnamese OCR is not available in this environment.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _windowsEngine is not null
                ? ReadWithWindowsEngine(image, cancellationToken)
                : await ReadWithCommandLineAsync(image, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<OcrTextLine> ReadWithWindowsEngine(
        ChatOcrInput image,
        CancellationToken cancellationToken)
    {
        using var pix = Pix.LoadFromMemory(image.PngContent);
        var pageSegmentationMode = image.Region == ChatOcrRegion.ConversationHeader
            ? PageSegMode.SingleLine
            : PageSegMode.SparseText;
        using var page = _windowsEngine!.Process(pix, pageSegmentationMode);
        using var iterator = page.GetIterator();
        iterator.Begin();
        var lines = new List<OcrTextLine>();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim();
            if (string.IsNullOrWhiteSpace(text)
                || !iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds))
                continue;
            lines.Add(new OcrTextLine(
                text,
                Math.Clamp((decimal)iterator.GetConfidence(PageIteratorLevel.TextLine) / 100m, 0m, 1m),
                bounds.X1,
                bounds.Y1,
                Math.Max(0, bounds.Width),
                Math.Max(0, bounds.Height),
                image.Width,
                image.Height));
        } while (iterator.Next(PageIteratorLevel.TextLine));
        return lines;
    }

    private async Task<IReadOnlyList<OcrTextLine>> ReadWithCommandLineAsync(
        ChatOcrInput image,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("stdin");
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("--tessdata-dir");
        startInfo.ArgumentList.Add(_dataPath);
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("vie+eng");
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add(image.Region == ChatOcrRegion.ConversationHeader ? "7" : "11");
        startInfo.ArgumentList.Add("tsv");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("OCR process could not be started.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("OCR executable is unavailable.", exception);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.BaseStream.WriteAsync(image.PngContent, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("OCR process failed.");
            return ParseTsv(output, image.Width, image.Height);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("OCR processing timed out.");
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static IReadOnlyList<OcrTextLine> ParseTsv(string tsv, int imageWidth, int imageHeight)
    {
        var words = new List<TsvWord>();
        foreach (var row in tsv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var columns = row.Split('\t', 12);
            if (columns.Length < 12
                || columns[0] != "5"
                || !int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var block)
                || !int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var paragraph)
                || !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var line)
                || !int.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
                || !int.TryParse(columns[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top)
                || !int.TryParse(columns[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || !int.TryParse(columns[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
                || !decimal.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                || confidence < 0m
                || string.IsNullOrWhiteSpace(columns[11]))
                continue;
            words.Add(new TsvWord(
                block,
                paragraph,
                line,
                columns[11].Trim(),
                Math.Clamp(confidence / 100m, 0m, 1m),
                left,
                top,
                width,
                height));
        }

        return words
            .GroupBy(word => (word.Block, word.Paragraph, word.Line))
            .Select(group =>
            {
                var ordered = group.OrderBy(word => word.Left).ToList();
                var left = ordered.Min(word => word.Left);
                var top = ordered.Min(word => word.Top);
                var right = ordered.Max(word => word.Left + word.Width);
                var bottom = ordered.Max(word => word.Top + word.Height);
                var weight = ordered.Sum(word => Math.Max(1, word.Text.Length));
                var confidence = ordered.Sum(word => word.Confidence * Math.Max(1, word.Text.Length)) / weight;
                return new OcrTextLine(
                    string.Join(' ', ordered.Select(word => word.Text)),
                    confidence,
                    left,
                    top,
                    Math.Max(0, right - left),
                    Math.Max(0, bottom - top),
                    imageWidth,
                    imageHeight);
            })
            .OrderBy(line => line.Top)
            .ThenBy(line => line.Left)
            .ToList();
    }

    private static bool HasLanguageData(string dataPath) =>
        File.Exists(Path.Combine(dataPath, "vie.traineddata"))
        && File.Exists(Path.Combine(dataPath, "eng.traineddata"));

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _windowsEngine?.Dispose();
        _gate.Dispose();
    }

    private sealed record TsvWord(
        int Block,
        int Paragraph,
        int Line,
        string Text,
        decimal Confidence,
        int Left,
        int Top,
        int Width,
        int Height);
}
