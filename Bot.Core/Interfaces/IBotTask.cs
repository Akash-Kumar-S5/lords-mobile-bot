namespace Bot.Core.Interfaces;

public interface IBotTask
{
    string Name { get; }
    int Priority { get; }
    Task<bool> CanRunAsync(CancellationToken cancellationToken = default);
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
