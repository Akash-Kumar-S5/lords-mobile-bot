namespace Bot.Tasks.Configuration;

public static class ManualClickPoints
{
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
    public static bool UseManualArmyOcrRegion = true;
    public static int ArmyOcrX1 = 69;
    public static int ArmyOcrY1 = 340;
    public static int ArmyOcrX2 = 72;
    public static int ArmyOcrY2 = 371;
    public static int ArmyOcrX3 = 116;
    public static int ArmyOcrY3 = 368;
    public static int ArmyOcrX4 = 113;
    public static int ArmyOcrY4 = 334;

    // Optional confidence gate: OCR runs only when army_indicator_icon is confidently
    // detected inside this fixed ROI. Confidence scale is 0.0..1.0.
    public static bool UseManualArmyIndicatorGateRegion = true;
    public static double ArmyIndicatorGateMinConfidence = 0.50;
    public static int ArmyIndicatorGateX1 = 1;
    public static int ArmyIndicatorGateY1 = 333;
    public static int ArmyIndicatorGateX2 = 128;
    public static int ArmyIndicatorGateY2 = 322;
    public static int ArmyIndicatorGateX3 = 122;
    public static int ArmyIndicatorGateY3 = 446;
    public static int ArmyIndicatorGateX4 = 1;
    public static int ArmyIndicatorGateY4 = 452;
}
