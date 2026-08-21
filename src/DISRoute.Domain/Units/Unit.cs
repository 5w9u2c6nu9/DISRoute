namespace DISRoute.Domain.Units;
public sealed record Unit(Guid Id, string CallSign, string Discipline, UnitStatus Status);
public enum UnitStatus { Available, Assigned, EnRoute, OnScene, Transporting, AtHospital, OutOfService }
