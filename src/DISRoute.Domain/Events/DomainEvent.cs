namespace DISRoute.Domain.Events;
public abstract record DomainEvent(Guid EventId,DateTimeOffset OccurredAtUtc);
public sealed record IncidentCreated(Guid EventId,DateTimeOffset OccurredAtUtc,Guid IncidentId):DomainEvent(EventId,OccurredAtUtc);
public sealed record UnitStatusChanged(Guid EventId,DateTimeOffset OccurredAtUtc,Guid UnitId,string OldStatus,string NewStatus,Guid? IncidentId):DomainEvent(EventId,OccurredAtUtc);
public sealed record UnitAssigned(Guid EventId,DateTimeOffset OccurredAtUtc,Guid UnitId,Guid IncidentId):DomainEvent(EventId,OccurredAtUtc);
public sealed record CameraOffline(Guid EventId,DateTimeOffset OccurredAtUtc,Guid CameraId):DomainEvent(EventId,OccurredAtUtc);
public sealed record StationDispatched(Guid EventId,DateTimeOffset OccurredAtUtc,Guid StationId,Guid IncidentId):DomainEvent(EventId,OccurredAtUtc);
