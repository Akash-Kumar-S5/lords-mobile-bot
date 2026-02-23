using Bot.Core.Enums;
using Bot.Core.Interfaces;
using Bot.Core.Models;
using Bot.Core.Configuration;
using Bot.Emulator.Interfaces;
using Bot.Infrastructure.Configuration;
using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Bot.Core.Services;

public sealed class ArmyLimitMonitorService : IArmyLimitMonitorService
{
    private static readonly double[] PopupCloseThresholds = { 0.90, 0.85, 0.80, 0.75 };
    private static readonly double[] ArmyIndicatorThresholds = { 0.90, 0.85, 0.80, 0.75, 0.70 };
    private const double PopupCloseMinConfidence = 0.75;
    private const int MaxCloseAttempts = 3;
    private static readonly bool SaveArmyMonitorDebugAlways = string.Equals(
        Environment.GetEnvironmentVariable("BOT_SAVE_ARMY_MONITOR_DEBUG_ALWAYS"),
        "1",
        StringComparison.OrdinalIgnoreCase);

    private readonly IEmulatorController _emulator;
    private readonly IStateResolver _stateResolver;
    private readonly IImageDetector _imageDetector;
    private readonly IOcrReader _ocrReader;
    private readonly IRuntimeBotSettings _runtimeSettings;
    private readonly ILogger<ArmyLimitMonitorService> _logger;

    public ArmyLimitMonitorService(
        IEmulatorController emulator,
        IStateResolver stateResolver,
        IImageDetector imageDetector,
        IOcrReader ocrReader,
        IRuntimeBotSettings runtimeSettings,
        ILogger<ArmyLimitMonitorService> logger)
    {
        _emulator = emulator;
        _stateResolver = stateResolver;
        _imageDetector = imageDetector;
        _ocrReader = ocrReader;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    public async Task<MonitorPrecheckResult> EnsureWorldMapReadyAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Army monitor precheck started.");
        var closeAttempts = 0;

        for (var attempt = 1; attempt <= MaxCloseAttempts; attempt++)
        {
            var context = await BuildContextAsync(cancellationToken);
            context = await CaptureContextAsync(context, cancellationToken);
            var state = await _stateResolver.ResolveAsync(context, cancellationToken);

            if (state == GameState.WorldMap)
            {
                _logger.LogInformation("Army monitor precheck ready: already on world map.");
                return new MonitorPrecheckResult(true, closeAttempts, true, "World map confirmed");
            }

            var closed = await TryClosePopupAsync(context, cancellationToken);
            if (closed)
            {
                closeAttempts++;
                await Task.Delay(250, cancellationToken);
            }

            context = await CaptureContextAsync(context, cancellationToken);
            var onMap = await _stateResolver.IsWorldMapAsync(context, cancellationToken);
            if (!onMap)
            {
                if (await TryClickMapButtonAsync(context, cancellationToken))
                {
                    await Task.Delay(300, cancellationToken);
                    context = await CaptureContextAsync(context, cancellationToken);
                    onMap = await _stateResolver.IsWorldMapAsync(context, cancellationToken);
                }
            }

            if (onMap)
            {
                _logger.LogInformation(
                    "Army monitor precheck ready after attempt {Attempt}/{MaxAttempts}.",
                    attempt,
                    MaxCloseAttempts);
                return new MonitorPrecheckResult(true, closeAttempts, true, "World map ready");
            }
        }

        _logger.LogWarning("Army monitor precheck failed: map not ready after retries.");
        return new MonitorPrecheckResult(false, closeAttempts, false, "Map not ready");
    }

    public async Task<ArmyLimitCheckResult> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        var limit = Math.Max(1, _runtimeSettings.MaxActiveMarches);
        var precheck = await EnsureWorldMapReadyAsync(cancellationToken);
        if (!precheck.IsReady)
        {
            return new ArmyLimitCheckResult(false, null, limit, false, "Precheck failed");
        }

        var context = await BuildContextAsync(cancellationToken);
        context = await CaptureContextAsync(context, cancellationToken);

