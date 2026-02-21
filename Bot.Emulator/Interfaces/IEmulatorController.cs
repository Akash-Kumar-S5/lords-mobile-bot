namespace Bot.Emulator.Interfaces;

public interface IEmulatorController
{
    Task StartServerAsync(CancellationToken cancellationToken = default);
    Task<string?> GetConnectedDeviceAsync(CancellationToken cancellationToken = default);
    Task TapAsync(int x, int y, CancellationToken cancellationToken = default);
    Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs = 300, CancellationToken cancellationToken = default);
    Task<string> TakeScreenshotAsync(string outputPath, CancellationToken cancellationToken = default);
    Task<(int Width, int Height)> GetResolutionAsync(CancellationToken cancellationToken = default);
}
