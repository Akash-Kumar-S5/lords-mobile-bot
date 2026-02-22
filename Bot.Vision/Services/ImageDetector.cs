using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using OpenCvSharp;

namespace Bot.Vision.Services;

public sealed class ImageDetector : IImageDetector
{
    public Task<DetectionResult> FindTemplateAsync(string screenshotPath, string templatePath, double threshold = 0.9, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var screenshot = ReadImage(screenshotPath, "Screenshot file not found.");
        using var template = ReadImage(templatePath, "Template file not found.");
        return Task.FromResult(MatchTemplate(screenshot, template, threshold, 0, 0));
    }

    public Task<DetectionResult> FindTemplateInRegionAsync(
        string screenshotPath,
        string templatePath,
        int x,
        int y,
        int width,
        int height,
        double threshold = 0.9,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var screenshot = ReadImage(screenshotPath, "Screenshot file not found.");
        using var template = ReadImage(templatePath, "Template file not found.");

        var rx = Math.Clamp(x, 0, Math.Max(0, screenshot.Width - 1));
        var ry = Math.Clamp(y, 0, Math.Max(0, screenshot.Height - 1));
        var rw = Math.Clamp(width, 1, screenshot.Width - rx);
        var rh = Math.Clamp(height, 1, screenshot.Height - ry);
        if (rw <= 1 || rh <= 1)
        {
            return Task.FromResult(DetectionResult.NotFound);
        }

        using var region = new Mat(screenshot, new Rect(rx, ry, rw, rh));
        return Task.FromResult(MatchTemplate(region, template, threshold, rx, ry));
    }

    private static Mat ReadImage(string path, string notFoundMessage)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(notFoundMessage, path);
        }

        var image = Cv2.ImRead(path, ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidOperationException($"Failed to decode image: {path}");
        }

        return image;
    }

    private static DetectionResult MatchTemplate(Mat source, Mat template, double threshold, int offsetX, int offsetY)
    {
        if (source.Width < template.Width || source.Height < template.Height)
        {
            return DetectionResult.NotFound;
        }

        using var result = new Mat();
        Cv2.MatchTemplate(source, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxPoint);

        var centerX = offsetX + maxPoint.X + (template.Width / 2);
        var centerY = offsetY + maxPoint.Y + (template.Height / 2);
        var isMatch = maxValue >= threshold;

        return new DetectionResult(isMatch, maxValue, centerX, centerY);
    }
}
