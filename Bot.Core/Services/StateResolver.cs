using Bot.Core.Configuration;
using Bot.Core.Enums;
using Bot.Core.Interfaces;
using Bot.Core.Models;
using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using Microsoft.Extensions.Logging;

namespace Bot.Core.Services;

public sealed class StateResolver : IStateResolver
{
    private static readonly string[] ResourceTemplates =
    {
        "resource_stone.png",
        "resource_stone_1.png",
        "resource_wood.png",
        "resource_wood_1.png",
        "resource_ore.png",
        "resource_ore_1.png",
        "resource_food.png",
        "resource_food_1.png",
        "resource_rune.png",
        "resource_rune_1.png"
    };

    private const string WorldMapCastleTemplate = "castle.png";
    private const double WorldMapCastleThreshold = 0.60;

    private static readonly string[] GatherTemplates =
    {
        "gather_button.png"
    };

    private static readonly string[] TilePopupTemplates =
    {
        "transfer_button.png",
        "occupy_button.png"
    };

    private static readonly string[] MarchTemplates =
    {
        "clear_section_button.png",
        "deploy_button.png"
    };

    private readonly IImageDetector _imageDetector;
    private readonly ILogger<StateResolver> _logger;

    public StateResolver(IImageDetector imageDetector, ILogger<StateResolver> logger)
    {
        _imageDetector = imageDetector;
        _logger = logger;
    }

    public async Task<GameState> ResolveAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (await IsMarchScreenAsync(context, cancellationToken))
        {
            return GameState.MarchScreen;
        }

        if (await IsResourcePopupAsync(context, cancellationToken))
        {
            return GameState.ResourcePopup;
        }

        if (await IsTilePopupAsync(context, cancellationToken))
        {
            return GameState.TilePopup;
        }

        if (await IsCityViewAsync(context, cancellationToken))
        {
            return GameState.City;
        }

        if (await IsWorldMapAsync(context, cancellationToken))
        {
            return GameState.WorldMap;
        }

