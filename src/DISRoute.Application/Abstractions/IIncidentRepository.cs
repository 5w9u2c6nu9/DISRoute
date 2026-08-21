using DISRoute.Domain.Incidents;
namespace DISRoute.Application.Abstractions;
public interface IIncidentRepository { Task<IReadOnlyList<Incident>> GetOpenAsync(CancellationToken ct); Task<Incident?> GetAsync(Guid id,CancellationToken ct); }
