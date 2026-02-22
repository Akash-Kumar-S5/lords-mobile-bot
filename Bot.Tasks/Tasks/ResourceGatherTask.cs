using Bot.Core.Enums;
using Bot.Core.Interfaces;
using Bot.Core.Models;
using Bot.Emulator.Interfaces;
using Bot.Infrastructure.Configuration;
using Bot.Tasks.Interfaces;
using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using Microsoft.Extensions.Logging;

namespace Bot.Tasks.Tasks;

public sealed class ResourceGatherTask : IBotTask
{
    private static readonly string[] GatherTemplates =
    {
        "gather_button.png"
    };

    private static readonly string[] LowestTierTemplates =
    {
        "lowest_tier_button.png"
    };

    private static readonly string[] DeployTemplates =
    {
        "deploy_button.png"
    };

    private static readonly string[] TilePopupTemplates =
    {
        "transfer_button.png",
        "occupy_button.png"
    };

    private static readonly string[] PopupCloseTemplates =
    {
        "popup_close.png"
    };

    private static readonly double[] ActionThresholds = { 0.80, 0.72, 0.64, 0.56, 0.48, 0.40 };
    private static readonly double[] MarchActionThresholds = { 0.72, 0.64, 0.56, 0.48, 0.40, 0.36, 0.32 };
    private static readonly double[] ArmyIndicatorThresholds = { 0.85, 0.75, 0.65, 0.58, 0.52, 0.45, 0.38, 0.30 };

    private static readonly string[] ArmyIndicatorTemplates =
    {
        "army_indicator_icon.png",
        "march_button.png"
    };

    private const int StepTimeoutSeconds = 45;
    private static readonly TimeSpan MarchSlotWaitTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan ArmyLimitRecheckInterval = TimeSpan.FromSeconds(10);

    private readonly IEmulatorController _emulatorController;
    private readonly IStateResolver _stateResolver;
    private readonly IMapNavigator _mapNavigator;
    private readonly IImageDetector _imageDetector;
    private readonly IOcrReader _ocrReader;
    private readonly IRuntimeBotSettings _runtimeBotSettings;
    private readonly ILogger<ResourceGatherTask> _logger;
    private readonly Random _random = Random.Shared;

    public ResourceGatherTask(
        IEmulatorController emulatorController,
        IStateResolver stateResolver,
        IMapNavigator mapNavigator,
        IImageDetector imageDetector,
        IOcrReader ocrReader,
        IRuntimeBotSettings runtimeBotSettings,
        ILogger<ResourceGatherTask> logger)
    {
        _emulatorController = emulatorController;
        _stateResolver = stateResolver;
        _mapNavigator = mapNavigator;
        _imageDetector = imageDetector;
        _ocrReader = ocrReader;
        _runtimeBotSettings = runtimeBotSettings;
        _logger = logger;
    }

    public string Name => "Resource Gather";
    public int Priority => 100;

