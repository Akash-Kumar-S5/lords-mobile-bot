using Bot.Vision.Interfaces;
using OpenCvSharp;
using Tesseract;

namespace Bot.Vision.Services;

public sealed class TesseractOcrReader : IOcrReader
{
    public Task<int?> ReadIntegerAsync(
        string screenshotPath,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(screenshotPath))
        {
            throw new FileNotFoundException("Screenshot file not found.", screenshotPath);
        }

        using var screenshot = Cv2.ImRead(screenshotPath, ImreadModes.Color);
        if (screenshot.Empty())
        {
            return Task.FromResult<int?>(null);
        }

        var rx = Math.Clamp(x, 0, Math.Max(0, screenshot.Width - 1));
        var ry = Math.Clamp(y, 0, Math.Max(0, screenshot.Height - 1));
        var rw = Math.Clamp(width, 1, screenshot.Width - rx);
        var rh = Math.Clamp(height, 1, screenshot.Height - ry);
        if (rw <= 1 || rh <= 1)
        {
            return Task.FromResult<int?>(null);
        }

        using var roi = new Mat(screenshot, new OpenCvSharp.Rect(rx, ry, rw, rh));
        using var gray = new Mat();
        using var resized = new Mat();
        using var bin = new Mat();

        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Resize(gray, resized, new Size(gray.Width * 3, gray.Height * 3), interpolation: InterpolationFlags.Cubic);
        Cv2.Threshold(resized, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        var tempPath = Path.Combine(Path.GetTempPath(), $"lordsbot-ocr-{Guid.NewGuid():N}.png");
        Cv2.ImWrite(tempPath, bin);

        try
        {
            using var engine = new TesseractEngine(ResolveTessdataPath(), "eng", EngineMode.Default);
            engine.SetVariable("tessedit_char_whitelist", "0123456789");
            engine.DefaultPageSegMode = PageSegMode.SingleWord;

            using var pix = Pix.LoadFromFile(tempPath);
            using var page = engine.Process(pix);
            var text = (page.GetText() ?? string.Empty).Trim();
            var digitsOnly = new string(text.Where(char.IsDigit).ToArray());
            if (int.TryParse(digitsOnly, out var value))
            {
                return Task.FromResult<int?>(value);
            }

            return Task.FromResult<int?>(null);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // ignore temp file cleanup failures
            }
        }
    }

    private static string ResolveTessdataPath()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("BOT_TESSDATA_PATH"),
            Environment.GetEnvironmentVariable("TESSDATA_PREFIX"),
            Path.Combine(Directory.GetCurrentDirectory(), "Bot.Vision", "Tessdata"),
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Bot.Vision", "Tessdata"))
        };

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (Directory.Exists(candidate!))
            {
                return candidate!;
            }
        }

        throw new DirectoryNotFoundException(
            "Tesseract tessdata folder not found. Set BOT_TESSDATA_PATH or place eng.traineddata under Bot.Vision/Tessdata.");
    }
}
