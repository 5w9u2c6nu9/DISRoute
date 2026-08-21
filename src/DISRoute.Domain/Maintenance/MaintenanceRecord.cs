namespace DISRoute.Domain.Maintenance;
public sealed record MaintenanceRecord(Guid Id,Guid AssetId,string AssetType,string Status,string Description,DateTimeOffset OpenedAtUtc,DateTimeOffset? ClosedAtUtc);
