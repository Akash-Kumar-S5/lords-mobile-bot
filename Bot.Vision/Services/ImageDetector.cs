using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using OpenCvSharp;

namespace Bot.Vision.Services;

public sealed class ImageDetector : IImageDetector
{
    public Task<DetectionResult> FindTemplateAsync(string screenshotPath, string templatePath, double threshold = 0.9, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(screenshotPath))
        {
            throw new FileNotFoundException("Screenshot file not found.", screenshotPath);
        }

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Template file not found.", templatePath);
        }

        using var screenshot = Cv2.ImRead(screenshotPath, ImreadModes.Color);
        using var template = Cv2.ImRead(templatePath, ImreadModes.Color);
        using var result = new Mat();

        Cv2.MatchTemplate(screenshot, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxPoint);

        var centerX = maxPoint.X + (template.Width / 2);
        var centerY = maxPoint.Y + (template.Height / 2);
        var isMatch = maxValue >= threshold;

        return Task.FromResult(new DetectionResult(isMatch, maxValue, centerX, centerY));
    }
}
