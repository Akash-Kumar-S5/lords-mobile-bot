namespace Bot.Emulator.Interfaces;

public interface IDeviceManager
{
    Task<string?> GetActiveDeviceAsync(CancellationToken cancellationToken = default);
    Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default);
}
