namespace Bot.Core.Models;

public sealed record ArmyLimitCheckResult(
    bool IsReadable,
    int? DetectedMarches,
    int ConfiguredLimit,
    bool AtOrAboveLimit,
    string Reason);
