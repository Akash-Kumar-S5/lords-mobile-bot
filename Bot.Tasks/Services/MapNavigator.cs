using Bot.Core.Interfaces;
using Bot.Core.Models;
using Bot.Emulator.Interfaces;
using Bot.Tasks.Interfaces;
using Bot.Vision.Interfaces;
using Bot.Vision.Models;
using Microsoft.Extensions.Logging;

namespace Bot.Tasks.Services;

public sealed class MapNavigator : IMapNavigator
{
    private static readonly string[] ResourceTemplates =
    {
        "resource_tile.png",
        "resource_stone.png",
        "resource_wood.png",
        "resource_ore.png",
        "resource_food.png",
        "resource_rune.png"
    };

    private static readonly double[] ResourceThresholds = { 0.76, 0.68, 0.60, 0.52, 0.45 };

    private readonly IEmulatorController _emulatorController;
    private readonly IImageDetector _imageDetector;
    private readonly IStateResolver _stateResolver;
    private readonly ILogger<MapNavigator> _logger;
    private readonly Random _random = Random.Shared;

    public MapNavigator(
        IEmulatorController emulatorController,
        IImageDetector imageDetector,
        IStateResolver stateResolver,
        ILogger<MapNavigator> logger)
    {
        _emulatorController = emulatorController;
        _imageDetector = imageDetector;
        _stateResolver = stateResolver;
        _logger = logger;
    }

    public async Task<bool> EnsureOnWorldMapAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (await _stateResolver.IsWorldMapAsync(context, cancellationToken))
        {
            return true;
        }

        if (await TryClickMapIconAsync(context, cancellationToken))
        {
            await Task.Delay(_random.Next(400, 1000), cancellationToken);
            return await _stateResolver.IsWorldMapAsync(context, cancellationToken);
        }

        _logger.LogWarning("Map button not detected. Attempting random pan recovery.");
        await RandomMapPanAsync(context, cancellationToken);
        return await _stateResolver.IsWorldMapAsync(context, cancellationToken);
    }

    public async Task ZoomOutAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var (width, height) = await _emulatorController.GetResolutionAsync(cancellationToken);
        var centerX = width / 2;
        var centerY = height / 2;

        // Single-finger zoom-out approximation for emulators that map drag to camera zoom behavior.
        await _emulatorController.SwipeAsync(centerX, centerY, centerX, (int)(height * 0.82), 320, cancellationToken);
        await Task.Delay(_random.Next(350, 950), cancellationToken);
        await _emulatorController.SwipeAsync(centerX, centerY, centerX, (int)(height * 0.82), 320, cancellationToken);

        _logger.LogInformation("Executed map zoom-out gestures.");
    }

    public async Task RandomMapPanAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var (width, height) = await _emulatorController.GetResolutionAsync(cancellationToken);

        var startX = (int)(width * NextRatio(0.35, 0.65));
        var startY = (int)(height * NextRatio(0.35, 0.65));
        var endX = (int)(width * NextRatio(0.2, 0.8));
        var endY = (int)(height * NextRatio(0.2, 0.8));

        await _emulatorController.SwipeAsync(startX, startY, endX, endY, _random.Next(240, 420), cancellationToken);
        _logger.LogInformation("Map pan executed: ({StartX},{StartY}) -> ({EndX},{EndY})", startX, startY, endX, endY);
    }

    public async Task<DetectionResult> FindResourceTileAsync(BotExecutionContext context, CancellationToken cancellationToken = default)
    {
        var result = DetectionResult.NotFound;
        string? pickedTemplate = null;

        foreach (var template in ResourceTemplates)
        {
            foreach (var threshold in ResourceThresholds)
            {
                var detection = await DetectAsync(context, template, threshold, cancellationToken);
                _logger.LogInformation(
                    "Resource detection template={Template} threshold={Threshold:F2} match={Match} confidence={Confidence:F3} center=({X},{Y})",
                    template,
                    threshold,
                    detection.IsMatch,
                    detection.Confidence,
                    detection.CenterX,
                    detection.CenterY);

                if (!detection.IsMatch)
                {
                    continue;
                }

                if (!result.IsMatch || detection.Confidence > result.Confidence)
                {
                    result = detection;
                    pickedTemplate = template;
                }
            }
        }

        _logger.LogInformation(
            "Resource tile selected template={Template} match={Match} confidence={Confidence:F3} center=({X},{Y})",
            pickedTemplate ?? "none",
            result.IsMatch,
            result.Confidence,
            result.CenterX,
            result.CenterY);
        return result;
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
            if (string.Equals(templateName, "map_button.png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(templateName, "resource_tile.png", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Template file missing: {TemplatePath}", templatePath);
            }
            return DetectionResult.NotFound;
        }

        return await _imageDetector.FindTemplateAsync(context.ScreenshotPath, templatePath, threshold, cancellationToken);
    }

    private async Task<bool> TryClickMapIconAsync(BotExecutionContext context, CancellationToken cancellationToken)
    {
        var templates = new[] { "map_button.png", "map_button_alt.png", "map_icon.png" };
        var thresholds = new[] { 0.80, 0.72, 0.64, 0.56, 0.48 };

        foreach (var template in templates)
        {
            foreach (var threshold in thresholds)
            {
                var detection = await DetectAsync(context, template, threshold, cancellationToken);
                _logger.LogInformation(
                    "Map icon detection template={Template} threshold={Threshold:F2} match={Match} confidence={Confidence:F3} center=({X},{Y})",
                    template,
                    threshold,
                    detection.IsMatch,
                    detection.Confidence,
                    detection.CenterX,
                    detection.CenterY);

                if (!detection.IsMatch)
                {
                    continue;
                }

                await TapWithOffsetAsync(detection.CenterX, detection.CenterY, cancellationToken);
                _logger.LogInformation("Clicked map icon using template {Template}.", template);
                return true;
            }
        }

        return false;
    }

    private async Task TapWithOffsetAsync(int x, int y, CancellationToken cancellationToken)
    {
        var offsetX = _random.Next(-5, 6);
        var offsetY = _random.Next(-5, 6);
        await _emulatorController.TapAsync(x + offsetX, y + offsetY, cancellationToken);
    }

    private double NextRatio(double min, double max) => min + ((max - min) * _random.NextDouble());
}
