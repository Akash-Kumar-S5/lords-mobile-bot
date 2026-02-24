namespace Bot.Infrastructure.Configuration;

public sealed class RuntimeBotSettings : IRuntimeBotSettings
{
    public int MaxActiveMarches { get; set; } = 1;
    public bool SearchStone { get; set; } = true;
    public bool SearchWood { get; set; } = true;
    public bool SearchOre { get; set; } = true;
    public bool SearchFood { get; set; } = true;
    public bool SearchRune { get; set; } = true;
}