    public Task<bool> CanRunAsync(CancellationToken cancellationToken = default)
    {
        return _emulatorController.GetConnectedDeviceAsync(cancellationToken)
            .ContinueWith(
                t => !string.IsNullOrWhiteSpace(t.Result),
                cancellationToken,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resource gather task loop started.");
        await TryWarmupAdbAsync(cancellationToken);
        _logger.LogInformation("Entering gather execution loop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Loop tick: building execution context.");
                var context = await BuildContextAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
                _logger.LogDebug("Loop tick: resolving game state.");
                var state = await ResolveStateWithFreshScreenshotAsync(context, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                _logger.LogInformation("Current state: {State}", state);

                if (await IsAtArmyLimitAsync(context, cancellationToken))
                {
                    var limit = Math.Max(1, _runtimeBotSettings.MaxActiveMarches);
                    _logger.LogInformation(
                        "Max army limit reached (active marches >= {Limit}). Rechecking in {Seconds}s.",
                        limit,
                        ArmyLimitRecheckInterval.TotalSeconds);
                    await Task.Delay(ArmyLimitRecheckInterval, cancellationToken);
                    continue;
                }

                switch (state)
                {
                    case GameState.City:
                        await ExecuteWithRetryAsync(
                            "Ensure world map from city",
                            ct => EnsureWorldMapWithFreshCaptureAsync(context, ct),
                            3,
                            cancellationToken);
                        break;

                    case GameState.WorldMap:
                        var gathered = await ExecuteWithRetryAsync(
                            "Gather sequence",
                            ct => ExecuteGatherSequenceAsync(context, ct),
                            3,
                            cancellationToken);

                        if (gathered)
                        {
                            await WaitUntilMarchSlotFreeAsync(context, cancellationToken);
                        }
                        break;

                    case GameState.ResourcePopup:
                        await ExecuteWithRetryAsync(
                            "Recover from resource popup",
                            ct => ClickGatherAndMarchAsync(context, ct),
                            3,
                            cancellationToken);
                        break;

                    case GameState.TilePopup:
                        await ExecuteWithRetryAsync(
                            "Recover from tile popup",
                            ct => DismissTilePopupAsync(context, ct),
                            3,
                            cancellationToken);
                        break;

                    case GameState.MarchScreen:
                        await ExecuteWithRetryAsync(
                            "Recover from march screen",
                            ct => TrySelectLowestTierAndDeployAsync(context, ct),
                            3,
                            cancellationToken);
                        break;

                    default:
                        _logger.LogWarning("Unknown state encountered. Running map recovery.");
                        await RecoverToWorldMapAsync(context, cancellationToken);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning("Loop timeout detected: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in resource gather loop. Triggering recovery.");
            }

            await RandomDelayAsync(300, 1200, cancellationToken);
        }

        _logger.LogInformation("Resource gather task loop stopped.");
    }

    private async Task TryWarmupAdbAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting ADB warmup phase.");
            await _emulatorController.StartServerAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            _logger.LogInformation("ADB startup phase completed.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("ADB warmup skipped: {Reason}", ex.Message);
        }
    }

    private async Task<bool> ExecuteGatherSequenceAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        if (!await EnsureWorldMapWithFreshCaptureAsync(context, cancellationToken))
        {
            return false;
        }

        if (_random.NextDouble() < 0.2)
        {
            await _mapNavigator.ZoomOutAsync(context, cancellationToken);
            await RandomDelayAsync(350, 900, cancellationToken);
        }

        if (_random.NextDouble() < 0.3)
        {
            await _mapNavigator.RandomMapPanAsync(context, cancellationToken);
            await RandomDelayAsync(300, 900, cancellationToken);
        }

        context = await CaptureContextAsync(context, cancellationToken);
        var resourceTile = await _mapNavigator.FindResourceTileAsync(context, cancellationToken);
        if (!resourceTile.IsMatch)
        {
            _logger.LogWarning("No resource tile detected, confidence={Confidence:F3}.", resourceTile.Confidence);
            await _mapNavigator.RandomMapPanAsync(context, cancellationToken);
            return false;
        }

        if (!await TryOpenResourcePopupAsync(context, resourceTile, cancellationToken))
        {
            _logger.LogWarning("Resource popup did not appear after tile click.");
            return false;
        }

