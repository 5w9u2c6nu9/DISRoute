namespace DISRoute.Domain.Hospitals;
public sealed record HospitalDestination(Guid Id,string Name,string Address,double Latitude,double Longitude,IReadOnlyList<string> Capabilities,string? Status);
