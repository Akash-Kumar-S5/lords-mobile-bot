namespace Bot.Infrastructure.Configuration;

public interface IRuntimeBotSettings
{
    int MaxActiveMarches { get; set; }
    bool SearchStone { get; set; }
    bool SearchWood { get; set; }
    bool SearchOre { get; set; }
    bool SearchFood { get; set; }
    bool SearchRune { get; set; }
}
