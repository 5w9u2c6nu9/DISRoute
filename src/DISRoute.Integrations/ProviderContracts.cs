namespace DISRoute.Integrations;

public enum ProviderAvailability { NotConfigured, Available, Degraded, Unavailable }

public sealed record ProviderHealth(string Key, ProviderAvailability Availability, string? Detail, DateTimeOffset CheckedAtUtc, DateTimeOffset? LastSuccessfulInteractionUtc);
public sealed record ProviderCapability(string Key, bool Supported, bool Enabled, string? Detail);
public sealed record ProviderValidationResult(bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
public sealed record ProviderOperationContext(Guid OperationId, Guid AgencyId, Guid? IncidentId, string Actor, DateTimeOffset RequestedAtUtc, string? CorrelationKey);
public sealed record ProviderOperationResult(Guid OperationId, string State, string? ProviderReference, string? Error, DateTimeOffset CompletedAtUtc);

public interface IProvider
{
    string Key { get; }
    string DisplayName { get; }
    string Category { get; }
    ValueTask<ProviderValidationResult> ValidateConfigurationAsync(CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<ProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    ValueTask<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

public sealed record StationAlertRequest(Guid StationId, Guid IncidentId, string IncidentNumber, string Nature, string Address, IReadOnlyList<string> UnitCallSigns, string? AudioProfile);
public interface IStationAlertProvider : IProvider
{
    ValueTask<ProviderOperationResult> AlertAsync(ProviderOperationContext context, StationAlertRequest request, CancellationToken cancellationToken = default);
}

public sealed record PrintJobRequest(string PrinterKey, string DocumentType, Guid IncidentId, int Revision, ReadOnlyMemory<byte> Content, string ContentType);
public interface IPrintProvider : IProvider
{
    ValueTask<ProviderOperationResult> PrintAsync(ProviderOperationContext context, PrintJobRequest request, CancellationToken cancellationToken = default);
}

public sealed record RoutePoint(double Latitude, double Longitude);
public sealed record RouteProviderRequest(RoutePoint Origin, RoutePoint Destination, IReadOnlyList<string> AvoidanceGeometryGeoJson, string VehicleProfile);
public sealed record RouteProviderResult(double DistanceMeters, int DurationSeconds, string GeometryGeoJson, IReadOnlyList<string> Warnings);
public interface IRoutingProvider : IProvider
{
    ValueTask<RouteProviderResult> RouteAsync(RouteProviderRequest request, CancellationToken cancellationToken = default);
}

public sealed record RadioEvent(string EventType, string NativeRadioId, string Channel, string? UnitId, string? Text, DateTimeOffset SourceTimestamp, IReadOnlyDictionary<string,string> NativeData);
public interface IRadioProvider : IProvider
{
    IAsyncEnumerable<RadioEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
    ValueTask<ProviderOperationResult> ClearEmergencyAsync(ProviderOperationContext context, string nativeRadioId, CancellationToken cancellationToken = default);
}

public sealed record IncomingCallEvent(string ProviderCallId, string Caller, string Callback, string SuppliedLocation, string Nature, string State, DateTimeOffset SourceTimestamp, IReadOnlyDictionary<string,string> NativeData);
public interface ICallHandlingProvider : IProvider
{
    IAsyncEnumerable<IncomingCallEvent> ReadCallEventsAsync(CancellationToken cancellationToken = default);
}

public sealed record RecordingQuery(Guid? IncidentId, string? Channel, string? TelephoneNumber, DateTimeOffset FromUtc, DateTimeOffset ToUtc);
public sealed record RecordingReference(string ProviderReference, string MediaType, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Description, Uri? PlaybackUri);
public interface IRecordingProvider : IProvider
{
    ValueTask<IReadOnlyList<RecordingReference>> FindRecordingsAsync(RecordingQuery query, CancellationToken cancellationToken = default);
}


public sealed record CameraDiscoveryCandidate(string NativeId, string Name, string Uri, string? Manufacturer, string? Model, string? SerialNumber, string State);
public interface ICameraDiscoveryProvider : IProvider
{
    ValueTask<IReadOnlyList<CameraDiscoveryCandidate>> DiscoverAsync(Guid? stationId, CancellationToken cancellationToken = default);
}

public sealed record AvlObservation(string NativeResourceId, double Latitude, double Longitude, double? SpeedMps, double? HeadingDegrees, DateTimeOffset ObservedAtUtc, string Source);
public interface IAvlProvider : IProvider
{
    IAsyncEnumerable<AvlObservation> ReadObservationsAsync(CancellationToken cancellationToken = default);
}

public sealed record AisObservation(string Mmsi, string? Name, double Latitude, double Longitude, double? SpeedKnots, double? CourseDegrees, DateTimeOffset ObservedAtUtc, string Source);
public interface IAisProvider : IProvider
{
    IAsyncEnumerable<AisObservation> ReadObservationsAsync(CancellationToken cancellationToken = default);
}

public sealed record FireReportSubmission(Guid ReportId, string PayloadJson, string SchemaVersion);
public interface IFireReportingProvider : IProvider
{
    ValueTask<ProviderValidationResult> ValidateReportAsync(FireReportSubmission report, CancellationToken cancellationToken = default);
    ValueTask<ProviderOperationResult> SubmitReportAsync(ProviderOperationContext context, FireReportSubmission report, CancellationToken cancellationToken = default);
}
