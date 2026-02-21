namespace Bot.Core.Interfaces;

public interface ITaskSchedulerService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
