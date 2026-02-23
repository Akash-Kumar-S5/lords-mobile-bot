namespace Bot.Core.Configuration;

public static class ManualDetectionSettings
{
    public static bool UseManualArmyOcrRegion { get; set; } = true;
    public static int ArmyOcrX1 { get; set; } = 69;
    public static int ArmyOcrY1 { get; set; } = 340;
    public static int ArmyOcrX2 { get; set; } = 72;
    public static int ArmyOcrY2 { get; set; } = 371;
    public static int ArmyOcrX3 { get; set; } = 116;
    public static int ArmyOcrY3 { get; set; } = 368;
    public static int ArmyOcrX4 { get; set; } = 113;
    public static int ArmyOcrY4 { get; set; } = 334;

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
