using Bot.Core.Enums;
using Bot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Bot.Core.Services;

public sealed class BotModeController : IBotModeController
{
    private readonly object _sync = new();
    private readonly ILogger<BotModeController> _logger;
    private BotRunMode _currentMode = BotRunMode.Running;

    public BotModeController(ILogger<BotModeController> logger)
    {
        _logger = logger;
    }

    public BotRunMode CurrentMode
    {
        get
        {
            lock (_sync)
            {
                return _currentMode;
            }
        }
    }

    public event Action<BotRunMode, string>? ModeChanged;

    public void EnterRunning(string reason) => SetMode(BotRunMode.Running, reason);

    public void EnterArmyMonitor(string reason) => SetMode(BotRunMode.ArmyMonitor, reason);

    private void SetMode(BotRunMode nextMode, string reason)
    {
        Action<BotRunMode, string>? handlers;
        BotRunMode previous;
        lock (_sync)
        {
            if (_currentMode == nextMode)
            {
                return;
            }

            previous = _currentMode;
            _currentMode = nextMode;
            handlers = ModeChanged;
        }

        _logger.LogInformation(
            "Bot mode changed: {PreviousMode} -> {NextMode}. Reason: {Reason}",
            previous,
            nextMode,
            reason);
        handlers?.Invoke(nextMode, reason);
    }
}