        var indicatorTemplate = Path.Combine(context.TemplateRoot, "army_indicator_icon.png");
        if (!File.Exists(indicatorTemplate))
        {
            _logger.LogWarning("Army monitor check failed: indicator template missing.");
            return new ArmyLimitCheckResult(false, null, limit, false, "Indicator template missing");
        }

        var indicator = await FindArmyIndicatorAsync(context, cancellationToken);

        if (!indicator.IsMatch)
        {
            if (SaveArmyMonitorDebugAlways)
            {
                await SaveArmyMonitorDebugAsync(
                    context,
                    "indicator-not-found",
                    null,
                    null,
                    null,
                    null,
                    cancellationToken);
            }

            _logger.LogInformation(
                "Army monitor check: indicator not detected on world map, treating active marches as 0 (limit={Limit}).",
                limit);
            return new ArmyLimitCheckResult(true, 0, limit, false, "Indicator not found (assumed zero)");
        }

        var (width, height) = await _emulator.GetResolutionAsync(cancellationToken);
        var inExpectedZone = indicator.CenterX <= (int)(width * 0.22)
            && indicator.CenterY >= (int)(height * 0.20)
            && indicator.CenterY <= (int)(height * 0.92);
        if (!inExpectedZone)
        {
            _logger.LogInformation(
                "Army monitor check unreadable: indicator outside expected zone center=({X},{Y}).",
                indicator.CenterX,
                indicator.CenterY);
            return new ArmyLimitCheckResult(false, null, limit, false, "Indicator outside zone");
        }

