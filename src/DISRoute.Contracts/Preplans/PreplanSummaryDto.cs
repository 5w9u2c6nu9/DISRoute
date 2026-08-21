namespace DISRoute.Contracts.Preplans;
public sealed record PreplanSummaryDto(Guid Id,string Address,string? BuildingName,string? Summary,IReadOnlyList<PreplanAssetDto> Assets);
public sealed record PreplanAssetDto(string Kind,string Name,string Uri,string? Notes);
