namespace Bot.Vision.Interfaces;

public interface IOcrReader
{
    Task<int?> ReadIntegerAsync(
        string screenshotPath,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken = default);

    Task<string> ReadTextAsync(
        string screenshotPath,
        int x,
        int y,
        int width,
        int height,
        CancellationToken cancellationToken = default);
}
