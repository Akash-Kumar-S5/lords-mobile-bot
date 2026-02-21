namespace Bot.Vision.Models;

public sealed record DetectionResult(bool IsMatch, double Confidence, int CenterX, int CenterY)
{
    public static DetectionResult NotFound => new(false, 0, 0, 0);
}
