namespace DISRoute.Server.Operational;

public sealed class AlertEscalationWorker(
    FeatureStore store,
    IConfiguration configuration,
    HaCoordinatorService ha,
    OperationalMutationBuffer mutationBuffer,
    StatePersistenceService persistence,
    AuthoritativeCommandGate commandGate,
    ILogger<AlertEscalationWorker> logger) : BackgroundService
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
                    var advisory = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Alerting:UnacknowledgedAdvisorySeconds", 60), 10, 3600));
                    var warning = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Alerting:UnacknowledgedWarningSeconds", 180), 20, 7200));
                    if (string.Equals(persistence.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
                    {
                        using var lease = await commandGate.EnterAsync(stoppingToken);
                        var changed = store.EscalateUnacknowledgedAlerts(DateTimeOffset.UtcNow, advisory, warning);
                        if (changed.Count > 0)
                        {
                            var target = mutationBuffer.Queued;
                            if (!await mutationBuffer.WaitUntilPersistedAsync(target, TimeSpan.FromSeconds(5), stoppingToken))
                                throw new TimeoutException("Alert escalation mutation journal did not reach the PostgreSQL durability boundary.");
                            await persistence.FlushWithinCommandGateAsync(stoppingToken);
                        }
                    }
                    else
                    {
                        store.EscalateUnacknowledgedAlerts(DateTimeOffset.UtcNow, advisory, warning);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OperationalLog.AlertEscalationWorkerFailed(logger, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
