namespace Bot.Infrastructure.Configuration;

public sealed class RuntimeBotSettings : IRuntimeBotSettings
{
    public int MaxActiveMarches { get; set; } = 1;
}
