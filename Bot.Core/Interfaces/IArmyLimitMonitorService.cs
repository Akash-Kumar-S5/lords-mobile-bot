using Bot.Core.Models;

namespace Bot.Core.Interfaces;

public interface IArmyLimitMonitorService
{
    Task<MonitorPrecheckResult> EnsureWorldMapReadyAsync(CancellationToken cancellationToken = default);
    Task<ArmyLimitCheckResult> CheckNowAsync(CancellationToken cancellationToken = default);
}
