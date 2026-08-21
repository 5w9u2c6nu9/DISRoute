namespace DISRoute.Domain.Equipment;
public sealed record EquipmentAsset(Guid Id,string AssetTag,string Name,string Category,Guid? AssignedUnitId,string Status);
