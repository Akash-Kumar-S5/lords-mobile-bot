using Bot.Core.Enums;
using Bot.Core.Interfaces;
using Bot.Core.Models;
using Bot.Emulator.Interfaces;
using Bot.Infrastructure.Configuration;
using Bot.Tasks.Interfaces;
using Bot.Tasks.Configuration;
using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Text;

namespace Bot.Tasks.Tasks;

public sealed class ResourceGatherTask : IBotTask
{
    private const string LowestTierXEnv = "BOT_LOWEST_TIER_X";
    private const string LowestTierYEnv = "BOT_LOWEST_TIER_Y";
    private const string DeployXEnv = "BOT_DEPLOY_X";
    private const string DeployYEnv = "BOT_DEPLOY_Y";

    private static readonly string[] GatherTemplates =
    {
        "gather_button.png"
    };

    private static readonly string[] LowestTierTemplates =
    {
        "lowest_tier_button.png"
    };

    private static readonly string[] ClearSelectionTemplates =
    {
        "clear_section_button.png"
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
    private static readonly double[] MarchAnchorThresholds = { 0.82, 0.74, 0.66, 0.58, 0.50 };
    private static readonly double[] ClearSelectionThresholds = { 0.78, 0.70, 0.62, 0.55 };
    private static readonly double[] ArmyIndicatorThresholds = { 0.90, 0.85, 0.80, 0.75, 0.70 };
    private static readonly double[] PopupCloseThresholds = { 0.90, 0.85, 0.80, 0.75 };

    private static readonly string[] ArmyIndicatorTemplates =
    {
        "army_indicator_icon.png"
    };
    private static readonly bool AlwaysSaveMarchDebug = string.Equals(
        Environment.GetEnvironmentVariable("BOT_SAVE_MARCH_DEBUG_ALWAYS"),
        "1",
        StringComparison.OrdinalIgnoreCase);
    private static readonly bool SaveClickDebug = !string.Equals(
        Environment.GetEnvironmentVariable("BOT_SAVE_CLICK_DEBUG"),
        "0",
        StringComparison.OrdinalIgnoreCase);

    private const int StepTimeoutSeconds = 45;
    private const double MinFrameMeanDiffAfterTap = 0.65;
    private const double MinFrameChangedPixelRatioAfterTap = 0.0025;
    private static readonly TimeSpan MarchSlotWaitTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan ArmyLimitRecheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StuckPopupDuration = TimeSpan.FromMinutes(1);
    private const double ArmyIndicatorMinConfidence = 0.70;
    private const double ClearSelectionMinConfidence = 0.55;
    private const double PopupCloseMinConfidence = 0.75;

    private readonly IEmulatorController _emulatorController;
    private readonly IStateResolver _stateResolver;
    private readonly IMapNavigator _mapNavigator;
    private readonly IImageDetector _imageDetector;
    private readonly IOcrReader _ocrReader;
    private readonly IRuntimeBotSettings _runtimeBotSettings;
    private readonly IBotModeController _modeController;
    private readonly IArmyLimitMonitorService _armyLimitMonitorService;
    private readonly ILogger<ResourceGatherTask> _logger;
    private readonly Random _random = Random.Shared;
    private MarchScreenDebugSnapshot? _lastMarchSnapshot;
    private GameState _lastResolvedState = GameState.Unknown;
    private DateTimeOffset _stateSinceUtc = DateTimeOffset.UtcNow;
    private bool _adbWarmedUp;

    private sealed record MarchScreenDebugSnapshot(
        DetectionResult DeployAnchor,
        DetectionResult BestClearCandidate,
        string TemplateName,
        double Threshold,
        int RoiX,
        int RoiY,
        int RoiW,
        int RoiH);

    public ResourceGatherTask(
        IEmulatorController emulatorController,
        IStateResolver stateResolver,
        IMapNavigator mapNavigator,
        IImageDetector imageDetector,
        IOcrReader ocrReader,
        IRuntimeBotSettings runtimeBotSettings,
        IBotModeController modeController,
        IArmyLimitMonitorService armyLimitMonitorService,
        ILogger<ResourceGatherTask> logger)
    {
        _emulatorController = emulatorController;
        _stateResolver = stateResolver;
        _mapNavigator = mapNavigator;
        _imageDetector = imageDetector;
        _ocrReader = ocrReader;
        _runtimeBotSettings = runtimeBotSettings;
        _modeController = modeController;
        _armyLimitMonitorService = armyLimitMonitorService;
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
        if (_modeController.CurrentMode != BotRunMode.Running)
        {
            return;
        }

        _logger.LogInformation("Resource gather task loop started.");
        if (!_adbWarmedUp)
        {
            await TryWarmupAdbAsync(cancellationToken);
            _adbWarmedUp = true;
        }
        _logger.LogInformation("Entering gather execution loop.");

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_modeController.CurrentMode != BotRunMode.Running)
            {
                return;
            }

            try
            {
                _logger.LogDebug("Loop tick: building execution context.");
                var context = await BuildContextAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
                _logger.LogDebug("Loop tick: resolving game state.");
                var state = await ResolveStateWithFreshScreenshotAsync(context, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
                _logger.LogInformation("Current state: {State}", state);
                TrackState(state);

                if (await TryForceCloseIfStuckOnPopupAsync(context, state, cancellationToken))
                {
                    await RandomDelayAsync(350, 850, cancellationToken);
                    continue;
                }

                if (await IsAtArmyLimitAsync(context, cancellationToken))
                {
                    _modeController.EnterArmyMonitor("Army limit reached");
                    _logger.LogInformation("Army limit reached. Switching to ArmyMonitor mode.");
                    return;
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
                        _logger.LogWarning("Unknown state encountered. Attempting to close active overlay.");
                        if (!await TryCloseActiveOverlayAsync(context, cancellationToken))
                        {
                            _logger.LogWarning("No close overlay action succeeded. Running map recovery.");
                        }
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

    private void TrackState(GameState currentState)
    {
        if (currentState == _lastResolvedState)
        {
            return;
        }

        _lastResolvedState = currentState;
        _stateSinceUtc = DateTimeOffset.UtcNow;
    }

    private async Task<bool> TryForceCloseIfStuckOnPopupAsync(
        BotExecutionContext context,
        GameState currentState,
        CancellationToken cancellationToken)
    {
        var popupState = currentState is GameState.TilePopup or GameState.ResourcePopup or GameState.MarchScreen;
        if (!popupState)
        {
            return false;
        }

        var stuckFor = DateTimeOffset.UtcNow - _stateSinceUtc;
        if (stuckFor < StuckPopupDuration)
        {
            return false;
        }

        _logger.LogWarning(
            "Detected stuck popup state {State} for {Seconds:F0}s. Forcing popup-close template click.",
            currentState,
            stuckFor.TotalSeconds);

        var close = await FindBestTemplateAsync(context, PopupCloseTemplates, ActionThresholds, cancellationToken);
        if (!close.IsMatch)
        {
            _logger.LogWarning("Stuck-popup recovery: popup_close template not found.");
            return false;
        }

        await TapWithOffsetAsync(context, close.CenterX, close.CenterY, "stuck-popup-close", cancellationToken);
        await RandomDelayAsync(280, 620, cancellationToken);
        _stateSinceUtc = DateTimeOffset.UtcNow;
        return true;
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

        DetectionResult resourceTile = DetectionResult.NotFound;
        const int maxResourceScanAttempts = 4;
        for (var scanAttempt = 1; scanAttempt <= maxResourceScanAttempts; scanAttempt++)
        {
            context = await CaptureContextAsync(context, cancellationToken);
            resourceTile = await _mapNavigator.FindResourceTileAsync(context, cancellationToken);
            if (resourceTile.IsMatch)
            {
                if (scanAttempt > 1)
                {
                    _logger.LogInformation(
                        "Resource found after map sweep attempt {Attempt}/{MaxAttempts}.",
                        scanAttempt,
                        maxResourceScanAttempts);
                }
                break;
            }

            _logger.LogWarning(
                "No resource tile detected on attempt {Attempt}/{MaxAttempts}, confidence={Confidence:F3}. Swiping map for more.",
                scanAttempt,
                maxResourceScanAttempts,
                resourceTile.Confidence);

            await _mapNavigator.RandomMapPanAsync(context, cancellationToken);
            await RandomDelayAsync(260, 700, cancellationToken);
        }

        if (!resourceTile.IsMatch)
        {
            _logger.LogWarning("No resource found after map sweep attempts. Continuing next loop tick.");
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

        await TapWithOffsetAsync(context, gather.CenterX, gather.CenterY, "gather-button", cancellationToken);
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
        var (screenWidth, screenHeight) = await _emulatorController.GetResolutionAsync(cancellationToken);
        var marchControls = GetMarchControlPoints(screenWidth, screenHeight);
        var deployAnchor = new DetectionResult(true, 1.0, marchControls.DeployX, marchControls.DeployY);
        _lastMarchSnapshot = null;
        var usingManualControls = IsManualMarchControlsEnabled();

        // Enforce sequence: click lowest tier first, verify clear selection, then deploy.
        await ClickLowestTierCandidateAsync(context, marchControls, cancellationToken);
        await RandomDelayAsync(420, 900, cancellationToken);
        if (AlwaysSaveMarchDebug)
        {
            await SaveMarchDebugFrameAsync(context, "after-lowest-tier-click", cancellationToken);
        }

        if (usingManualControls && !ManualClickPoints.VerifyClearSelectionBeforeDeploy)
        {
            _logger.LogInformation("Manual march controls active. Skipping clear-selection verification and proceeding to deploy.");
        }
        else if (!await IsClearSelectionActiveAsync(context, deployAnchor, screenWidth, screenHeight, cancellationToken))
        {
            _logger.LogWarning("Clear Selection was not detected after lowest-tier click. Retrying lowest-tier click once.");
            await ClickLowestTierCandidateAsync(context, marchControls, cancellationToken);
            await RandomDelayAsync(420, 900, cancellationToken);
            if (AlwaysSaveMarchDebug)
            {
                await SaveMarchDebugFrameAsync(context, "after-lowest-tier-retry", cancellationToken);
            }

            if (!await IsClearSelectionActiveAsync(context, deployAnchor, screenWidth, screenHeight, cancellationToken))
            {
                _logger.LogWarning("Clear Selection was not detected after lowest-tier click. Skipping deploy.");
                await SaveMarchDebugFrameAsync(context, "clear-selection-missing", cancellationToken);
                await TryCloseActiveOverlayAsync(context, cancellationToken);
                return false;
            }
        }

        await TapWithOffsetAsync(context, marchControls.DeployX, marchControls.DeployY, "deploy-static", cancellationToken);
        _logger.LogInformation("Clicked deploy button. Gather dispatched successfully.");
        await RandomDelayAsync(450, 1100, cancellationToken);
        return true;
    }

    private async Task<bool> WaitForMarchControlsAsync(
        BotExecutionContext context,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await WaitForStateAsync(GameState.MarchScreen, context, timeout, cancellationToken);
    }

    private async Task<bool> IsClearSelectionActiveAsync(
        BotExecutionContext context,
        DetectionResult deploy,
        int screenWidth,
        int screenHeight,
        CancellationToken cancellationToken)
    {
        var (rx, ry, rw, rh) = GetClearSelectionSearchRegion(deploy, screenWidth, screenHeight);
        var best = DetectionResult.NotFound;
        string selectedTemplate = "none";
        var bestCandidateAnyThreshold = DetectionResult.NotFound;
        string bestCandidateTemplate = "none";
        double bestCandidateThreshold = 0;

        foreach (var templateName in ClearSelectionTemplates)
        {
            var templatePath = Path.Combine(context.TemplateRoot, templateName);
            if (!File.Exists(templatePath))
            {
                continue;
            }

            foreach (var threshold in ClearSelectionThresholds)
            {
                context = await CaptureContextAsync(context, cancellationToken);
                var detection = await _imageDetector.FindTemplateInRegionAsync(
                    context.ScreenshotPath,
                    templatePath,
                    rx,
                    ry,
                    rw,
                    rh,
                    threshold,
                    cancellationToken);

                _logger.LogInformation(
                    "Clear-selection ROI detection template={Template} threshold={Threshold:F2} match={Match} confidence={Confidence:F3} center=({X},{Y}) roi=({RX},{RY},{RW},{RH})",
                    templateName,
                    threshold,
                    detection.IsMatch,
                    detection.Confidence,
                    detection.CenterX,
                    detection.CenterY,
                    rx,
                    ry,
                    rw,
                    rh);

                if (detection.Confidence > bestCandidateAnyThreshold.Confidence)
                {
                    bestCandidateAnyThreshold = detection;
                    bestCandidateTemplate = templateName;
                    bestCandidateThreshold = threshold;
                }

                if (!detection.IsMatch || detection.Confidence < ClearSelectionMinConfidence)
                {
                    continue;
                }

                if (!best.IsMatch || detection.Confidence > best.Confidence)
                {
                    best = detection;
                    selectedTemplate = templateName;
                }
            }
        }

        _lastMarchSnapshot = new MarchScreenDebugSnapshot(
            deploy,
            bestCandidateAnyThreshold,
            bestCandidateTemplate,
            bestCandidateThreshold,
            rx,
            ry,
            rw,
            rh);

        if (!best.IsMatch)
        {
            return false;
        }

        var dx = best.CenterX - deploy.CenterX;
        var dy = best.CenterY - deploy.CenterY;
        _logger.LogInformation(
            "Clear-selection candidate selected template={Template} confidence={Confidence:F3} dx={Dx} dy={Dy}",
            selectedTemplate,
            best.Confidence,
            dx,
            dy);

        if (!IsClearSelectionInExpectedZone(best, deploy, screenWidth, screenHeight))
        {
            _logger.LogWarning("Clear-selection candidate rejected by relative position gate.");
            return false;
        }

        context = await CaptureContextAsync(context, cancellationToken);
        var text = (await _ocrReader.ReadTextAsync(context.ScreenshotPath, rx, ry, rw, rh, cancellationToken)).ToLowerInvariant();
        _logger.LogInformation("Clear-selection OCR text='{Text}'", text);
        return text.Contains("clear", StringComparison.Ordinal);
    }

    private async Task ClickLowestTierCandidateAsync(
        BotExecutionContext context,
        MarchControlPoints controls,
        CancellationToken cancellationToken)
    {
        await TapWithOffsetAsync(context, controls.LowestTierX, controls.LowestTierY, "lowest-tier-static", cancellationToken);
        _logger.LogInformation(
            "Clicked lowest-tier static point at ({X},{Y}).",
            controls.LowestTierX,
            controls.LowestTierY);
    }

    private static MarchControlPoints GetMarchControlPoints(int screenWidth, int screenHeight)
    {
        var manual = TryGetManualMarchControlPoints();
        if (manual is not null)
        {
            return new MarchControlPoints(
                Math.Clamp(manual.Value.LowestTierX, 10, screenWidth - 10),
                Math.Clamp(manual.Value.LowestTierY, 10, screenHeight - 10),
                Math.Clamp(manual.Value.DeployX, 10, screenWidth - 10),
                Math.Clamp(manual.Value.DeployY, 10, screenHeight - 10));
        }

        // Static control layout on march screen (resolution-relative).
        var deployX = Math.Clamp((int)(screenWidth * 0.776), 10, screenWidth - 10);
        var deployY = Math.Clamp((int)(screenHeight * 0.806), 10, screenHeight - 10);
        var lowestTierX = Math.Clamp((int)(screenWidth * 0.786), 10, screenWidth - 10);
        var lowestTierY = Math.Clamp((int)(screenHeight * 0.656), 10, screenHeight - 10);
        return new MarchControlPoints(lowestTierX, lowestTierY, deployX, deployY);
    }

    private static MarchControlPoints? TryGetManualMarchControlPoints()
    {
        if (ManualClickPoints.UseManualCoordinates)
        {
            return new MarchControlPoints(
                ManualClickPoints.LowestTierX,
                ManualClickPoints.LowestTierY,
                ManualClickPoints.DeployX,
                ManualClickPoints.DeployY);
        }

        if (!TryGetIntFromEnv(LowestTierXEnv, out var ltX)
            || !TryGetIntFromEnv(LowestTierYEnv, out var ltY)
            || !TryGetIntFromEnv(DeployXEnv, out var dpX)
            || !TryGetIntFromEnv(DeployYEnv, out var dpY))
        {
            return null;
        }

        return new MarchControlPoints(ltX, ltY, dpX, dpY);
    }

    private static bool IsManualMarchControlsEnabled()
    {
        return ManualClickPoints.UseManualCoordinates
            || (TryGetIntFromEnv(LowestTierXEnv, out _)
                && TryGetIntFromEnv(LowestTierYEnv, out _)
                && TryGetIntFromEnv(DeployXEnv, out _)
                && TryGetIntFromEnv(DeployYEnv, out _));
    }

    private static bool TryGetIntFromEnv(string name, out int value)
    {
        value = 0;
        var raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out value);
    }

    private readonly record struct MarchControlPoints(int LowestTierX, int LowestTierY, int DeployX, int DeployY);

    private static bool IsClearSelectionInExpectedZone(
        DetectionResult clearSelection,
        DetectionResult deploy,
        int screenWidth,
        int screenHeight)
    {
        var dx = clearSelection.CenterX - deploy.CenterX;
        var dy = clearSelection.CenterY - deploy.CenterY;

        var minDx = (int)(-0.12 * screenWidth);
        var maxDx = (int)(0.16 * screenWidth);
        var minDy = (int)(-0.42 * screenHeight);
        var maxDy = (int)(-0.10 * screenHeight);

        return dx >= minDx && dx <= maxDx && dy >= minDy && dy <= maxDy;
    }

    private static (int X, int Y, int W, int H) GetClearSelectionSearchRegion(
        DetectionResult deploy,
        int screenWidth,
        int screenHeight)
    {
        // Clear Selection sits just above deploy on the right panel.
        // Keep ROI anchored relative to deploy to stay resolution-independent.
        var x = Math.Clamp(deploy.CenterX - (int)(screenWidth * 0.12), 0, screenWidth - 1);
        var y = Math.Clamp(deploy.CenterY - (int)(screenHeight * 0.28), 0, screenHeight - 1);
        var w = Math.Clamp((int)(screenWidth * 0.26), 60, screenWidth - x);
        var h = Math.Clamp((int)(screenHeight * 0.26), 60, screenHeight - y);
        return (x, y, w, h);
    }

    private async Task SaveMarchDebugFrameAsync(
        BotExecutionContext context,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            context = await CaptureContextAsync(context, cancellationToken);
            var fileName = $"march-{reason}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png";
            var outputPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "runtime", "debug", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "logs", "debug", fileName)
            };

            foreach (var outputPath in outputPaths)
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            using var screenshot = Cv2.ImRead(context.ScreenshotPath, ImreadModes.Color);
            if (screenshot.Empty())
            {
                foreach (var outputPath in outputPaths)
                {
                    File.Copy(context.ScreenshotPath, outputPath, overwrite: true);
                    _logger.LogWarning("Saved march debug frame (unannotated): {Path}", outputPath);
                }
                return;
            }

            foreach (var outputPath in outputPaths)
            {
                Cv2.ImWrite(outputPath, screenshot);
                _logger.LogWarning("Saved march debug frame: {Path}", outputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to save march debug frame.");
        }
    }

    private async Task<bool> IsAtArmyLimitAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _armyLimitMonitorService.CheckNowAsync(cancellationToken);
        if (!result.IsReadable)
        {
            _logger.LogInformation("Army monitor check unreadable in task tick: {Reason}", result.Reason);
            return false;
        }

        _logger.LogInformation(
            "Army monitor check in task tick: marches={Marches} limit={Limit} atOrAbove={AtOrAbove}",
            result.DetectedMarches,
            result.ConfiguredLimit,
            result.AtOrAboveLimit);

        return result.AtOrAboveLimit;
    }

    private async Task<int?> DetectActiveMarchCountAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        context = await CaptureContextAsync(context, cancellationToken);
        var (screenWidth, screenHeight) = await _emulatorController.GetResolutionAsync(cancellationToken);

        if (TryGetManualArmyIndicatorGateBounds(screenWidth, screenHeight, out var gateRect))
        {
            var gateTemplatePath = Path.Combine(context.TemplateRoot, "army_indicator_icon.png");
            if (File.Exists(gateTemplatePath))
            {
                var gateDetection = await _imageDetector.FindTemplateInRegionAsync(
                    context.ScreenshotPath,
                    gateTemplatePath,
                    gateRect.X,
                    gateRect.Y,
                    gateRect.W,
                    gateRect.H,
                    0.0,
                    cancellationToken);

                await SaveArmyOcrDebugAsync(
                    context,
                    "indicator-gate",
                    gateRect.X,
                    gateRect.Y,
                    gateRect.W,
                    gateRect.H,
                    gateDetection,
                    null,
                    cancellationToken);

                _logger.LogInformation(
                    "Army indicator gate detection confidence={Confidence:F3} center=({X},{Y}) roi=({RX},{RY},{RW},{RH}) required>={Required:F3}",
                    gateDetection.Confidence,
                    gateDetection.CenterX,
                    gateDetection.CenterY,
                    gateRect.X,
                    gateRect.Y,
                    gateRect.W,
                    gateRect.H,
                    ManualClickPoints.ArmyIndicatorGateMinConfidence);

                if (gateDetection.Confidence < ManualClickPoints.ArmyIndicatorGateMinConfidence)
                {
                    _logger.LogInformation("Army indicator gate failed. Skipping OCR for this cycle.");
                    return null;
                }
            }
        }

        if (TryGetManualArmyOcrBounds(screenWidth, screenHeight, out var manualRect))
        {
            var manualValue = await _ocrReader.ReadIntegerAsync(
                context.ScreenshotPath,
                manualRect.X,
                manualRect.Y,
                manualRect.W,
                manualRect.H,
                cancellationToken);

            await SaveArmyOcrDebugAsync(
                context,
                "manual-region",
                manualRect.X,
                manualRect.Y,
                manualRect.W,
                manualRect.H,
                null,
                manualValue,
                cancellationToken);

            _logger.LogInformation(
                "Manual army OCR region used roi=({X},{Y},{W},{H}) value={Value}",
                manualRect.X,
                manualRect.Y,
                manualRect.W,
                manualRect.H,
                manualValue.HasValue ? manualValue.Value.ToString() : "null");

            if (manualValue is >= 0 and <= 9)
            {
                return manualValue;
            }
        }

        var indicator = await FindBestTemplateAsync(context, ArmyIndicatorTemplates, ArmyIndicatorThresholds, cancellationToken);
        if (!indicator.IsMatch)
        {
            _logger.LogDebug("Army indicator icon not found for OCR.");
            return null;
        }

        var inExpectedZone = indicator.CenterX <= (int)(screenWidth * 0.22)
            && indicator.CenterY >= (int)(screenHeight * 0.20)
            && indicator.CenterY <= (int)(screenHeight * 0.92);

        if (indicator.Confidence < ArmyIndicatorMinConfidence || !inExpectedZone)
        {
            _logger.LogInformation(
                "Rejected army indicator anchor: confidence={Confidence:F3}, center=({X},{Y}), zoneOk={ZoneOk}.",
                indicator.Confidence,
                indicator.CenterX,
                indicator.CenterY,
                inExpectedZone);
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
            await SaveArmyOcrDebugAsync(
                context,
                "anchor-probe",
                x,
                y,
                w,
                h,
                indicator,
                value,
                cancellationToken);
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

    private async Task SaveArmyOcrDebugAsync(
        BotExecutionContext context,
        string reason,
        int x,
        int y,
        int width,
        int height,
        DetectionResult? anchor,
        int? parsedValue,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            var baseName = $"army-{reason}-{stamp}";
            var roots = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "logs", "debug", "army-ocr"),
                Path.Combine(AppContext.BaseDirectory, "runtime", "debug", "army-ocr")
            };

            foreach (var root in roots)
            {
                Directory.CreateDirectory(Path.Combine(root, "full"));
                Directory.CreateDirectory(Path.Combine(root, "crop"));
                Directory.CreateDirectory(Path.Combine(root, "meta"));
            }

            using var screenshot = Cv2.ImRead(context.ScreenshotPath, ImreadModes.Color);
            if (screenshot.Empty())
            {
                return;
            }

            var rx = Math.Clamp(x, 0, screenshot.Width - 1);
            var ry = Math.Clamp(y, 0, screenshot.Height - 1);
            var rw = Math.Clamp(width, 1, screenshot.Width - rx);
            var rh = Math.Clamp(height, 1, screenshot.Height - ry);
            var roi = new Rect(rx, ry, rw, rh);

            using var full = screenshot.Clone();

            using var crop = new Mat(screenshot, roi);
            var meta = BuildArmyMeta(
                context.ScreenshotPath,
                reason,
                rx,
                ry,
                rw,
                rh,
                anchor,
                parsedValue);

            foreach (var root in roots)
            {
                Cv2.ImWrite(Path.Combine(root, "full", $"{baseName}.png"), full);
                Cv2.ImWrite(Path.Combine(root, "crop", $"{baseName}.png"), crop);
                await File.WriteAllTextAsync(
                    Path.Combine(root, "meta", $"{baseName}.txt"),
                    meta,
                    Encoding.UTF8,
                    cancellationToken);
            }

            _logger.LogDebug(
                "Army OCR debug saved: reason={Reason} roi=({X},{Y},{W},{H}) value={Value}",
                reason,
                rx,
                ry,
                rw,
                rh,
                parsedValue.HasValue ? parsedValue.Value.ToString() : "null");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to save army OCR debug artifacts.");
        }
    }

