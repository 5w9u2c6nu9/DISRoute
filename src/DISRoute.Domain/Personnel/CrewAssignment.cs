namespace DISRoute.Domain.Personnel;
public sealed record CrewAssignment(Guid UnitId,Guid PersonnelId,string Position,DateTimeOffset StartsAtUtc,DateTimeOffset? EndsAtUtc);
