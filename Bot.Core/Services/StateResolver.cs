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
        "resource_tile.png",
        "resource_stone.png",
        "resource_wood.png",
        "resource_ore.png",
        "resource_food.png",
        "resource_rune.png"
    };

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
        "march_button.png"
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
}
