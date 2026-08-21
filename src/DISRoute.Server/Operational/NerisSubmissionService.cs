using DISRoute.Contracts.Operational;
using DISRoute.Integrations;

namespace DISRoute.Server.Operational;

public sealed class NerisSubmissionService(OperationalStore store, ProviderRegistry registry)
{
    public async Task<NerisProviderValidationDto> ValidateProviderAsync(Guid submissionId, string? requestedProviderKey, CancellationToken cancellationToken = default)
    {
        var submission = store.GetNerisSubmission(submissionId);
        var provider = ResolveProvider(requestedProviderKey ?? submission.ProviderKey);
        var result = await provider.ValidateReportAsync(new FireReportSubmission(submission.Id, submission.PayloadJson, submission.ProfileVersion), cancellationToken);
        return new NerisProviderValidationDto(submission.Id, provider.Key, result.Valid, result.Errors, result.Warnings, DateTimeOffset.UtcNow);
    }

    internal IFireReportingProvider ResolveProvider(string? requestedProviderKey)
    {
        if (!string.IsNullOrWhiteSpace(requestedProviderKey))
            return registry.Get<IFireReportingProvider>(requestedProviderKey.Trim()) ?? throw new InvalidOperationException($"Fire-reporting provider '{requestedProviderKey.Trim()}' is not configured.");
        var providers = registry.GetAll<IFireReportingProvider>();
        return providers.Count > 0 ? providers[0] : throw new InvalidOperationException("No fire-reporting provider is configured.");
    }
}
