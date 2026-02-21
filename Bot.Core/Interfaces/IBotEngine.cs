namespace Bot.Core.Interfaces;

public interface IBotEngine
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
