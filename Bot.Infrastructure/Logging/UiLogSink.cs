using Serilog.Core;
using Serilog.Events;

namespace Bot.Infrastructure.Logging;

public sealed class UiLogSink : ILogEventSink
{
    private readonly InMemoryLogStream _stream;

    public UiLogSink(InMemoryLogStream stream)
    {
        _stream = stream;
    }

    public void Emit(LogEvent logEvent)
    {
        var message = $"[{logEvent.Timestamp:HH:mm:ss}] {logEvent.Level}: {logEvent.RenderMessage()}";
        _stream.Append(message);
    }
}
