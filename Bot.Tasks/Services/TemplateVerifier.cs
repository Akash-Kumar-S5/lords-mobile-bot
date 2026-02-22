using Bot.Tasks.Interfaces;
using Microsoft.Extensions.Logging;

namespace Bot.Tasks.Services;

public sealed class TemplateVerifier : ITemplateVerifier
{
    private static readonly string[] RequiredTemplates =
    {
        "map_button.png",
        "resource_tile.png",
        "gather_button.png",
        "lowest_tier_button.png",
        "deploy_button.png"
    };

    private readonly ILogger<TemplateVerifier> _logger;

    public TemplateVerifier(ILogger<TemplateVerifier> logger)
    {
        _logger = logger;
    }

    public Task<TemplateVerificationResult> VerifyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var templateRoot = ResolveTemplateRoot();
        var missing = new List<string>();

        foreach (var template in RequiredTemplates)
        {
            var path = Path.Combine(templateRoot, template);
            if (!File.Exists(path))
            {
                missing.Add(template);
            }
        }

        if (missing.Count == 0)
        {
            _logger.LogInformation("Template verification passed. Root: {TemplateRoot}", templateRoot);
            return Task.FromResult(new TemplateVerificationResult(true, templateRoot, Array.Empty<string>()));
        }

        _logger.LogError(
            "Template verification failed. Root: {TemplateRoot}. Missing: {Missing}",
            templateRoot,
            string.Join(", ", missing));

        return Task.FromResult(new TemplateVerificationResult(false, templateRoot, missing));
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
