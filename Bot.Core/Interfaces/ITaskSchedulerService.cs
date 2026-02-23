namespace Bot.Core.Interfaces;

public interface ITaskSchedulerService
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void RequestImmediateArmyCheck();
    DateTimeOffset? NextArmyCheckUtc { get; }
    event Action? SchedulerStateChanged;
}
