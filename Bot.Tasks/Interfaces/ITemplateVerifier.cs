namespace Bot.Tasks.Interfaces;

public interface ITemplateVerifier
{
    Task<TemplateVerificationResult> VerifyAsync(CancellationToken cancellationToken = default);
}

public sealed record TemplateVerificationResult(bool IsValid, string TemplateRoot, IReadOnlyCollection<string> MissingTemplates);
