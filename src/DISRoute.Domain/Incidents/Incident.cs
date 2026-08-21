namespace DISRoute.Domain.Incidents;
public sealed record Incident(Guid Id, string Number, string Nature, string Address, DateTimeOffset CreatedAtUtc, IncidentStatus Status);
public enum IncidentStatus { Open, Dispatched, EnRoute, OnScene, Closed }
