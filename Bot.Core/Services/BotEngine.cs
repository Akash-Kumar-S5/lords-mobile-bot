using Bot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Bot.Core.Services;

public sealed class BotEngine : IBotEngine
{
    private readonly ITaskSchedulerService _taskScheduler;
    private readonly ILogger<BotEngine> _logger;
    private CancellationTokenSource? _cts;
    private Task? _runTask;

    public BotEngine(ITaskSchedulerService taskScheduler, ILogger<BotEngine> logger)
    {
        _taskScheduler = taskScheduler;
        _logger = logger;
    }

    public bool IsRunning => _runTask is { IsCompleted: false };

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Bot is already running.");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => _taskScheduler.RunAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("Bot engine started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning || _cts is null || _runTask is null)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            await _runTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _runTask = null;
        }

        _logger.LogInformation("Bot engine stopped.");
    }
}
