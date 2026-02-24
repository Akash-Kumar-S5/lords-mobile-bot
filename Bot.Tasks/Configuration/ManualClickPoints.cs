using Bot.Core.Configuration;

namespace Bot.Tasks.Configuration;

public static class ManualClickPoints
{
    // When false, no army OCR debug images/meta/log entries are written.
    public static bool EnableArmyOcrDebug
    {
        get => ManualDetectionSettings.EnableArmyOcrDebug;
        set => ManualDetectionSettings.EnableArmyOcrDebug = value;
    }

    // Set true to force march-screen clicks to use the coordinates below.
    public static bool UseManualCoordinates = true;

    // When false, bot clicks deploy immediately after lowest tier (manual mode).
    public static bool VerifyClearSelectionBeforeDeploy = false;

    // Update these with the exact emulator coordinates you want.
    public static int LowestTierX = 1353;
    public static int LowestTierY = 526;
    public static int DeployX = 1232;
    public static int DeployY = 674;

    // Optional: force OCR to read only this exact 4-point region for active march count.
    // Coordinates are absolute screen pixels from your screenshot/emulator.
    public static bool UseManualArmyOcrRegion
    {
        get => ManualDetectionSettings.UseManualArmyOcrRegion;
        set => ManualDetectionSettings.UseManualArmyOcrRegion = value;
    }

    public static int ArmyOcrX1
    {
        get => ManualDetectionSettings.ArmyOcrX1;
        set => ManualDetectionSettings.ArmyOcrX1 = value;
    }

    public static int ArmyOcrY1
    {
        get => ManualDetectionSettings.ArmyOcrY1;
        set => ManualDetectionSettings.ArmyOcrY1 = value;
    }

    public static int ArmyOcrX2
    {
        get => ManualDetectionSettings.ArmyOcrX2;
        set => ManualDetectionSettings.ArmyOcrX2 = value;
    }

    public static int ArmyOcrY2
    {
        get => ManualDetectionSettings.ArmyOcrY2;
        set => ManualDetectionSettings.ArmyOcrY2 = value;
    }

    public static int ArmyOcrX3
    {
        get => ManualDetectionSettings.ArmyOcrX3;
        set => ManualDetectionSettings.ArmyOcrX3 = value;
    }

    public static int ArmyOcrY3
    {
        get => ManualDetectionSettings.ArmyOcrY3;
        set => ManualDetectionSettings.ArmyOcrY3 = value;
    }

    public static int ArmyOcrX4
    {
        get => ManualDetectionSettings.ArmyOcrX4;
        set => ManualDetectionSettings.ArmyOcrX4 = value;
    }

    public static int ArmyOcrY4
    {
        get => ManualDetectionSettings.ArmyOcrY4;
        set => ManualDetectionSettings.ArmyOcrY4 = value;
    }

    // Optional confidence gate: OCR runs only when army_indicator_icon is confidently
    // detected inside this fixed ROI. Confidence scale is 0.0..1.0.
    public static bool UseManualArmyIndicatorGateRegion
    {
        get => ManualDetectionSettings.UseManualArmyIndicatorGateRegion;
        set => ManualDetectionSettings.UseManualArmyIndicatorGateRegion = value;
    }

    public static double ArmyIndicatorGateMinConfidence
    {
        get => ManualDetectionSettings.ArmyIndicatorGateMinConfidence;
        set => ManualDetectionSettings.ArmyIndicatorGateMinConfidence = value;
    }

    public static int ArmyIndicatorGateX1
    {
        get => ManualDetectionSettings.ArmyIndicatorGateX1;
        set => ManualDetectionSettings.ArmyIndicatorGateX1 = value;
    }

    public static int ArmyIndicatorGateY1
    {
        get => ManualDetectionSettings.ArmyIndicatorGateY1;
        set => ManualDetectionSettings.ArmyIndicatorGateY1 = value;
    }

    public static int ArmyIndicatorGateX2
    {
        get => ManualDetectionSettings.ArmyIndicatorGateX2;
        set => ManualDetectionSettings.ArmyIndicatorGateX2 = value;
    }

    public static int ArmyIndicatorGateY2
    {
        get => ManualDetectionSettings.ArmyIndicatorGateY2;
        set => ManualDetectionSettings.ArmyIndicatorGateY2 = value;
    }

    public static int ArmyIndicatorGateX3
    {
        get => ManualDetectionSettings.ArmyIndicatorGateX3;
        set => ManualDetectionSettings.ArmyIndicatorGateX3 = value;
    }

    public static int ArmyIndicatorGateY3
    {
        get => ManualDetectionSettings.ArmyIndicatorGateY3;
        set => ManualDetectionSettings.ArmyIndicatorGateY3 = value;
    }

    public static int ArmyIndicatorGateX4
    {
        get => ManualDetectionSettings.ArmyIndicatorGateX4;
        set => ManualDetectionSettings.ArmyIndicatorGateX4 = value;
    }

    public static int ArmyIndicatorGateY4
    {
        get => ManualDetectionSettings.ArmyIndicatorGateY4;
        set => ManualDetectionSettings.ArmyIndicatorGateY4 = value;
    }
}
