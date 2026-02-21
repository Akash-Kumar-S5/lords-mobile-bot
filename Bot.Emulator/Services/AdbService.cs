using Bot.Emulator.Interfaces;
using AdvancedSharpAdbClient;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Bot.Emulator.Services;

public sealed class AdbService : IEmulatorController
{
    private readonly ILogger<AdbService> _logger;
    private readonly IDeviceManager _deviceManager;
    private readonly AdbServer _adbServer;
    private readonly string _adbExecutable;

    public AdbService(ILogger<AdbService> logger, IDeviceManager deviceManager)
    {
        _logger = logger;
        _deviceManager = deviceManager;
        _adbServer = new AdbServer();
        _adbExecutable = ResolveAdbExecutable();
    }

    public Task StartServerAsync(CancellationToken cancellationToken = default)
        => StartServerInternalAsync(cancellationToken);

    public Task<string?> GetConnectedDeviceAsync(CancellationToken cancellationToken = default)
        => _deviceManager.GetActiveDeviceAsync(cancellationToken);

    public async Task TapAsync(int x, int y, CancellationToken cancellationToken = default)
    {
        var serial = await GetSerialAsync(cancellationToken);
        await ExecuteAdbForDeviceAsync(serial, $"shell input tap {x} {y}", cancellationToken);
    }

    public async Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs = 300, CancellationToken cancellationToken = default)
    {
        var serial = await GetSerialAsync(cancellationToken);
        await ExecuteAdbForDeviceAsync(serial, $"shell input swipe {x1} {y1} {x2} {y2} {durationMs}", cancellationToken);
    }

    public async Task<string> TakeScreenshotAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        var serial = await GetSerialAsync(cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        var remotePath = "/sdcard/lordsbot_screen.png";

        await ExecuteAdbForDeviceAsync(serial, $"shell screencap -p {remotePath}", cancellationToken);
        await ExecuteAdbForDeviceAsync(serial, $"pull {remotePath} \"{outputPath}\"", cancellationToken);
        _ = ExecuteAdbForDeviceAsync(serial, $"shell rm {remotePath}", CancellationToken.None);

        return outputPath;
    }

    public async Task<(int Width, int Height)> GetResolutionAsync(CancellationToken cancellationToken = default)
    {
        var serial = await GetSerialAsync(cancellationToken);
        var output = await ExecuteAdbForDeviceAsync(serial, "shell wm size", cancellationToken);
        var marker = "Physical size:";
        var idx = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            throw new InvalidOperationException($"Unable to parse wm size output: {output}");
        }

        var dims = output[(idx + marker.Length)..].Trim().Split('x', StringSplitOptions.TrimEntries);
        if (dims.Length != 2 || !int.TryParse(dims[0], out var width) || !int.TryParse(dims[1], out var height))
        {
            throw new InvalidOperationException($"Invalid wm size format: {output}");
        }

        return (width, height);
    }

    private async Task StartServerInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await Task.Run(() => _adbServer.StartServer(_adbExecutable, false), cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            _logger.LogInformation("ADB server start status: {Status}", result);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ADB start-server timed out. Continuing with existing daemon if available.");
        }
    }

    private async Task<string> GetSerialAsync(CancellationToken cancellationToken)
    {
        var serial = await _deviceManager.GetActiveDeviceAsync(cancellationToken)
                     ?? throw new InvalidOperationException("No connected emulator device available.");
        return serial;
    }

    private async Task<string> ExecuteAdbForDeviceAsync(string serial, string arguments, CancellationToken cancellationToken)
    {
        var fullArgs = $"-s {serial} {arguments}";
        return await ExecuteAdbAsync(fullArgs, cancellationToken);
    }

    private async Task<string> ExecuteAdbAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _adbExecutable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb {arguments} failed ({process.ExitCode}): {stderr}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogDebug("adb stderr for '{Args}': {Stderr}", arguments, stderr.Trim());
        }

        return stdout;
    }

    private static string ResolveAdbExecutable()
    {
        var envPath = Environment.GetEnvironmentVariable("ADB_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        const string ldPlayerAdb = @"D:\LDPlayer\LDPlayer9\adb.exe";
        if (File.Exists(ldPlayerAdb))
        {
            return ldPlayerAdb;
        }

        return "adb";
    }
}
