using DISRoute.Contracts.Preplans;
namespace DISRoute.Contracts.Stations;
public sealed record StationStatusDto(Guid StationId,string StationName,IReadOnlyList<StationUnitDto> Units,IReadOnlyList<StationIncidentDto> ActiveIncidents,PreplanSummaryDto? RelevantPreplan,IReadOnlyList<string> EnabledPanels);
public sealed record StationUnitDto(Guid Id,string Name,string Type,string Status);
public sealed record StationIncidentDto(Guid Id,string Number,string Nature,string Address,string Status,DateTimeOffset CreatedAtUtc,double? Latitude,double? Longitude);
