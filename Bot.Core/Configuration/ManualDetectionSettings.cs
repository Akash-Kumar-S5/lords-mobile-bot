namespace Bot.Core.Configuration;

public static class ManualDetectionSettings
{
    // Controls high-frequency click/action logs (tap points, map pans, probe taps).
    // Keep false for long-running stability to avoid very large log files.
    public static bool EnableNormalClickLogs { get; set; } = false;

    public static bool EnableArmyOcrDebug { get; set; } = true;

    public static bool SaveDebugAlways { get; } = string.Equals(
        Environment.GetEnvironmentVariable("BOT_SAVE_ARMY_MONITOR_DEBUG_ALWAYS"),
        "1",
        StringComparison.OrdinalIgnoreCase);


    public static bool UseManualArmyOcrRegion { get; set; } = true;
    public static int ArmyOcrX1 { get; set; } = 64;
    public static int ArmyOcrY1 { get; set; } = 342;
    public static int ArmyOcrX2 { get; set; } = 92;
    public static int ArmyOcrY2 { get; set; } = 343;
    public static int ArmyOcrX3 { get; set; } = 94;
    public static int ArmyOcrY3 { get; set; } = 366;
    public static int ArmyOcrX4 { get; set; } = 68;
    public static int ArmyOcrY4 { get; set; } = 366;

    public static bool UseManualCastleRegion { get; set; } = true;
    public static int CastleRegionX1 { get; set; } = 25;
    public static int CastleRegionY1 { get; set; } = 781;
    public static int CastleRegionX2 { get; set; } = 195;
    public static int CastleRegionY2 { get; set; } = 779;
    public static int CastleRegionX3 { get; set; } = 189;
    public static int CastleRegionY3 { get; set; } = 886;
    public static int CastleRegionX4 { get; set; } = 21;
    public static int CastleRegionY4 { get; set; } = 881;

    public static bool UseManualArmyIndicatorGateRegion { get; set; } = true;
    public static double ArmyIndicatorGateMinConfidence { get; set; } = 0.50;
    public static int ArmyIndicatorGateX1 { get; set; } = 3;
    public static int ArmyIndicatorGateY1 { get; set; } = 339;
    public static int ArmyIndicatorGateX2 { get; set; } = 104;
    public static int ArmyIndicatorGateY2 { get; set; } = 338;
    public static int ArmyIndicatorGateX3 { get; set; } = 107;
    public static int ArmyIndicatorGateY3 { get; set; } = 432;
    public static int ArmyIndicatorGateX4 { get; set; } = 2;
    public static int ArmyIndicatorGateY4 { get; set; } = 441;
}
