namespace Bot.Infrastructure.Configuration;

public sealed class BotOptions
{
    public required string ScreenshotDirectory { get; init; }
    public required string TemplateDirectory { get; init; }
    public double MatchThreshold { get; init; } = 0.9;
}
