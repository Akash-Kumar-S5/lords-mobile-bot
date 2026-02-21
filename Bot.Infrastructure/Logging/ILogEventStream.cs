namespace Bot.Infrastructure.Logging;

public interface ILogEventStream
{
    event Action<string>? LogAppended;
    IReadOnlyCollection<string> Snapshot();
}
