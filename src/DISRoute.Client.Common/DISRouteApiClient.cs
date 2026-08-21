using System.Net.Http.Json; using DISRoute.Contracts.Identity; using DISRoute.Contracts.Incidents;
namespace DISRoute.Client.Common;
public sealed class DISRouteApiClient(HttpClient http)
{
    public Task<CurrentUserDto?> GetCurrentUserAsync(CancellationToken ct=default)=>http.GetFromJsonAsync<CurrentUserDto>("api/me",ct);
    public async Task<IReadOnlyList<IncidentSummaryDto>> GetOpenIncidentsAsync(CancellationToken ct=default)=>await http.GetFromJsonAsync<IncidentSummaryDto[]>("api/incidents/open",ct) ?? [];
}
