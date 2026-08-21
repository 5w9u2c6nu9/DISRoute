namespace DISRoute.Contracts.Video;
public sealed record CameraDto(Guid Id,Guid StationId,string Name,string Status,bool OnvifEnabled,string? LogicalSlot,int DisplayOrder);
