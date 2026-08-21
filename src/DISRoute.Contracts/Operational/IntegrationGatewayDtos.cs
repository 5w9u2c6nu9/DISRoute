namespace DISRoute.Contracts.Operational;

public sealed record IntegrationEventDto(
    Guid Id, Guid AgencyId, string ProviderKey, string Category, string NativeEventId, string EventType,
    string State, Guid? IncidentId, Guid? UnitId, string? ResourceKey, DateTimeOffset SourceTimestamp,
    DateTimeOffset ReceivedAtUtc, string PayloadSha256, string PayloadJson, string CorrelationKey, string? Error);

public sealed record IntegrationOperationDto(
    Guid Id, Guid AgencyId, string ProviderKey, string Kind, string State, Guid? IncidentId, Guid? UnitId,
    string RequestJson, string RequestSha256, int AttemptCount, string? ProviderReference, string? Error,
    bool PossibleDuplicate, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public sealed record IntegrationAttemptDto(
    Guid Id, Guid OperationId, int AttemptNumber, Guid ProviderOperationId, string State,
    string? ProviderReference, string? Error, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);

public sealed record Ingest911GatewayEventRequest(
    Guid AgencyId, string ProviderKey, string ProviderCallId, string EventState, string CallerName,
    string CallbackNumber, string SuppliedLocation, string Nature, DateTimeOffset SourceTimestamp,
    IReadOnlyDictionary<string,string>? NativeData = null);

public sealed record IngestRadioGatewayEventRequest(
    Guid AgencyId, string ProviderKey, string NativeEventId, string EventType, string NativeRadioId,
    string Channel, Guid? UnitId, string? Text, DateTimeOffset SourceTimestamp,
    IReadOnlyDictionary<string,string>? NativeData = null);

public sealed record IngestAisGatewayEventRequest(
    Guid AgencyId, string ProviderKey, string NativeEventId, string Mmsi, string? Name,
    double Latitude, double Longitude, double? SpeedKnots, double? CourseDegrees,
    DateTimeOffset SourceTimestamp, IReadOnlyDictionary<string,string>? NativeData = null);

public sealed record ProviderRouteRequest(
    Guid AgencyId, double FromLatitude, double FromLongitude, double ToLatitude, double ToLongitude,
    Guid? UnitId, Guid? IncidentId, bool AvoidRestrictions, string VehicleProfile, string? ProviderKey = null);

public sealed record ProviderRouteResultDto(
    Guid OperationId, string ProviderKey, double DistanceMeters, int DurationSeconds, string GeometryGeoJson,
    IReadOnlyList<string> Warnings, DateTimeOffset CalculatedAtUtc);

public sealed record RecordingLookupRequest(
    Guid AgencyId, Guid? IncidentId, string? Channel, string? TelephoneNumber,
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, string? ProviderKey = null);

public sealed record RecordingReferenceDto(
    Guid Id, Guid AgencyId, Guid? IncidentId, string ProviderKey, string ProviderReference, string MediaType,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Description, string? PlaybackUri, DateTimeOffset LinkedAtUtc);

public sealed record CameraDiscoveryRequest(Guid AgencyId, Guid? StationId, string? ProviderKey = null);
public sealed record CameraDiscoveryCandidateDto(
    string ProviderKey, string NativeId, string Name, string Uri, string? Manufacturer, string? Model,
    string? SerialNumber, string State, DateTimeOffset DiscoveredAtUtc);
public sealed record ImportDiscoveredCameraRequest(Guid AgencyId, Guid? StationId, Guid? PremiseId, CameraDiscoveryCandidateDto Candidate);

public sealed record RadioEmergencyClearRequest(Guid AgencyId, Guid? IncidentId, Guid? UnitId, string NativeRadioId, string? ProviderKey, string? IdempotencyKey);
public sealed record RetryIntegrationOperationRequest(bool ConfirmPossibleDuplicate, string? IdempotencyKey);
public sealed record ReconcileIntegrationOperationRequest(string Resolution, string? ProviderReference, string? Notes);

public sealed record IntegrationDashboardDto(
    IReadOnlyList<ProviderHealthDto> Providers,
    IReadOnlyList<IntegrationEventDto> RecentEvents,
    IReadOnlyList<IntegrationOperationDto> Operations,
    IReadOnlyList<RecordingReferenceDto> Recordings,
    IReadOnlyList<CameraFeedDto> Cameras,
    IReadOnlyList<VesselObservationDto> Vessels);
