namespace DISRoute.Server.Operational;

/// <summary>
/// Prevents a successful API mutation response from being returned until the authoritative
/// mutation journal/outbox has caught up and the current operational state has been checkpointed.
/// In PostgreSQL mode each HTTP mutation is correlated to a durability batch and executed under
/// the node command gate. Outbox rows for that batch cannot be published until the checkpoint /
/// normalized-projection transaction marks them authoritative. Development keeps the fsync-backed
/// NDJSON journal plus atomic file checkpoint behavior.
/// </summary>
public sealed class DurabilityCommitFilter(
    OperationalMutationBuffer mutationBuffer,
    DurableMutationWriterService mutationWriter,
    StatePersistenceService persistence,
    AuthoritativeCommandGate commandGate,
    IConfiguration configuration,
    ILogger<DurabilityCommitFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var isMutation = !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method);

        var localHaControl = request.Path.StartsWithSegments("/api/admin/ha/local");
        if (!isMutation || localHaControl || request.Path.Equals("/api/persistence/flush", StringComparison.OrdinalIgnoreCase))
            return await next(context);

        var postgres = string.Equals(persistence.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase);
        IDisposable? commandLease = null;
        Guid? batchId = null;
        DISRoutePersistedState? before = null;

        try
        {
            if (postgres)
            {
                commandLease = await commandGate.EnterAsync(context.HttpContext.RequestAborted);
                batchId = Guid.NewGuid();
                before = persistence.CaptureInMemoryState();
            }

            object? result;
            IDisposable? batchScope = batchId is Guid id ? mutationBuffer.BeginAuthoritativeBatch(id) : null;
            try
            {
                result = await next(context);
            }
            catch
            {
                if (postgres && before is not null && batchId is Guid failedBatch)
                {
                    persistence.RestoreInMemoryState(before);
                    mutationBuffer.AbandonBatch(failedBatch);
                }
                throw;
            }
            finally
            {
                batchScope?.Dispose();
            }

            if (postgres && result is IStatusCodeHttpResult { StatusCode: >= 400 } && before is not null && batchId is Guid rejectedBatch)
            {
                persistence.RestoreInMemoryState(before);
                mutationBuffer.AbandonBatch(rejectedBatch);
                return result;
            }

            var target = mutationBuffer.Queued;
            var timeoutSeconds = Math.Clamp(configuration.GetValue("Persistence:CommitTimeoutSeconds", 5), 1, 60);
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (!await mutationBuffer.WaitUntilPersistedAsync(target, timeout, context.HttpContext.RequestAborted))
            {
                OperationalLog.DurabilityTimeout(
                    logger, timeoutSeconds, mutationBuffer.Queued, mutationBuffer.Persisted, mutationBuffer.Failed, mutationWriter.Status.StateDescription());
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Durability acknowledgement timeout",
                    detail: "The command may have been applied in memory, but DISRoute could not confirm durable journal persistence before the acknowledgement deadline. Retry using the same idempotency key.");
            }

            if (postgres) await persistence.FlushWithinCommandGateAsync(context.HttpContext.RequestAborted);
            else await persistence.FlushAsync(context.HttpContext.RequestAborted);

            if (!string.Equals(persistence.Status.State, "Healthy", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Persistence degraded",
                    detail: persistence.Status.LastError ?? "DISRoute could not confirm durable state persistence.");
            }

            if (postgres) QualificationFaultInjection.CrashIfRequested(context.HttpContext, "after-durable-commit");
            return result;
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (postgres && before is not null && batchId is Guid failedBatch &&
                mutationBuffer.PendingAuthoritativeBatchIds.Contains(failedBatch))
            {
                persistence.RestoreInMemoryState(before);
                mutationBuffer.AbandonBatch(failedBatch);
            }
            OperationalLog.DurabilityFailed(logger, request.Method, request.Path.Value ?? string.Empty, ex);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Durability acknowledgement failed",
                detail: "DISRoute could not confirm that the command was durably persisted. Retry using the same idempotency key.");
        }
        finally
        {
            commandLease?.Dispose();
        }
    }
}

internal static class MutationJournalStatusExtensions
{
    public static string StateDescription(this MutationJournalStatusDto status) =>
        $"{status.Provider}: queued={status.Queued}, persisted={status.Persisted}, failed={status.Failed}, pendingAuthoritativeBatches={status.PendingAuthoritativeBatches}, lastError={status.LastError ?? "none"}";
}
