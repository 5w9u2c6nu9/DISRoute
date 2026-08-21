namespace DISRoute.Integrations;

public abstract class SimulationProviderBase(string key, string displayName, string category) : IProvider
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;
    public string Category { get; } = category;
    public virtual ValueTask<ProviderValidationResult> ValidateConfigurationAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderValidationResult(true, [], ["Simulation provider — never use for production external control."]));
    public virtual ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ProviderCapability>>([]);
    public virtual ValueTask<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderHealth(Key, ProviderAvailability.Available, "Simulation", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
}

public sealed class SimulationStationAlertProvider() : SimulationProviderBase("simulation-station-alert", "Simulation Station Alert", "Station"), IStationAlertProvider
{
    public override ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ProviderCapability>>([new("station.alert", true, true, "Simulated confirmation")]);
    public ValueTask<ProviderOperationResult> AlertAsync(ProviderOperationContext context, StationAlertRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderOperationResult(context.OperationId, "Confirmed", $"SIM-ALERT-{context.OperationId:N}", null, DateTimeOffset.UtcNow));
}

public sealed class SimulationPrintProvider() : SimulationProviderBase("simulation-print", "Simulation Printer", "Printing"), IPrintProvider
{
    public ValueTask<ProviderOperationResult> PrintAsync(ProviderOperationContext context, PrintJobRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderOperationResult(context.OperationId, "Printed", $"SIM-PRINT-{context.OperationId:N}", null, DateTimeOffset.UtcNow));
}

public sealed class SimulationRoutingProvider() : SimulationProviderBase("simulation-routing", "Simulation Routing", "GIS"), IRoutingProvider
{
    public ValueTask<RouteProviderResult> RouteAsync(RouteProviderRequest request, CancellationToken cancellationToken = default)
    {
        var geometry = $"{{\"type\":\"LineString\",\"coordinates\":[[{request.Origin.Longitude:R},{request.Origin.Latitude:R}],[{request.Destination.Longitude:R},{request.Destination.Latitude:R}]]}}";
        return ValueTask.FromResult(new RouteProviderResult(1000, 120, geometry, ["Simulation route"]));
    }
}

public sealed class SimulationFireReportingProvider() : SimulationProviderBase("simulation-fire-reporting", "Simulation Fire Reporting", "Reporting"), IFireReportingProvider
{
    public ValueTask<ProviderValidationResult> ValidateReportAsync(FireReportSubmission report, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderValidationResult(!string.IsNullOrWhiteSpace(report.PayloadJson), string.IsNullOrWhiteSpace(report.PayloadJson) ? ["Payload is empty."] : [], []));
    public ValueTask<ProviderOperationResult> SubmitReportAsync(ProviderOperationContext context, FireReportSubmission report, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ProviderOperationResult(context.OperationId, "Accepted", $"SIM-NERIS-{Guid.NewGuid():N}", null, DateTimeOffset.UtcNow));
}


public sealed class SimulationRecordingProvider() : SimulationProviderBase("simulation-recording", "Simulation Recording / Eventide", "Communications"), IRecordingProvider
{
    public ValueTask<IReadOnlyList<RecordingReference>> FindRecordingsAsync(RecordingQuery query, CancellationToken cancellationToken = default)
    {
        var start = query.FromUtc;
        var end = query.ToUtc <= start ? start.AddMinutes(2) : query.ToUtc;
        return ValueTask.FromResult<IReadOnlyList<RecordingReference>>([new RecordingReference($"SIM-REC-{Guid.NewGuid():N}", "audio/opus", start, end, $"Simulated recording for {query.Channel ?? query.TelephoneNumber ?? "incident"}", null)]);
    }
}

public sealed class SimulationCameraDiscoveryProvider() : SimulationProviderBase("simulation-onvif", "Simulation ONVIF Discovery", "Video"), ICameraDiscoveryProvider
{
    public ValueTask<IReadOnlyList<CameraDiscoveryCandidate>> DiscoverAsync(Guid? stationId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<CameraDiscoveryCandidate>>([
            new CameraDiscoveryCandidate("sim-onvif-1", "Station Bay Camera", "rtsp://192.0.2.10/stream1", "Simulation", "ONVIF-CAM", "SIM0001", "Discovered"),
            new CameraDiscoveryCandidate("sim-onvif-2", "Apron Camera", "rtsp://192.0.2.11/stream1", "Simulation", "ONVIF-CAM", "SIM0002", "Discovered")]);
}

public sealed class SimulationRadioProvider() : SimulationProviderBase("simulation-radio", "Simulation Radio Gateway", "Communications"), IRadioProvider
{
    public async IAsyncEnumerable<RadioEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
    public ValueTask<ProviderOperationResult> ClearEmergencyAsync(ProviderOperationContext context, string nativeRadioId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new ProviderOperationResult(context.OperationId, "Confirmed", $"SIM-RADIO-{nativeRadioId}-{context.OperationId:N}", null, DateTimeOffset.UtcNow));
}
