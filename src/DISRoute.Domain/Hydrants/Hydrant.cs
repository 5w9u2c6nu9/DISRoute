namespace DISRoute.Domain.Hydrants;
public sealed record Hydrant(Guid Id,double Latitude,double Longitude,string Status,int? FlowGpm,string? MainSize,string? Notes,DateTimeOffset? LastInspectedUtc);
