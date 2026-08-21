using DISRoute.Application.Abstractions; using DISRoute.Domain.Incidents;
namespace DISRoute.Data;
public sealed class InMemoryIncidentRepository : IIncidentRepository
{
    private readonly List<Incident> _items=[new(Guid.Parse("11111111-1111-1111-1111-111111111111"),"26-000001","Medical","100 Main St",DateTimeOffset.UtcNow,IncidentStatus.Open)];
    public Task<IReadOnlyList<Incident>> GetOpenAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<Incident>>(_items.Where(x=>x.Status!=IncidentStatus.Closed).ToArray());
    public Task<Incident?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult(_items.SingleOrDefault(x=>x.Id==id));
}
