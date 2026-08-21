namespace DISRoute.Domain.Preplans;
public sealed record Preplan(Guid Id,string PremiseKey,string Address,string? BuildingName,string? Summary,IReadOnlyList<PreplanAsset> Assets,DateTimeOffset UpdatedAtUtc);
public sealed record PreplanAsset(Guid Id,string Kind,string Name,string Uri,string? Notes);