        if (TryGetManualArmyOcrBounds(width, height, out var manualRect))
        {
            var manualValue = await _ocrReader.ReadIntegerAsync(
                context.ScreenshotPath,
                manualRect.X,
                manualRect.Y,
                manualRect.W,
                manualRect.H,
                cancellationToken);
            if (manualValue is >= 0 and <= 9)
            {
                var atOrAbove = manualValue.Value >= limit;
                if (SaveArmyMonitorDebugAlways)
                {
                    await SaveArmyMonitorDebugAsync(
                        context,
                        "manual-ocr-success",
                        TryGetManualArmyIndicatorGateBounds(width, height, out var gateRectForSuccess) ? gateRectForSuccess : null,
                        indicator,
                        manualRect,
                        manualValue,
                        cancellationToken);
                }

                _logger.LogInformation(
                    "Army monitor check readable (Manual OCR): marches={ActiveMarches} limit={Limit} atOrAbove={AtOrAbove} roi=({X},{Y},{W},{H}).",
                    manualValue.Value,
                    limit,
                    atOrAbove,
                    manualRect.X,
                    manualRect.Y,
                    manualRect.W,
                    manualRect.H);
                return new ArmyLimitCheckResult(true, manualValue.Value, limit, atOrAbove, "Manual OCR");
            }

            _logger.LogWarning(
                "Army monitor check unreadable: manual OCR parse failed roi=({X},{Y},{W},{H}).",
                manualRect.X,
                manualRect.Y,
                manualRect.W,
                manualRect.H);
            await SaveArmyMonitorDebugAsync(
                context,
                "manual-ocr-parse-failed",
                TryGetManualArmyIndicatorGateBounds(width, height, out var gateRectForFail) ? gateRectForFail : null,
                indicator,
                manualRect,
                manualValue,
                cancellationToken);
            return new ArmyLimitCheckResult(false, null, limit, false, "Manual OCR parse failed");
        }

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
            if (value is >= 0 and <= 9)
            {
                var atOrAbove = value.Value >= limit;
                if (SaveArmyMonitorDebugAlways)
                {
                    var dynamicRect = (x, y, w, h);
                    await SaveArmyMonitorDebugAsync(
                        context,
                        "dynamic-ocr-success",
                        TryGetManualArmyIndicatorGateBounds(width, height, out var gateRectForDynamicSuccess) ? gateRectForDynamicSuccess : null,
                        indicator,
                        dynamicRect,
                        value,
                        cancellationToken);
                }

                _logger.LogInformation(
                    "Army monitor check readable (Dynamic ROI): marches={ActiveMarches} limit={Limit} atOrAbove={AtOrAbove}.",
                    value.Value,
                    limit,
                    atOrAbove);
                return new ArmyLimitCheckResult(true, value.Value, limit, atOrAbove, "Dynamic ROI");
            }
        }

        _logger.LogInformation("Army monitor check unreadable: OCR parse failed.");
        await SaveArmyMonitorDebugAsync(
            context,
            "dynamic-ocr-parse-failed",
            TryGetManualArmyIndicatorGateBounds(width, height, out var gateRectForDynamicFail) ? gateRectForDynamicFail : null,
            indicator,
            null,
            null,
            cancellationToken);
        return new ArmyLimitCheckResult(false, null, limit, false, "OCR parse failed");
    }

    private async Task<DetectionResult> FindArmyIndicatorAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var (screenWidth, screenHeight) = await _emulator.GetResolutionAsync(cancellationToken);

        if (TryGetManualArmyIndicatorGateBounds(screenWidth, screenHeight, out var gateRect))
        {
            var templatePath = Path.Combine(context.TemplateRoot, "army_indicator_icon.png");
            if (!File.Exists(templatePath))
            {
                return DetectionResult.NotFound;
            }

            var detection = await _imageDetector.FindTemplateInRegionAsync(
                context.ScreenshotPath,
                templatePath,
                gateRect.X,
                gateRect.Y,
                gateRect.W,
                gateRect.H,
                0.0,
                cancellationToken);

            _logger.LogInformation(
                "Army indicator gate detection confidence={Confidence:F3} center=({X},{Y}) roi=({RX},{RY},{RW},{RH}) required>={Required:F3}.",
                detection.Confidence,
                detection.CenterX,
                detection.CenterY,
                gateRect.X,
                gateRect.Y,
                gateRect.W,
                gateRect.H,
                ManualDetectionSettings.ArmyIndicatorGateMinConfidence);

            if (detection.Confidence >= ManualDetectionSettings.ArmyIndicatorGateMinConfidence)
            {
                return detection with { IsMatch = true };
            }

            return DetectionResult.NotFound;
        }

        return await FindBestTemplateAsync(
            context,
            new[] { "army_indicator_icon.png" },
            ArmyIndicatorThresholds,
            cancellationToken);
    }

    private static bool TryGetManualArmyOcrBounds(int screenWidth, int screenHeight, out (int X, int Y, int W, int H) rect)
    {
        rect = default;
        if (!ManualDetectionSettings.UseManualArmyOcrRegion)
        {
            return false;
        }

        var xs = new[]
        {
            ManualDetectionSettings.ArmyOcrX1,
            ManualDetectionSettings.ArmyOcrX2,
            ManualDetectionSettings.ArmyOcrX3,
            ManualDetectionSettings.ArmyOcrX4
        };
        var ys = new[]
        {
            ManualDetectionSettings.ArmyOcrY1,
            ManualDetectionSettings.ArmyOcrY2,
            ManualDetectionSettings.ArmyOcrY3,
            ManualDetectionSettings.ArmyOcrY4
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
        if (!ManualDetectionSettings.UseManualArmyIndicatorGateRegion)
        {
            return false;
        }

        var xs = new[]
        {
            ManualDetectionSettings.ArmyIndicatorGateX1,
            ManualDetectionSettings.ArmyIndicatorGateX2,
            ManualDetectionSettings.ArmyIndicatorGateX3,
            ManualDetectionSettings.ArmyIndicatorGateX4
        };
        var ys = new[]
        {
            ManualDetectionSettings.ArmyIndicatorGateY1,
            ManualDetectionSettings.ArmyIndicatorGateY2,
            ManualDetectionSettings.ArmyIndicatorGateY3,
            ManualDetectionSettings.ArmyIndicatorGateY4
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

    private async Task SaveArmyMonitorDebugAsync(
        BotExecutionContext context,
        string reason,
        (int X, int Y, int W, int H)? gateRect,
        DetectionResult? indicator,
        (int X, int Y, int W, int H)? ocrRect,
        int? ocrValue,
        CancellationToken cancellationToken)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            var token = $"{timestamp:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}".ToLowerInvariant();

            var outputRoots = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "runtime", "debug", "army-monitor"),
                Path.Combine(Directory.GetCurrentDirectory(), "logs", "debug", "army-monitor")
            };

            foreach (var root in outputRoots)
            {
                Directory.CreateDirectory(Path.Combine(root, "annotated"));
                Directory.CreateDirectory(Path.Combine(root, "crops"));
                Directory.CreateDirectory(Path.Combine(root, "meta"));
            }

            using var source = Cv2.ImRead(context.ScreenshotPath, ImreadModes.Color);
            if (source.Empty())
            {
                return;
            }

            using var annotated = source.Clone();

            if (gateRect is { } g)
            {
                Cv2.Rectangle(annotated, new Rect(g.X, g.Y, g.W, g.H), new Scalar(0, 200, 255), 2);
                Cv2.PutText(
                    annotated,
                    "Gate ROI",
                    new Point(Math.Max(4, g.X), Math.Max(22, g.Y - 6)),
                    HersheyFonts.HersheySimplex,
                    0.55,
                    new Scalar(0, 200, 255),
                    2);
            }

            if (indicator is { } ind && ind.IsMatch)
            {
                Cv2.Circle(annotated, new Point(ind.CenterX, ind.CenterY), 10, new Scalar(0, 0, 255), 3);
                Cv2.Line(annotated, new Point(ind.CenterX - 18, ind.CenterY), new Point(ind.CenterX + 18, ind.CenterY), new Scalar(0, 0, 255), 2);
                Cv2.Line(annotated, new Point(ind.CenterX, ind.CenterY - 18), new Point(ind.CenterX, ind.CenterY + 18), new Scalar(0, 0, 255), 2);
                Cv2.PutText(
                    annotated,
                    $"Indicator ({ind.CenterX},{ind.CenterY}) c={ind.Confidence:F3}",
                    new Point(Math.Max(4, ind.CenterX - 120), Math.Max(24, ind.CenterY - 18)),
                    HersheyFonts.HersheySimplex,
                    0.55,
                    new Scalar(0, 0, 255),
                    2);
            }

            if (ocrRect is { } o)
            {
                Cv2.Rectangle(annotated, new Rect(o.X, o.Y, o.W, o.H), new Scalar(0, 255, 0), 2);
                Cv2.PutText(
                    annotated,
                    $"OCR ROI ({o.X},{o.Y},{o.W},{o.H})",
                    new Point(Math.Max(4, o.X), Math.Max(24, o.Y - 6)),
                    HersheyFonts.HersheySimplex,
                    0.55,
                    new Scalar(0, 255, 0),
                    2);

                var ox = Math.Clamp(o.X, 0, source.Width - 1);
                var oy = Math.Clamp(o.Y, 0, source.Height - 1);
                var ow = Math.Clamp(o.W, 1, source.Width - ox);
                var oh = Math.Clamp(o.H, 1, source.Height - oy);
                if (ow > 1 && oh > 1)
                {
                    using var crop = new Mat(source, new Rect(ox, oy, ow, oh));
                    foreach (var root in outputRoots)
                    {
                        var cropPath = Path.Combine(root, "crops", $"ocr-{reason}-{token}.png");
                        Cv2.ImWrite(cropPath, crop);
                    }
                }
            }

            var meta = BuildArmyMonitorDebugMeta(reason, context.ScreenshotPath, gateRect, indicator, ocrRect, ocrValue);

            foreach (var root in outputRoots)
            {
                var annotatedPath = Path.Combine(root, "annotated", $"screen-{reason}-{token}.png");
                Cv2.ImWrite(annotatedPath, annotated);

                var metaPath = Path.Combine(root, "meta", $"meta-{reason}-{token}.txt");
                await File.WriteAllTextAsync(metaPath, meta, cancellationToken);
            }

            _logger.LogInformation(
                "Army monitor debug saved: reason={Reason} indicator=({IX},{IY}) ocr={Ocr}",
                reason,
                indicator?.CenterX ?? -1,
                indicator?.CenterY ?? -1,
                ocrValue?.ToString() ?? "null");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save army monitor debug artifacts for reason={Reason}.", reason);
        }
    }

    private static string BuildArmyMonitorDebugMeta(
        string reason,
        string screenshotPath,
        (int X, int Y, int W, int H)? gateRect,
        DetectionResult? indicator,
        (int X, int Y, int W, int H)? ocrRect,
        int? ocrValue)
    {
        var lines = new List<string>
        {
            $"timestamp_utc={DateTimeOffset.UtcNow:O}",
            $"reason={reason}",
            $"screenshot={screenshotPath}",
            $"ocr_value={(ocrValue.HasValue ? ocrValue.Value.ToString() : "null")}"
        };

        if (gateRect is { } g)
        {
            lines.Add($"gate_roi_x={g.X}");
            lines.Add($"gate_roi_y={g.Y}");
            lines.Add($"gate_roi_w={g.W}");
            lines.Add($"gate_roi_h={g.H}");
        }

        if (indicator is { } ind)
        {
            lines.Add($"indicator_match={ind.IsMatch}");
            lines.Add($"indicator_confidence={ind.Confidence:F6}");
            lines.Add($"indicator_x={ind.CenterX}");
            lines.Add($"indicator_y={ind.CenterY}");
        }

        if (ocrRect is { } o)
        {
            lines.Add($"ocr_roi_x={o.X}");
            lines.Add($"ocr_roi_y={o.Y}");
            lines.Add($"ocr_roi_w={o.W}");
            lines.Add($"ocr_roi_h={o.H}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task<bool> TryClosePopupAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var close = await FindBestTemplateAsync(context, new[] { "popup_close.png" }, PopupCloseThresholds, cancellationToken);
        if (!close.IsMatch || close.Confidence < PopupCloseMinConfidence)
        {
            return false;
        }

        var (width, height) = await _emulator.GetResolutionAsync(cancellationToken);
        var inTopRight = close.CenterX >= (int)(width * 0.80)
            && close.CenterY <= (int)(height * 0.25);
        if (!inTopRight)
        {
            return false;
        }

        await _emulator.TapAsync(close.CenterX, close.CenterY, cancellationToken);
        _logger.LogInformation("Army monitor precheck: closed popup via close button.");
        return true;
    }

    private async Task<bool> TryClickMapButtonAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var map = await FindBestTemplateAsync(context, new[] { "map_button.png" }, new[] { 0.80, 0.72, 0.64, 0.56, 0.48 }, cancellationToken);
        if (!map.IsMatch)
        {
            return false;
        }

        await _emulator.TapAsync(map.CenterX, map.CenterY, cancellationToken);
        _logger.LogInformation("Army monitor precheck: clicked map button.");
        return true;
    }

    private async Task<DetectionResult> FindBestTemplateAsync(
        BotExecutionContext context,
        IReadOnlyCollection<string> templates,
        IReadOnlyCollection<double> thresholds,
        CancellationToken cancellationToken)
    {
        var best = DetectionResult.NotFound;
        foreach (var templateName in templates)
        {
            var templatePath = Path.Combine(context.TemplateRoot, templateName);
            if (!File.Exists(templatePath))
            {
                continue;
            }

            foreach (var threshold in thresholds)
            {
                context = await CaptureContextAsync(context, cancellationToken);
                var detection = await _imageDetector.FindTemplateAsync(
                    context.ScreenshotPath,
                    templatePath,
                    threshold,
                    cancellationToken);

                if (!detection.IsMatch)
                {
                    continue;
                }

                if (!best.IsMatch || detection.Confidence > best.Confidence)
                {
                    best = detection;
                }
            }
        }

        return best;
    }

    private async Task<BotExecutionContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        var deviceId = await _emulator.GetConnectedDeviceAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new InvalidOperationException("No active device found for army monitor.");
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
        await _emulator.TakeScreenshotAsync(screenshotPath, cancellationToken);
        return context with { ScreenshotPath = screenshotPath };
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
