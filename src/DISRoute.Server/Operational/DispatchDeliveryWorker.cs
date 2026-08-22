using DISRoute.Integrations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DISRoute.Server.Operational;

public sealed class DispatchDeliveryWorker(
    FeatureStore store,
    OperationalStore core,
    ProviderRegistry registry,
    HaCoordinatorService ha,
    OperationalMutationBuffer mutationBuffer,
    StatePersistenceService persistence,
    AuthoritativeCommandGate commandGate,
    ILogger<DispatchDeliveryWorker> logger) : BackgroundService
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
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }
                    foreach (var delivery in store.DispatchDeliveries.Where(x => x.State == "Pending").Take(100).ToArray())
                    {
                        var provider = delivery.Provider.ToLowerInvariant();
                        if ((provider.Contains("station", StringComparison.OrdinalIgnoreCase) || provider == "runsheet") && core.Modules.FirstOrDefault(x => x.Key == "station") is { Enabled: false })
                        {
                            await CompleteDeliveryDurablyAsync(delivery.Id, false, "Station module is disabled.", "delivery-worker", stoppingToken);
                            continue;
                        }
                        var cancellation = provider.StartsWith("cancel:", StringComparison.Ordinal);
                        if (cancellation) provider = provider[7..];
                        switch (provider)
                        {
                            case "stationdisplay":
                            case "runsheet":
                            case "mobile":
                                await CompleteDeliveryDurablyAsync(delivery.Id, true, null, cancellation ? "cancellation-worker" : "delivery-worker", stoppingToken);
                                break;
                            case "stationalert":
                            {
                                var action = store.DispatchActions.FirstOrDefault(x => x.Id == delivery.DispatchActionId);
                                if (action is null)
                                {
                                    await CompleteDeliveryDurablyAsync(delivery.Id, false, "Dispatch action not found.", "delivery-worker", stoppingToken);
                                    break;
                                }
                                var incident = core.GetIncident(action.IncidentId);
                                var providers = registry.GetAll<IStationAlertProvider>();
                                var providerInstance = providers.Count > 0 ? providers[0] : null;
                                if (providerInstance is null)
                                {
                                    await CompleteDeliveryDurablyAsync(delivery.Id, false, "Station alert provider is not configured.", "delivery-worker", stoppingToken);
                                    break;
                                }
                                var callSigns = action.UnitIds.Select(id => core.Units.FirstOrDefault(x => x.Id == id)?.CallSign ?? id.ToString("D")).ToArray();
                                var context = new ProviderOperationContext(delivery.Id, action.AgencyId, action.IncidentId, action.Actor, DateTimeOffset.UtcNow, delivery.Id.ToString("N"));
                                var result = await providerInstance.AlertAsync(context, new StationAlertRequest(incident.StationId, incident.Id, incident.Number, incident.Nature, incident.Address, callSigns, null), stoppingToken);
                                await CompleteDeliveryDurablyAsync(delivery.Id, result.State is "Confirmed" or "Accepted" or "Spooled", result.Error, "delivery-worker", stoppingToken);
                                break;
                            }
                            default:
                            {
                                var configured = store.Providers.Any(x => x.Key.Equals(provider, StringComparison.OrdinalIgnoreCase) && x.Enabled && x.State == "Available");
                                await CompleteDeliveryDurablyAsync(delivery.Id, configured, configured ? null : $"Provider '{delivery.Provider}' is not configured/available.", "delivery-worker", stoppingToken);
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OperationalLog.DispatchDeliveryWorkerFailed(logger, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task CompleteDeliveryDurablyAsync(Guid deliveryId, bool success, string? error, string actor, CancellationToken cancellationToken)
    {
        if (!string.Equals(persistence.Provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            store.CompleteDelivery(deliveryId, success, error, actor);
            return;
        }

        using var lease = await commandGate.EnterAsync(cancellationToken);
        store.CompleteDelivery(deliveryId, success, error, actor);
        var target = mutationBuffer.Queued;
        if (!await mutationBuffer.WaitUntilPersistedAsync(target, TimeSpan.FromSeconds(5), cancellationToken))
            throw new TimeoutException("Dispatch-delivery mutation journal did not reach the PostgreSQL durability boundary.");
        await persistence.FlushWithinCommandGateAsync(cancellationToken);
    }
}