    private static string BuildArmyMeta(
        string screenshotPath,
        string reason,
        int x,
        int y,
        int width,
        int height,
        DetectionResult? anchor,
        int? parsedValue)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"timestamp_utc={DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"reason={reason}");
        sb.AppendLine($"screenshot={screenshotPath}");
        sb.AppendLine($"roi_x={x}");
        sb.AppendLine($"roi_y={y}");
        sb.AppendLine($"roi_w={width}");
        sb.AppendLine($"roi_h={height}");
        sb.AppendLine($"ocr_value={(parsedValue.HasValue ? parsedValue.Value.ToString() : "null")}");
        if (anchor is not null)
        {
            sb.AppendLine($"anchor_confidence={anchor.Confidence:F3}");
            sb.AppendLine($"anchor_x={anchor.CenterX}");
            sb.AppendLine($"anchor_y={anchor.CenterY}");
        }
        return sb.ToString();
    }

    private static bool TryGetManualArmyOcrBounds(int screenWidth, int screenHeight, out (int X, int Y, int W, int H) rect)
    {
        rect = default;
        if (!ManualClickPoints.UseManualArmyOcrRegion)
        {
            return false;
        }

        var xs = new[]
        {
            ManualClickPoints.ArmyOcrX1,
            ManualClickPoints.ArmyOcrX2,
            ManualClickPoints.ArmyOcrX3,
            ManualClickPoints.ArmyOcrX4
        };
        var ys = new[]
        {
            ManualClickPoints.ArmyOcrY1,
            ManualClickPoints.ArmyOcrY2,
            ManualClickPoints.ArmyOcrY3,
            ManualClickPoints.ArmyOcrY4
        };

        if (xs.Any(v => v <= 0) || ys.Any(v => v <= 0))
        {
            return false;
        }

        var minX = Math.Clamp(xs.Min(), 0, screenWidth - 1);
        var maxX = Math.Clamp(xs.Max(), 0, screenWidth - 1);
        var minY = Math.Clamp(ys.Min(), 0, screenHeight - 1);
        var maxY = Math.Clamp(ys.Max(), 0, screenHeight - 1);

        var width = maxX - minX;
        var height = maxY - minY;
        if (width < 8 || height < 8)
        {
            return false;
        }

        rect = (minX, minY, width, height);
        return true;
    }

    private static bool TryGetManualArmyIndicatorGateBounds(int screenWidth, int screenHeight, out (int X, int Y, int W, int H) rect)
    {
        rect = default;
        if (!ManualClickPoints.UseManualArmyIndicatorGateRegion)
        {
            return false;
        }

        var xs = new[]
        {
            ManualClickPoints.ArmyIndicatorGateX1,
            ManualClickPoints.ArmyIndicatorGateX2,
            ManualClickPoints.ArmyIndicatorGateX3,
            ManualClickPoints.ArmyIndicatorGateX4
        };
        var ys = new[]
        {
            ManualClickPoints.ArmyIndicatorGateY1,
            ManualClickPoints.ArmyIndicatorGateY2,
            ManualClickPoints.ArmyIndicatorGateY3,
            ManualClickPoints.ArmyIndicatorGateY4
        };

        var minX = Math.Clamp(xs.Min(), 0, screenWidth - 1);
        var maxX = Math.Clamp(xs.Max(), 0, screenWidth - 1);
        var minY = Math.Clamp(ys.Min(), 0, screenHeight - 1);
        var maxY = Math.Clamp(ys.Max(), 0, screenHeight - 1);

        var width = maxX - minX;
        var height = maxY - minY;
        if (width < 8 || height < 8)
        {
            return false;
        }

        rect = (minX, minY, width, height);
        return true;
    }

    private async Task WaitUntilMarchSlotFreeAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        _logger.LogInformation("Waiting for a march slot to become free.");

        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow - started < MarchSlotWaitTimeout)
        {
            var check = await _armyLimitMonitorService.CheckNowAsync(cancellationToken);
            _logger.LogInformation(
                "March wait check: readable={Readable} marches={Marches} limit={Limit} atOrAbove={AtOrAbove} reason={Reason}",
                check.IsReadable,
                check.DetectedMarches,
                check.ConfiguredLimit,
                check.AtOrAboveLimit,
                check.Reason);

            if (!check.IsReadable)
            {
                _logger.LogWarning("March wait: precheck/ocr unreadable, retrying.");
            }
            else if (!check.AtOrAboveLimit)
            {
                _logger.LogInformation("March wait: exiting wait (below limit).");
                await RecoverToWorldMapAsync(context, cancellationToken);
                return;
            }
            else
            {
                _logger.LogInformation("March wait: continuing wait (full).");
            }

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

            var beforeTap = await CaptureContextAsync(context, cancellationToken);
            await TapWithOffsetAsync(beforeTap, x, y, "resource-probe", cancellationToken);
            _logger.LogInformation(
                "Resource probe tap at ({X},{Y}) base=({BaseX},{BaseY}) confidence={Confidence:F3}",
                x, y, resourceTile.CenterX, resourceTile.CenterY, resourceTile.Confidence);

            await RandomDelayAsync(320, 680, cancellationToken);
            var afterTap = await CaptureContextAsync(beforeTap, cancellationToken);
            if (!DidScreenChangeAfterInteraction(beforeTap.ScreenshotPath, afterTap.ScreenshotPath))
            {
                _logger.LogInformation(
                    "Resource probe produced no significant screen change; skipping popup checks for this probe.");
                continue;
            }

            var popupState = await WaitForPopupStateAsync(afterTap, TimeSpan.FromSeconds(2.8), cancellationToken);
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

    private bool DidScreenChangeAfterInteraction(string beforeScreenshotPath, string afterScreenshotPath)
    {
        try
        {
            using var before = Cv2.ImRead(beforeScreenshotPath, ImreadModes.Grayscale);
            using var after = Cv2.ImRead(afterScreenshotPath, ImreadModes.Grayscale);
            if (before.Empty() || after.Empty())
            {
                return true;
            }

            var width = Math.Min(before.Width, after.Width);
            var height = Math.Min(before.Height, after.Height);
            if (width <= 2 || height <= 2)
            {
                return true;
            }

            using var beforeRoi = new Mat(before, new Rect(0, 0, width, height));
            using var afterRoi = new Mat(after, new Rect(0, 0, width, height));
            using var diff = new Mat();
            Cv2.Absdiff(beforeRoi, afterRoi, diff);

            var meanDiff = Cv2.Mean(diff).Val0;

            using var threshold = new Mat();
            Cv2.Threshold(diff, threshold, 12, 255, ThresholdTypes.Binary);
            var changedPixelCount = Cv2.CountNonZero(threshold);
            var changedRatio = changedPixelCount / (double)(width * height);

            var changed = meanDiff >= MinFrameMeanDiffAfterTap || changedRatio >= MinFrameChangedPixelRatioAfterTap;
            _logger.LogInformation(
                "Frame delta after interaction: changed={Changed} meanDiff={MeanDiff:F3} changedRatio={ChangedRatio:P3}",
                changed,
                meanDiff,
                changedRatio);
            return changed;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Frame-delta comparison failed; allowing popup checks to proceed.");
            return true;
        }
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
        var tileButton = await FindBestTemplateAsync(context, TilePopupTemplates, ActionThresholds, cancellationToken);
        if (!tileButton.IsMatch)
        {
            _logger.LogWarning("Tile popup recovery requested but occupy/transfer buttons were not detected.");
            return false;
        }

        // Primary: swipe outside popup to dismiss without tapping other tiles.
        var (width, height) = await _emulatorController.GetResolutionAsync(cancellationToken);
        var swipeStartX = Math.Clamp((int)(width * 0.86), 10, width - 10);
        var swipeStartY = Math.Clamp((int)(height * 0.74), 10, height - 10);
        var swipeEndX = Math.Clamp((int)(width * 0.60), 10, width - 10);
        var swipeEndY = Math.Clamp((int)(height * 0.70), 10, height - 10);

        await _emulatorController.SwipeAsync(swipeStartX, swipeStartY, swipeEndX, swipeEndY, 220, cancellationToken);
        _logger.LogInformation(
            "Attempted tile popup dismissal via outside swipe: ({StartX},{StartY}) -> ({EndX},{EndY}).",
            swipeStartX,
            swipeStartY,
            swipeEndX,
            swipeEndY);
        await RandomDelayAsync(280, 620, cancellationToken);

        var stateAfterSwipe = await ResolveStateWithFreshScreenshotAsync(context, cancellationToken);
        if (stateAfterSwipe != GameState.TilePopup)
        {
            _logger.LogInformation("Tile popup dismissed by outside swipe.");
            return true;
        }

        // Fallback: explicit close button.
        var close = await TryFindPopupCloseAsync(context, cancellationToken);
        if (close is not null)
        {
            await TapWithOffsetAsync(context, close.CenterX, close.CenterY, "popup-close", cancellationToken);
            _logger.LogInformation("Dismissed popup using close button.");
            await RandomDelayAsync(250, 550, cancellationToken);
            return true;
        }

        _logger.LogWarning("Tile popup detected but close button was not confidently found. Skipping tap.");
        return false;
    }

    private async Task<bool> TryCloseActiveOverlayAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var close = await TryFindPopupCloseAsync(context, cancellationToken);
        if (close is not null)
        {
            await TapWithOffsetAsync(context, close.CenterX, close.CenterY, "overlay-close", cancellationToken);
            _logger.LogInformation("Closed active overlay using close button.");
            await RandomDelayAsync(280, 620, cancellationToken);
            return true;
        }

        _logger.LogInformation("Overlay close button not confidently detected; skipping close tap.");
        return false;
    }

    private async Task<DetectionResult?> TryFindPopupCloseAsync(
        BotExecutionContext context,
        CancellationToken cancellationToken)
    {
        var candidate = await FindBestTemplateAsync(context, PopupCloseTemplates, PopupCloseThresholds, cancellationToken);
        if (!candidate.IsMatch || candidate.Confidence < PopupCloseMinConfidence)
        {
            return null;
        }

        var (width, height) = await _emulatorController.GetResolutionAsync(cancellationToken);
        var inTopRightZone = candidate.CenterX >= (int)(width * 0.80)
            && candidate.CenterY <= (int)(height * 0.25);

        if (!inTopRightZone)
        {
            _logger.LogWarning(
                "Rejected popup-close candidate outside top-right zone: confidence={Confidence:F3} center=({X},{Y})",
                candidate.Confidence,
                candidate.CenterX,
                candidate.CenterY);
            return null;
        }

        return candidate;
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

    private async Task TapWithOffsetAsync(
        BotExecutionContext context,
        int x,
        int y,
        string reason,
        CancellationToken cancellationToken)
    {
        var tapX = x + _random.Next(-5, 6);
        var tapY = y + _random.Next(-5, 6);
        await SaveClickDebugFrameAsync(context, reason, tapX, tapY, cancellationToken);
        await _emulatorController.TapAsync(tapX, tapY, cancellationToken);
    }

    private async Task SaveClickDebugFrameAsync(
        BotExecutionContext context,
        string reason,
        int tapX,
        int tapY,
        CancellationToken cancellationToken)
    {
        if (!SaveClickDebug)
        {
            return;
        }

        try
        {
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "runtime", "screenshots");
            Directory.CreateDirectory(tempRoot);
            var sourcePath = Path.Combine(tempRoot, $"click-source-{Guid.NewGuid():N}.png");
            await _emulatorController.TakeScreenshotAsync(sourcePath, cancellationToken);

            using var screenshot = Cv2.ImRead(sourcePath, ImreadModes.Color);
            if (screenshot.Empty())
            {
                return;
            }

            Cv2.Circle(screenshot, new Point(tapX, tapY), 12, new Scalar(0, 0, 255), 3);
            Cv2.Line(screenshot, new Point(tapX - 18, tapY), new Point(tapX + 18, tapY), new Scalar(0, 0, 255), 2);
            Cv2.Line(screenshot, new Point(tapX, tapY - 18), new Point(tapX, tapY + 18), new Scalar(0, 0, 255), 2);
            Cv2.PutText(
                screenshot,
                $"tap:{reason} ({tapX},{tapY})",
                new Point(Math.Max(10, tapX - 120), Math.Max(28, tapY - 20)),
                HersheyFonts.HersheySimplex,
                0.55,
                new Scalar(0, 255, 255),
                2);

            var fileName = $"click-{reason}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.png";
            var outputPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "runtime", "debug", "clicks", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "logs", "debug", "clicks", fileName)
            };

            foreach (var outputPath in outputPaths)
            {
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Cv2.ImWrite(outputPath, screenshot);
            }

            _logger.LogInformation("Saved click debug frame: reason={Reason} tap=({X},{Y})", reason, tapX, tapY);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to save click debug frame for reason={Reason}.", reason);
        }
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
