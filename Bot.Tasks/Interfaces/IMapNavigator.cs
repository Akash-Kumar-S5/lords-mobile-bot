using Bot.Core.Models;
using Bot.Vision.Models;

namespace Bot.Tasks.Interfaces;

public interface IMapNavigator
{
    Task<bool> EnsureOnWorldMapAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task ZoomOutAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task RandomMapPanAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task<DetectionResult> FindResourceTileAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
}
