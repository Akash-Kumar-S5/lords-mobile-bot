using Bot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Bot.Core.Services;

public sealed class TaskSchedulerService : ITaskSchedulerService
{
    private readonly IEnumerable<IBotTask> _tasks;
    private readonly ILogger<TaskSchedulerService> _logger;

    public TaskSchedulerService(IEnumerable<IBotTask> tasks, ILogger<TaskSchedulerService> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Task scheduler started.");

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var task in _tasks.OrderBy(x => x.Priority))
            {
                if (!await task.CanRunAsync(cancellationToken))
                {
                    continue;
                }

                _logger.LogInformation("Executing task: {TaskName}", task.Name);
                await task.ExecuteAsync(cancellationToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        _logger.LogInformation("Task scheduler stopped.");
    }
}
