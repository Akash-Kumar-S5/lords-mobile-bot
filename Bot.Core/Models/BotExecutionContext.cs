namespace Bot.Core.Models;

public sealed record BotExecutionContext(string AccountId, string DeviceId, string ScreenshotPath, string TemplateRoot);
