using Bot.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Bot.Core.Enums;

namespace Bot.Core.Services;

public sealed class TaskSchedulerService : ITaskSchedulerService
{
    private readonly IEnumerable<IBotTask> _tasks;
    private readonly IBotModeController _modeController;
    private readonly IArmyLimitMonitorService _armyLimitMonitorService;
    private readonly ILogger<TaskSchedulerService> _logger;
    private volatile bool _forceImmediateArmyCheck;
    private static readonly TimeSpan ArmyMonitorInterval = TimeSpan.FromMinutes(10);

    public TaskSchedulerService(
        IEnumerable<IBotTask> tasks,
        IBotModeController modeController,
        IArmyLimitMonitorService armyLimitMonitorService,
        ILogger<TaskSchedulerService> logger)
    {
        _tasks = tasks;
        _modeController = modeController;
        _armyLimitMonitorService = armyLimitMonitorService;
        _logger = logger;
        _modeController.ModeChanged += OnModeChanged;
    }

    public DateTimeOffset? NextArmyCheckUtc { get; private set; }
    public event Action? SchedulerStateChanged;

    public void RequestImmediateArmyCheck()
    {
        _forceImmediateArmyCheck = true;
        if (_modeController.CurrentMode == BotRunMode.Running)
        {
            _modeController.EnterArmyMonitor("Manual army check requested");
        }
        SchedulerStateChanged?.Invoke();
        _logger.LogInformation("Manual army check requested.");
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Task scheduler started.");
        NextArmyCheckUtc = DateTimeOffset.UtcNow.Add(ArmyMonitorInterval);
        SchedulerStateChanged?.Invoke();

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_modeController.CurrentMode == BotRunMode.ArmyMonitor)
            {
                var now = DateTimeOffset.UtcNow;
                var shouldCheck = _forceImmediateArmyCheck || (NextArmyCheckUtc is not null && now >= NextArmyCheckUtc.Value);
                if (shouldCheck)
                {
                    _forceImmediateArmyCheck = false;
                    _logger.LogInformation("Army monitor tick started.");
                    var result = await _armyLimitMonitorService.CheckNowAsync(cancellationToken);
                    _logger.LogInformation(
                        "Army monitor result readable={Readable} marches={Marches} limit={Limit} atOrAbove={AtOrAbove} reason={Reason}",
                        result.IsReadable,
                        result.DetectedMarches,
                        result.ConfiguredLimit,
                        result.AtOrAboveLimit,
                        result.Reason);

                    NextArmyCheckUtc = DateTimeOffset.UtcNow.Add(ArmyMonitorInterval);
                    SchedulerStateChanged?.Invoke();

                    if (result.IsReadable && !result.AtOrAboveLimit)
                    {
                        _modeController.EnterRunning("Army monitor detected available slot");
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                continue;
            }

            foreach (var task in _tasks.OrderBy(x => x.Priority))
            {
                if (!await task.CanRunAsync(cancellationToken))
                {
                    continue;
                }

                _logger.LogInformation("Executing task: {TaskName}", task.Name);
                await task.ExecuteAsync(cancellationToken);

                if (_modeController.CurrentMode != BotRunMode.Running)
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        _logger.LogInformation("Task scheduler stopped.");
    }

    private void OnModeChanged(BotRunMode mode, string reason)
    {
        if (mode == BotRunMode.ArmyMonitor)
        {
            _forceImmediateArmyCheck = true;
            NextArmyCheckUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            NextArmyCheckUtc = DateTimeOffset.UtcNow.Add(ArmyMonitorInterval);
        }

        SchedulerStateChanged?.Invoke();
        _logger.LogInformation(
            "Scheduler observed mode change: {Mode}. Reason: {Reason}.",
            mode,
            reason);
    }
}