        return await ClickGatherAndMarchAsync(context, cancellationToken);
    }

    private async Task<bool> ClickGatherAndMarchAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var gather = await FindBestTemplateAsync(context, GatherTemplates, ActionThresholds, cancellationToken);
        if (!gather.IsMatch)
        {
            _logger.LogWarning("Gather button not found.");
            return false;
        }

        await TapWithOffsetAsync(gather.CenterX, gather.CenterY, cancellationToken);
        _logger.LogInformation("Clicked gather button.");

        await RandomDelayAsync(500, 1100, cancellationToken);
        _logger.LogInformation("Post-gather mode: focusing only on lowest tier and deploy buttons.");
        if (!await WaitForMarchControlsAsync(context, TimeSpan.FromSeconds(12), cancellationToken))
        {
            _logger.LogWarning("March controls did not appear after gather.");
            return false;
        }

        return await TrySelectLowestTierAndDeployAsync(context, cancellationToken);
    }

    private async Task<bool> TrySelectLowestTierAndDeployAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var lowestTier = await FindBestTemplateAsync(context, LowestTierTemplates, MarchActionThresholds, cancellationToken);
        if (!lowestTier.IsMatch)
        {
            // Some screens may already have lowest tier selected; allow direct deploy fallback.
            _logger.LogWarning("Lowest tier button not found on march screen. Trying deploy fallback.");
        }
        else
        {
            await TapWithOffsetAsync(lowestTier.CenterX, lowestTier.CenterY, cancellationToken);
            _logger.LogInformation("Clicked lowest tier button.");
            await RandomDelayAsync(420, 900, cancellationToken);
        }

        var deploy = await FindBestTemplateAsync(context, DeployTemplates, MarchActionThresholds, cancellationToken);
        if (!deploy.IsMatch)
        {
            _logger.LogWarning("Deploy button not found after selecting lowest tier.");
            return false;
        }

        await TapWithOffsetAsync(deploy.CenterX, deploy.CenterY, cancellationToken);
        _logger.LogInformation("Clicked deploy button. Gather dispatched successfully.");
        await RandomDelayAsync(450, 1100, cancellationToken);
        return true;
    }

    private async Task<bool> WaitForMarchControlsAsync(
        BotExecutionContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            var lowestTier = await FindBestTemplateAsync(context, LowestTierTemplates, MarchActionThresholds, cancellationToken);
            if (lowestTier.IsMatch)
            {
                _logger.LogInformation("March controls ready: lowest tier detected.");
                return true;
            }

            var deploy = await FindBestTemplateAsync(context, DeployTemplates, MarchActionThresholds, cancellationToken);
            if (deploy.IsMatch)
            {
                _logger.LogInformation("March controls ready: deploy detected.");
                return true;
            }

            await RandomDelayAsync(220, 520, cancellationToken);
        }

        return false;
    }

    private async Task<bool> IsAtArmyLimitAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var configuredLimit = Math.Max(1, _runtimeBotSettings.MaxActiveMarches);
        var marchCount = await DetectActiveMarchCountAsync(context, cancellationToken);
        if (marchCount is null)
        {
            _logger.LogInformation("March count icon missing or unreadable. Continuing resource collection.");
            return false;
        }

        _logger.LogInformation(
            "Detected active marches: {ActiveMarches}. Configured max army limit: {ConfiguredLimit}.",
            marchCount.Value,
            configuredLimit);

        return marchCount.Value >= configuredLimit;
    }

    private async Task<int?> DetectActiveMarchCountAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        context = await CaptureContextAsync(context, cancellationToken);

        var indicator = await FindBestTemplateAsync(context, ArmyIndicatorTemplates, ArmyIndicatorThresholds, cancellationToken);
        if (!indicator.IsMatch)
        {
            _logger.LogDebug("Army indicator icon not found for OCR.");
            return null;
        }

        // OCR is intentionally restricted to the area above the indicator badge
        // to avoid picking unrelated numbers elsewhere in the UI.
        var rois = new (int Dx, int Dy, int W, int H)[]
        {
            (12, -56, 34, 22),
            (16, -52, 38, 24),
            (8, -50, 30, 20)
        };

        foreach (var (dx, dy, w, h) in rois)
        {
            var x = indicator.CenterX + dx;
            var y = indicator.CenterY + dy;
            var value = await _ocrReader.ReadIntegerAsync(context.ScreenshotPath, x, y, w, h, cancellationToken);
            _logger.LogDebug(
                "Army OCR probe roi=({X},{Y},{W},{H}) value={Value}",
                x,
                y,
                w,
                h,
                value.HasValue ? value.Value.ToString() : "null");

            if (value is >= 0 and <= 9)
            {
                return value;
            }
        }

        return null;
    }

    private async Task WaitUntilMarchSlotFreeAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        _logger.LogInformation("Waiting for a march slot to become free.");

        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow - started < MarchSlotWaitTimeout)
        {
            var probeReady = await ExecuteWithRetryAsync(
                "March slot probe",
                async ct =>
                {
                    if (!await EnsureWorldMapWithFreshCaptureAsync(context, ct))
                    {
                        return false;
                    }

                    context = await CaptureContextAsync(context, ct);
                    var tile = await _mapNavigator.FindResourceTileAsync(context, ct);
                    if (!tile.IsMatch)
                    {
                        await _mapNavigator.RandomMapPanAsync(context, ct);
                        return false;
                    }

                    await TapWithOffsetAsync(tile.CenterX, tile.CenterY, ct);
                    await RandomDelayAsync(450, 900, ct);
                    if (!await WaitForStateAsync(GameState.ResourcePopup, context, TimeSpan.FromSeconds(6), ct))
                    {
                        return false;
                    }

                    var gather = await FindBestTemplateAsync(context, GatherTemplates, ActionThresholds, ct);
                    if (!gather.IsMatch)
                    {
                        return false;
                    }

                    await TapWithOffsetAsync(gather.CenterX, gather.CenterY, ct);
                    await RandomDelayAsync(450, 900, ct);

                    if (!await WaitForStateAsync(GameState.MarchScreen, context, TimeSpan.FromSeconds(6), ct))
                    {
                        return false;
                    }

                    var deploy = await FindBestTemplateAsync(context, DeployTemplates, ActionThresholds, ct);
                    return deploy.IsMatch;
                },
                1,
                cancellationToken);

            if (probeReady)
            {
                _logger.LogInformation("March slot is available again.");
                await RecoverToWorldMapAsync(context, cancellationToken);
                return;
            }

            _logger.LogInformation("March slot still busy. Waiting before next probe.");
            await RecoverToWorldMapAsync(context, cancellationToken);
            await RandomDelayAsync(8_000, 14_000, cancellationToken);
        }

        _logger.LogWarning("Timed out waiting for march slot availability.");
    }

    private async Task<bool> EnsureWorldMapWithFreshCaptureAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        context = await CaptureContextAsync(context, cancellationToken);
        return await _mapNavigator.EnsureOnWorldMapAsync(context, cancellationToken);
    }

    private async Task RecoverToWorldMapAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var onMap = await EnsureWorldMapWithFreshCaptureAsync(context, cancellationToken);
        if (onMap)
        {
            return;
        }

        await _mapNavigator.RandomMapPanAsync(context, cancellationToken);
        await RandomDelayAsync(350, 900, cancellationToken);
        _ = await EnsureWorldMapWithFreshCaptureAsync(context, cancellationToken);
    }

    private async Task<GameState> ResolveStateWithFreshScreenshotAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        context = await CaptureContextAsync(context, cancellationToken);
        return await _stateResolver.ResolveAsync(context, cancellationToken);
    }

    private async Task<bool> WaitForStateAsync(
        GameState expectedState,
        BotExecutionContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            var state = await ResolveStateWithFreshScreenshotAsync(context, cancellationToken);
            if (state == expectedState)
            {
                return true;
            }

            await RandomDelayAsync(250, 600, cancellationToken);
        }

        return false;
    }

    private async Task<DetectionResult> FindAndLogAsync(
        BotExecutionContext context,
        string templateName,
        double threshold,
        CancellationToken cancellationToken)
    {
        context = await CaptureContextAsync(context, cancellationToken);
        var templatePath = Path.Combine(context.TemplateRoot, templateName);
        if (!File.Exists(templatePath))
        {
            _logger.LogWarning("Template missing: {TemplatePath}", templatePath);
            return DetectionResult.NotFound;
        }

        var detection = await _imageDetector.FindTemplateAsync(context.ScreenshotPath, templatePath, threshold, cancellationToken);
        _logger.LogInformation(
            "Template {Template} match={Match} confidence={Confidence:F3} center=({X},{Y})",
            templateName,
            detection.IsMatch,
            detection.Confidence,
            detection.CenterX,
            detection.CenterY);
        return detection;
    }

    private async Task<DetectionResult> FindBestTemplateAsync(
        BotExecutionContext context,
        IReadOnlyCollection<string> templates,
        IReadOnlyCollection<double> thresholds,
        CancellationToken cancellationToken)
    {
        var best = DetectionResult.NotFound;
        string chosenTemplate = "none";

        foreach (var template in templates)
        {
            foreach (var threshold in thresholds)
            {
                var detection = await FindAndLogAsync(context, template, threshold, cancellationToken);
                if (!detection.IsMatch)
                {
                    continue;
                }

                if (!best.IsMatch || detection.Confidence > best.Confidence)
                {
                    best = detection;
                    chosenTemplate = template;
                }
            }
        }

        if (best.IsMatch)
        {
            _logger.LogInformation(
                "Action template selected template={Template} confidence={Confidence:F3} center=({X},{Y})",
                chosenTemplate,
                best.Confidence,
                best.CenterX,
                best.CenterY);
        }

        return best;
    }

    private async Task<bool> TryOpenResourcePopupAsync(
        BotExecutionContext context,
        DetectionResult resourceTile,
        CancellationToken cancellationToken)
    {
        // Resource hitboxes on world map are small; probe nearby offsets around best match center.
        var probeOffsets = new (int Dx, int Dy)[]
        {
            (0, 0), (18, 0), (-18, 0), (0, 18), (0, -18),
            (28, 12), (-28, 12), (28, -12), (-28, -12),
            (0, 30), (0, -30)
        };

        foreach (var (dx, dy) in probeOffsets)
        {
            var x = resourceTile.CenterX + dx;
            var y = resourceTile.CenterY + dy;

            await TapWithOffsetAsync(x, y, cancellationToken);
            _logger.LogInformation(
                "Resource probe tap at ({X},{Y}) base=({BaseX},{BaseY}) confidence={Confidence:F3}",
                x, y, resourceTile.CenterX, resourceTile.CenterY, resourceTile.Confidence);

            await RandomDelayAsync(320, 680, cancellationToken);

            var popupState = await WaitForPopupStateAsync(context, TimeSpan.FromSeconds(2.8), cancellationToken);
            if (popupState == GameState.ResourcePopup)
            {
                _logger.LogInformation("Resource popup opened after probe tap.");
                return true;
            }

            if (popupState == GameState.TilePopup)
            {
                _logger.LogInformation("Tile popup opened after probe tap; dismissing and retrying next probe.");
                _ = await DismissTilePopupAsync(context, cancellationToken);
                await RandomDelayAsync(280, 520, cancellationToken);
            }
        }

        return false;
    }

    private async Task<GameState> WaitForPopupStateAsync(
        BotExecutionContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            var state = await ResolveStateWithFreshScreenshotAsync(context, cancellationToken);
            if (state == GameState.ResourcePopup || state == GameState.TilePopup)
            {
                return state;
            }

            await RandomDelayAsync(180, 420, cancellationToken);
        }

        return GameState.Unknown;
    }

    private async Task<bool> DismissTilePopupAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var close = await FindBestTemplateAsync(context, PopupCloseTemplates, ActionThresholds, cancellationToken);
        if (close.IsMatch)
        {
            await TapWithOffsetAsync(close.CenterX, close.CenterY, cancellationToken);
            _logger.LogInformation("Dismissed popup using close button.");
            await RandomDelayAsync(250, 550, cancellationToken);
            return true;
        }

        var tileButton = await FindBestTemplateAsync(context, TilePopupTemplates, ActionThresholds, cancellationToken);
        if (!tileButton.IsMatch)
        {
            _logger.LogWarning("Tile popup detected but no close/tile button template matched for dismissal.");
            return false;
        }

        var (width, height) = await _emulatorController.GetResolutionAsync(cancellationToken);
        var tapX = Math.Clamp(tileButton.CenterX - (int)(width * 0.24), 10, width - 10);
        var tapY = Math.Clamp(tileButton.CenterY - (int)(height * 0.18), 10, height - 10);

        await TapWithOffsetAsync(tapX, tapY, cancellationToken);
        _logger.LogInformation("Dismissed tile popup by tapping outside panel near ({X},{Y}).", tapX, tapY);
        await RandomDelayAsync(260, 620, cancellationToken);
        return true;
    }

    private async Task<bool> ExecuteWithRetryAsync(
        string stepName,
        Func<CancellationToken, Task<bool>> action,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stepCts.CancelAfter(TimeSpan.FromSeconds(StepTimeoutSeconds));

            try
            {
                var ok = await action(stepCts.Token);
                if (ok)
                {
                    return true;
                }

                _logger.LogWarning("Step failed: {StepName}, attempt {Attempt}/{MaxRetries}", stepName, attempt, maxRetries);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Step timeout: {StepName}, attempt {Attempt}/{MaxRetries}", stepName, attempt, maxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step error: {StepName}, attempt {Attempt}/{MaxRetries}", stepName, attempt, maxRetries);
            }

            await RandomDelayAsync(450, 950, cancellationToken);
        }

        _logger.LogError("Step exhausted retries: {StepName}", stepName);
        return false;
    }

    private async Task<BotExecutionContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        var deviceId = await _emulatorController.GetConnectedDeviceAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new InvalidOperationException("No active device found for resource gather task.");
        }

        var templateRoot = ResolveTemplateRoot();
        var screenshotRoot = Path.Combine(AppContext.BaseDirectory, "runtime", "screenshots");
        Directory.CreateDirectory(screenshotRoot);

        var screenshotPath = Path.Combine(screenshotRoot, $"screen-{Guid.NewGuid():N}.png");
        return new BotExecutionContext("default-account", deviceId, screenshotPath, templateRoot);
    }

    private async Task<BotExecutionContext> CaptureContextAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var screenshotRoot = Path.GetDirectoryName(context.ScreenshotPath) ?? Path.Combine(AppContext.BaseDirectory, "runtime", "screenshots");
        Directory.CreateDirectory(screenshotRoot);
        var screenshotPath = Path.Combine(screenshotRoot, $"screen-{Guid.NewGuid():N}.png");
        await _emulatorController.TakeScreenshotAsync(screenshotPath, cancellationToken);
        return context with { ScreenshotPath = screenshotPath };
    }

    private async Task TapWithOffsetAsync(int x, int y, CancellationToken cancellationToken)
    {
        var tapX = x + _random.Next(-5, 6);
        var tapY = y + _random.Next(-5, 6);
        await _emulatorController.TapAsync(tapX, tapY, cancellationToken);
    }

    private Task RandomDelayAsync(int minMs, int maxMs, CancellationToken cancellationToken)
    {
        return Task.Delay(_random.Next(minMs, maxMs + 1), cancellationToken);
    }

    private static string ResolveTemplateRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("BOT_TEMPLATE_ROOT"),
            Path.Combine(Directory.GetCurrentDirectory(), "Bot.Vision", "Templates"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Bot.Vision", "Templates"))
        };

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (Directory.Exists(candidate!))
            {
                return candidate!;
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "Bot.Vision", "Templates");
    }
}