        return GameState.Unknown;
    }

    public async Task<bool> IsCityViewAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var detection = await DetectAsync(context, "map_button.png", 0.85, cancellationToken);
        return detection.IsMatch;
    }

    public async Task<bool> IsWorldMapAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var castleTemplatePath = Path.Combine(context.TemplateRoot, WorldMapCastleTemplate);
        if (File.Exists(castleTemplatePath))
        {
            var (roiX, roiY, roiW, roiH) = GetCastleRoi(context, cancellationToken);

            if (ManualDetectionSettings.SaveDebugAlways)
            {
                SaveCastleRoiCrop(context.ScreenshotPath, roiX, roiY, roiW, roiH);
            }

            var castleDetection = await _imageDetector.FindTemplateInRegionAsync(
                context.ScreenshotPath,
                castleTemplatePath,
                roiX,
                roiY,
                roiW,
                roiH,
                WorldMapCastleThreshold,
                cancellationToken);

            _logger.LogDebug(
                "World-map castle detection match={Match} confidence={Confidence:F3} center=({X},{Y}) roi=({RX},{RY},{RW},{RH})",
                castleDetection.IsMatch,
                castleDetection.Confidence,
                castleDetection.CenterX,
                castleDetection.CenterY,
                roiX,
                roiY,
                roiW,
                roiH);

            if (castleDetection.IsMatch)
            {
                return true;
            }
        }

        var detection = await DetectAnyAsync(context, ResourceTemplates, 0.55, cancellationToken);
        return detection.IsMatch;
    }

    public async Task<bool> IsTilePopupAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var detection = await DetectAnyAsync(context, TilePopupTemplates, 0.40, cancellationToken);
        return detection.IsMatch;
    }

    public async Task<bool> IsResourcePopupAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var detection = await DetectAnyAsync(context, GatherTemplates, 0.45, cancellationToken);
        return detection.IsMatch;
    }

    public async Task<bool> IsMarchScreenAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var detection = await DetectAnyAsync(context, MarchTemplates, 0.45, cancellationToken);
        return detection.IsMatch;
    }

    private async Task<DetectionResult> DetectAnyAsync(
        BotExecutionContext context,
        IReadOnlyCollection<string> templates,
        double threshold,
        CancellationToken cancellationToken)
    {
        var best = DetectionResult.NotFound;
        foreach (var template in templates)
        {
            var detection = await DetectAsync(context, template, threshold, cancellationToken);
            if (!best.IsMatch || detection.Confidence > best.Confidence)
            {
                best = detection;
            }
        }

        return best;
    }

    private async Task<DetectionResult> DetectAsync(
        BotExecutionContext context,
        string templateName,
        double threshold,
        CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(context.TemplateRoot, templateName);
        if (!File.Exists(templatePath))
        {
            _logger.LogDebug("Template missing for state resolver: {TemplatePath}", templatePath);
            return DetectionResult.NotFound;
        }

        var detection = await _imageDetector.FindTemplateAsync(
            context.ScreenshotPath,
            templatePath,
            threshold,
            cancellationToken);

        _logger.LogDebug(
            "State detection template={Template} match={Match} confidence={Confidence:F3}",
            templateName,
            detection.IsMatch,
            detection.Confidence);

        return detection;
    }

    private void SaveCastleRoiCrop(string screenshotPath, int roiX, int roiY, int roiW, int roiH)
    {
        try
        {
            using var screenshot = OpenCvSharp.Cv2.ImRead(screenshotPath, OpenCvSharp.ImreadModes.Color);
            if (screenshot.Empty()) return;

            var cx = Math.Clamp(roiX, 0, screenshot.Width - 1);
            var cy = Math.Clamp(roiY, 0, screenshot.Height - 1);
            var cw = Math.Clamp(roiW, 1, screenshot.Width - cx);
            var ch = Math.Clamp(roiH, 1, screenshot.Height - cy);

            var debugDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "debug", "castle-roi");
            Directory.CreateDirectory(debugDir);

            var token = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}";
            var cropPath = Path.Combine(debugDir, $"crop-{token}.png");
            var annotatedPath = Path.Combine(debugDir, $"annotated-{token}.png");

            // Save the crop
            using var crop = new OpenCvSharp.Mat(screenshot, new OpenCvSharp.Rect(cx, cy, cw, ch));
            OpenCvSharp.Cv2.ImWrite(cropPath, crop);

            // Save annotated full screenshot with ROI box drawn
            using var annotated = screenshot.Clone();
            OpenCvSharp.Cv2.Rectangle(annotated, new OpenCvSharp.Rect(cx, cy, cw, ch), new OpenCvSharp.Scalar(0, 165, 255), 2);
            OpenCvSharp.Cv2.PutText(annotated, $"Castle ROI ({cx},{cy},{cw},{ch})",
                new OpenCvSharp.Point(Math.Max(4, cx), Math.Max(20, cy - 6)),
                OpenCvSharp.HersheyFonts.HersheySimplex, 0.55, new OpenCvSharp.Scalar(0, 165, 255), 2);
            OpenCvSharp.Cv2.ImWrite(annotatedPath, annotated);

            _logger.LogDebug("Castle ROI debug saved: roi=({X},{Y},{W},{H}) screenSize=({SW},{SH}) crop={Crop}",
                cx, cy, cw, ch, screenshot.Width, screenshot.Height, cropPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save castle ROI debug crop.");
        }
    }

    private static (int X, int Y, int W, int H) GetCastleRoi(
        BotExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (ManualDetectionSettings.UseManualCastleRegion)
        {
            var xs = new[] { ManualDetectionSettings.CastleRegionX1, ManualDetectionSettings.CastleRegionX2, ManualDetectionSettings.CastleRegionX3, ManualDetectionSettings.CastleRegionX4 };
            var ys = new[] { ManualDetectionSettings.CastleRegionY1, ManualDetectionSettings.CastleRegionY2, ManualDetectionSettings.CastleRegionY3, ManualDetectionSettings.CastleRegionY4 };
            var minX = xs.Min();
            var minY = ys.Min();
            var w = xs.Max() - minX;
            var h = ys.Max() - minY;
            if (w >= 8 && h >= 8)
            {
                return (minX, minY, w, h);
            }
        }

        using var screenshot = OpenCvSharp.Cv2.ImRead(context.ScreenshotPath, OpenCvSharp.ImreadModes.Grayscale);
        cancellationToken.ThrowIfCancellationRequested();
        if (!screenshot.Empty())
        {
            var width = screenshot.Width;
            var height = screenshot.Height;
            var x = 0;
            var y = (int)(height * 0.60);
            var w = Math.Max(64, (int)(width * 0.24));
            var h = Math.Max(64, height - y);
            return (x, y, Math.Min(w, width), Math.Min(h, height - y));
        }

        return (0, 480, 420, 420);
    }
}
