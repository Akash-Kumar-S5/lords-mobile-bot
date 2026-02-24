namespace Bot.Core.Configuration;

public static class ManualDetectionSettings
{
    // Controls high-frequency click/action logs (tap points, map pans, probe taps).
    // Keep false for long-running stability to avoid very large log files.
    public static bool EnableNormalClickLogs { get; set; } = false;

    public static bool EnableArmyOcrDebug { get; set; } = false;

    public static bool UseManualArmyOcrRegion { get; set; } = true;
    public static int ArmyOcrX1 { get; set; } = 72;
    public static int ArmyOcrY1 { get; set; } = 342;
    public static int ArmyOcrX2 { get; set; } = 100;
    public static int ArmyOcrY2 { get; set; } = 341;
    public static int ArmyOcrX3 { get; set; } = 99;
    public static int ArmyOcrY3 { get; set; } = 368;
    public static int ArmyOcrX4 { get; set; } = 70;
    public static int ArmyOcrY4 { get; set; } = 368;

    public static bool UseManualArmyIndicatorGateRegion { get; set; } = true;
    public static double ArmyIndicatorGateMinConfidence { get; set; } = 0.50;
    public static int ArmyIndicatorGateX1 { get; set; } = 1;
    public static int ArmyIndicatorGateY1 { get; set; } = 333;
    public static int ArmyIndicatorGateX2 { get; set; } = 128;
    public static int ArmyIndicatorGateY2 { get; set; } = 322;
    public static int ArmyIndicatorGateX3 { get; set; } = 122;
    public static int ArmyIndicatorGateY3 { get; set; } = 446;
    public static int ArmyIndicatorGateX4 { get; set; } = 1;
    public static int ArmyIndicatorGateY4 { get; set; } = 452;
}
