using Bot.Emulator.Interfaces;
using AdvancedSharpAdbClient;
using Microsoft.Extensions.Logging;

namespace Bot.Emulator.Services;

public sealed class DeviceManager : IDeviceManager
{
    private readonly ILogger<DeviceManager> _logger;
    private readonly AdvancedAdbClient _adbClient;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private string? _cachedDevice;

    public DeviceManager(ILogger<DeviceManager> logger)
    {
        _logger = logger;
        _adbClient = new AdvancedAdbClient();
    }

    public async Task<string?> GetActiveDeviceAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedDevice) && await IsDeviceOnlineAsync(_cachedDevice, cancellationToken))
            {
                return _cachedDevice;
            }

            _cachedDevice = await DiscoverDeviceAsync(cancellationToken);
            return _cachedDevice;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        var device = await GetActiveDeviceAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(device))
        {
            return true;
        }

        _logger.LogWarning("No online emulator device detected. Retrying discovery.");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        device = await DiscoverDeviceAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(device);
    }

    private Task<string?> DiscoverDeviceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = _adbClient.GetDevices();
        foreach (var device in devices)
        {
            if (device.State == DeviceState.Online)
            {
                _logger.LogInformation("Active emulator device detected: {Device}", device.Serial);
                return Task.FromResult<string?>(device.Serial);
            }
        }

        return Task.FromResult<string?>(null);
    }

    private Task<bool> IsDeviceOnlineAsync(string serial, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _adbClient.GetDevices().FirstOrDefault(x => x.Serial == serial);
        return Task.FromResult(device?.State == DeviceState.Online);
    }
}
