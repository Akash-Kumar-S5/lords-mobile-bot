using Bot.Core.Enums;

namespace Bot.Core.Interfaces;

public interface IBotModeController
{
    BotRunMode CurrentMode { get; }
    event Action<BotRunMode, string>? ModeChanged;
    void EnterRunning(string reason);
    void EnterArmyMonitor(string reason);
}
