namespace Bot.Core.Models;

public sealed record MonitorPrecheckResult(
    bool IsReady,
    int CloseAttempts,
    bool MapConfirmed,
    string Reason);
