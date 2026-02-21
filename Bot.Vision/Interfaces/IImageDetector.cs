using Bot.Vision.Models;

namespace Bot.Vision.Interfaces;

public interface IImageDetector
{
    Task<DetectionResult> FindTemplateAsync(string screenshotPath, string templatePath, double threshold = 0.9, CancellationToken cancellationToken = default);
}
