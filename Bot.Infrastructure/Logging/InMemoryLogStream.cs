using System.Collections.Concurrent;

namespace Bot.Infrastructure.Logging;

public sealed class InMemoryLogStream : ILogEventStream
{
    private readonly ConcurrentQueue<string> _entries = new();

    public event Action<string>? LogAppended;

    public IReadOnlyCollection<string> Snapshot() => _entries.ToArray();

    public void Append(string message)
    {
        _entries.Enqueue(message);
        LogAppended?.Invoke(message);
    }
}
