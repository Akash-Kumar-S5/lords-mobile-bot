using Bot.Core.Enums;
using Bot.Core.Models;

namespace Bot.Core.Interfaces;

public interface IStateResolver
{
    Task<GameState> ResolveAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task<bool> IsCityViewAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task<bool> IsWorldMapAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task<bool> IsTilePopupAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task<bool> IsResourcePopupAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
    Task<bool> IsMarchScreenAsync(BotExecutionContext context, CancellationToken cancellationToken = default);
}
