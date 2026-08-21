namespace DISRoute.Domain.Stations;
public sealed record Station(Guid Id,string Name,string Code,string Address,double? Latitude,double? Longitude);
