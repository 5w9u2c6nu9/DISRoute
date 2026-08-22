using DISRoute.Integrations;
using DISRoute.Server.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace DISRoute.Server.Operational;

public sealed class NerisSubmissionWorker(
    OperationalStore store,
    NerisSubmissionService service,
    HaCoordinatorService ha,
    OperationalMutationBuffer mutationBuffer,
    StatePersistenceService persistence,
    AuthoritativeCommandGate commandGate,
    IHubContext<OperationsHub> hub,
    ILogger<NerisSubmissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!ha.AcceptingWrites)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    foreach (var queued in store.NerisSubmissions.Where(x => x.State == "Queued").OrderBy(x => x.UpdatedAtUtc).Take(20).ToArray())
                        await ProcessOneAsync(queued.Id, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OperationalLog.NerisSubmissionWorkerFailed(logger, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task ProcessOneAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var snapshot = store.GetNerisSubmission(submissionId);
        IFireReportingProvider provider;
        try
        {
            provider = service.ResolveProvider(snapshot.ProviderKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failedStart = await BeginDurablyAsync(submissionId, snapshot.ProviderKey ?? "unconfigured", cancellationToken);
            var failed = await CompleteValidationFailureDurablyAsync(submissionId, failedStart.Attempt.Id, [ex.Message], [], cancellationToken);
            await BroadcastAsync(failed, cancellationToken);
            return;
        }

        var started = await BeginDurablyAsync(submissionId, provider.Key, cancellationToken);
        var fireReport = new FireReportSubmission(started.Submission.Id, started.Submission.PayloadJson, started.Submission.ProfileVersion);
        ProviderValidationResult validation;
        try
        {
            validation = await provider.ValidateReportAsync(fireReport, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failed = await CompleteValidationFailureDurablyAsync(submissionId, started.Attempt.Id, [$"Provider validation threw: {ex.Message}"], [], cancellationToken);
            await BroadcastAsync(failed, cancellationToken);
            return;
        }
        if (!validation.Valid)
        {
            var failed = await CompleteValidationFailureDurablyAsync(submissionId, started.Attempt.Id, validation.Errors, validation.Warnings, cancellationToken);
            await BroadcastAsync(failed, cancellationToken);
            return;
        }

        ProviderOperationResult result;
        try
        {
            var context = new ProviderOperationContext(started.Attempt.OperationId, started.Submission.AgencyId, started.Submission.IncidentId, started.Attempt.Actor, started.Attempt.RequestedAtUtc, started.Submission.Id.ToString("N"));
            result = await provider.SubmitReportAsync(context, fireReport, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new ProviderOperationResult(started.Attempt.OperationId, "Failed", null, ex.Message, DateTimeOffset.UtcNow);
        }
        var completed = await CompleteAttemptDurablyAsync(submissionId, started.Attempt.Id, result, validation.Warnings, cancellationToken);
        await BroadcastAsync(completed, cancellationToken);
    }

    private async Task<(DISRoute.Contracts.Operational.NerisSubmissionDto Submission, DISRoute.Contracts.Operational.NerisSubmissionAttemptDto Attempt)> BeginDurablyAsync(Guid submissionId, string providerKey, CancellationToken cancellationToken)
    {
        if (!string.Equals(persistence.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var started = store.BeginNerisSubmissionAttempt(submissionId, providerKey, "neris-submission-worker");
            await persistence.FlushAsync(cancellationToken);
            return started;
        }

        using var lease = await commandGate.EnterAsync(cancellationToken);
        var pgStarted = store.BeginNerisSubmissionAttempt(submissionId, providerKey, "neris-submission-worker");
        await WaitForMutationBoundaryAsync(cancellationToken);
        await persistence.FlushWithinCommandGateAsync(cancellationToken);
        return pgStarted;
    }

    private async Task<DISRoute.Contracts.Operational.NerisSubmissionDto> CompleteValidationFailureDurablyAsync(Guid submissionId, Guid attemptId, IReadOnlyList<string> errors, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        if (!string.Equals(persistence.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var updated = store.CompleteNerisProviderValidationFailure(submissionId, attemptId, errors, warnings, "neris-submission-worker");
            await persistence.FlushAsync(cancellationToken);
            return updated;
        }
        using var lease = await commandGate.EnterAsync(cancellationToken);
        var pgUpdated = store.CompleteNerisProviderValidationFailure(submissionId, attemptId, errors, warnings, "neris-submission-worker");
        await WaitForMutationBoundaryAsync(cancellationToken);
        await persistence.FlushWithinCommandGateAsync(cancellationToken);
        return pgUpdated;
    }

    private async Task<DISRoute.Contracts.Operational.NerisSubmissionDto> CompleteAttemptDurablyAsync(Guid submissionId, Guid attemptId, ProviderOperationResult result, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
    {
        if (!string.Equals(persistence.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var updated = store.CompleteNerisProviderAttempt(submissionId, attemptId, result.State, result.ProviderReference, result.Error, warnings, "neris-submission-worker");
            await persistence.FlushAsync(cancellationToken);
            return updated;
        }
        using var lease = await commandGate.EnterAsync(cancellationToken);
        var pgUpdated = store.CompleteNerisProviderAttempt(submissionId, attemptId, result.State, result.ProviderReference, result.Error, warnings, "neris-submission-worker");
        await WaitForMutationBoundaryAsync(cancellationToken);
        await persistence.FlushWithinCommandGateAsync(cancellationToken);
        return pgUpdated;
    }

    private Task BroadcastAsync(DISRoute.Contracts.Operational.NerisSubmissionDto submission, CancellationToken cancellationToken) =>
        hub.Clients.Group($"agency:{submission.AgencyId:D}").SendAsync("nerisSubmissionChanged", submission, cancellationToken);

    private async Task WaitForMutationBoundaryAsync(CancellationToken cancellationToken)
    {
        var target = mutationBuffer.Queued;
        if (!await mutationBuffer.WaitUntilPersistedAsync(target, TimeSpan.FromSeconds(5), cancellationToken))
            throw new TimeoutException("NERIS submission mutation journal did not reach the PostgreSQL durability boundary.");
    }
}
