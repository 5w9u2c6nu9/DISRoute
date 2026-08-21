namespace DISRoute.Contracts.Operational;

public sealed record AgencyDto(Guid Id, string Code, string Name);
public sealed record StationDto(Guid Id, Guid AgencyId, string Code, string Name, string Address);
public sealed record UnitDto(Guid Id, Guid AgencyId, Guid StationId, string CallSign, string Discipline, string UnitType, string Status, Guid? IncidentId, DateTimeOffset UpdatedAtUtc);
public sealed record IncidentDto(
    Guid Id,
    Guid AgencyId,
    string Number,
    string Nature,
    string Priority,
    string Address,
    string CrossStreets,
    string Municipality,
    string CallerName,
    string CallerPhone,
    string Notes,
    string Status,
    Guid StationId,
    long Version,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc);
public sealed record IncidentActivityDto(Guid Id, Guid IncidentId, string Category, string Message, string Actor, DateTimeOffset OccurredAtUtc);
public sealed record CreateIncidentRequest(Guid AgencyId, Guid StationId, string Nature, string Priority, string Address, string CrossStreets, string Municipality, string CallerName, string CallerPhone, string Notes, string? IdempotencyKey);
public sealed record UpdateIncidentRequest(long ExpectedVersion, string Nature, string Priority, string Address, string CrossStreets, string Municipality, string CallerName, string CallerPhone, string Notes, string? IdempotencyKey);
public sealed record AssignUnitRequest(Guid UnitId, long ExpectedIncidentVersion, bool AllowReassignment, string? IdempotencyKey);
public sealed record SetUnitStatusRequest(string Status, string? IdempotencyKey);
public sealed record CloseIncidentRequest(long ExpectedVersion, string Reason, string? IdempotencyKey);
public sealed record ReopenIncidentRequest(string Reason, string? IdempotencyKey);
public sealed record AddNarrativeRequest(string Text, string? IdempotencyKey);

public sealed record ModuleStateDto(string Key, string DisplayName, bool Enabled, bool Core, DateTimeOffset UpdatedAtUtc);
public sealed record SetModuleStateRequest(bool Enabled);
public sealed record CapabilityDto(string Key, string State, string? Detail);

public sealed record RoadRestrictionDto(Guid Id, Guid AgencyId, string Name, string Description, double Latitude, double Longitude, double RadiusMeters, DateTimeOffset StartsAtUtc, DateTimeOffset? EndsAtUtc, bool Active);
public sealed record CreateRoadRestrictionRequest(Guid AgencyId, string Name, string Description, double Latitude, double Longitude, double RadiusMeters, DateTimeOffset StartsAtUtc, DateTimeOffset? EndsAtUtc);

public sealed record MessageDto(Guid Id, Guid AgencyId, Guid? IncidentId, string Channel, string Sender, string Text, DateTimeOffset SentAtUtc);
public sealed record SendMessageRequest(Guid AgencyId, Guid? IncidentId, string Channel, string Sender, string Text, string? IdempotencyKey = null);

public sealed record WorkforceShiftDto(Guid Id, Guid AgencyId, DateOnly Date, string ShiftName, Guid StationId, string Position, string EmployeeName, string Qualification, string Status);
public sealed record CreateWorkforceShiftRequest(Guid AgencyId, DateOnly Date, string ShiftName, Guid StationId, string Position, string EmployeeName, string Qualification, string Status);

public sealed record DocumentDto(Guid Id, Guid AgencyId, string Title, string Classification, int Version, string Status, string Content, string CreatedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateDocumentRequest(Guid AgencyId, string Title, string Classification, string Content, string CreatedBy);
public sealed record PublishDocumentRequest(string Actor);

public sealed record RmsRecordDto(Guid Id, Guid AgencyId, string RecordType, string Number, string Title, string Status, string Summary, Guid? IncidentId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateRmsRecordRequest(Guid AgencyId, string RecordType, string Title, string Summary, Guid? IncidentId);

public sealed record SearchResultDto(string Kind, Guid Id, string Primary, string Secondary, DateTimeOffset UpdatedAtUtc);
public sealed record ReplayEventDto(Guid Id, Guid AgencyId, Guid? IncidentId, string Stream, string Kind, string Summary, string Actor, DateTimeOffset OccurredAtUtc);
public sealed record SystemStatusDto(string Service, string Status, string Database, string Realtime, int ActiveIncidents, int ConnectedClients, DateTimeOffset AsOfUtc);

// Historical state contracts used by Replay. These are immutable snapshots captured when an
// authoritative operational state changes; Replay never substitutes today's projection for a
// historical value when a snapshot exists.
public sealed record IncidentHistoryEntryDto(Guid Id, Guid IncidentId, long Version, string Cause, string Actor, IncidentDto State, DateTimeOffset RecordedAtUtc);
public sealed record UnitHistoryEntryDto(Guid Id, Guid UnitId, string Cause, string Actor, UnitDto State, DateTimeOffset RecordedAtUtc);
public sealed record DispatchActionHistoryEntryDto(Guid Id, Guid DispatchActionId, Guid IncidentId, string Cause, string Actor, DispatchActionDto State, DateTimeOffset RecordedAtUtc);
public sealed record DispatchDeliveryHistoryEntryDto(Guid Id, Guid DeliveryId, Guid IncidentId, string Cause, string Actor, DispatchDeliveryDto State, DateTimeOffset RecordedAtUtc);
public sealed record StationDeliveryHistoryEntryDto(Guid Id, Guid DeliveryId, Guid IncidentId, string Cause, string Actor, StationDeliveryDto State, DateTimeOffset RecordedAtUtc);
public sealed record AlertHistoryEntryDto(Guid Id, Guid AlertId, Guid? IncidentId, string Cause, string Actor, AlertDto State, DateTimeOffset RecordedAtUtc);

public sealed record ReplayAvlStateDto(AvlObservationDto Observation, double AgeSeconds, bool Fresh);
public sealed record ReplayKnowledgeItemDto(Guid Id, string Classification, string Stream, string Kind, string Summary, string Actor, DateTimeOffset EffectiveAtUtc, DateTimeOffset RecordedAtUtc);
public sealed record ReplaySnapshotDto(
    Guid AgencyId,
    Guid IncidentId,
    DateTimeOffset AtUtc,
    IncidentDto Incident,
    IReadOnlyList<UnitDto> Units,
    IReadOnlyList<IncidentActivityDto> Activity,
    IReadOnlyList<ReplayAvlStateDto> Avl,
    IReadOnlyList<VesselObservationDto> Ais,
    IReadOnlyList<RoadRestrictionDto> RoadRestrictions,
    IReadOnlyList<MapObjectDto> MapObjects,
    IReadOnlyList<DispatchActionDto> DispatchActions,
    IReadOnlyList<DispatchDeliveryDto> DispatchDeliveries,
    IReadOnlyList<StationDeliveryDto> StationDeliveries,
    IReadOnlyList<RunSheetDto> RunSheets,
    IReadOnlyList<PreplanDto> Preplans,
    IReadOnlyList<CrgAssetDto> CrgAssets,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<OperationalMessageDto> OperationalMessages,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<CommandAssignmentDto> Commands,
    IReadOnlyList<OperationalGroupDto> Groups,
    IReadOnlyList<MutualAidRequestDto> MutualAid,
    IReadOnlyList<MaydayEventDto> Maydays,
    IReadOnlyList<IncidentCorrectionDto> CorrectedLater,
    IReadOnlyList<ReplayKnowledgeItemDto> Timeline,
    IReadOnlyList<string> Limitations);
