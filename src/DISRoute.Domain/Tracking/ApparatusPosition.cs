namespace DISRoute.Domain.Tracking;
public sealed record ApparatusPosition(Guid UnitId,double Latitude,double Longitude,double? Heading,double? SpeedKph,DateTimeOffset ObservedAtUtc);
