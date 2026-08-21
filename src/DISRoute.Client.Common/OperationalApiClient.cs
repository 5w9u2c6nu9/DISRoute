using System.Net.Http.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Client.Common;

public sealed class OperationalApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken ct = default) => await http.GetFromJsonAsync<StationDto[]>("api/stations", ct) ?? [];
    public async Task<IReadOnlyList<UnitDto>> GetUnitsAsync(CancellationToken ct = default) => await http.GetFromJsonAsync<UnitDto[]>("api/units", ct) ?? [];
    public async Task<IReadOnlyList<IncidentDto>> GetIncidentsAsync(bool openOnly = false, string? query = null, CancellationToken ct = default)
    {
        var path = $"api/incidents?openOnly={openOnly.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(query)) path += "&query=" + Uri.EscapeDataString(query.Trim());
        return await http.GetFromJsonAsync<IncidentDto[]>(path, ct) ?? [];
    }
    public Task<IncidentDto?> GetIncidentAsync(Guid id, CancellationToken ct = default) => http.GetFromJsonAsync<IncidentDto>($"api/incidents/{id:D}", ct);
    public async Task<IReadOnlyList<IncidentActivityDto>> GetActivityAsync(Guid id, CancellationToken ct = default) => await http.GetFromJsonAsync<IncidentActivityDto[]>($"api/incidents/{id:D}/activity", ct) ?? [];
    public async Task<IncidentDto> CreateIncidentAsync(CreateIncidentRequest request, CancellationToken ct = default) => await PostAsync<IncidentDto>("api/incidents", request, ct);
    public async Task<IncidentDto> UpdateIncidentAsync(Guid id, UpdateIncidentRequest request, CancellationToken ct = default) => await PutAsync<IncidentDto>($"api/incidents/{id:D}", request, ct);
    public async Task<IncidentDto> AssignUnitAsync(Guid id, AssignUnitRequest request, CancellationToken ct = default) => await PostAsync<IncidentDto>($"api/incidents/{id:D}/assign", request, ct);
    public async Task<IncidentDto> CloseIncidentAsync(Guid id, CloseIncidentRequest request, CancellationToken ct = default) => await PostAsync<IncidentDto>($"api/incidents/{id:D}/close", request, ct);
    public async Task<IncidentDto> ReopenIncidentAsync(Guid id, ReopenIncidentRequest request, CancellationToken ct = default) => await PostAsync<IncidentDto>($"api/incidents/{id:D}/reopen", request, ct);
    public async Task<UnitDto> SetUnitStatusAsync(Guid id, SetUnitStatusRequest request, CancellationToken ct = default) => await PostAsync<UnitDto>($"api/units/{id:D}/status", request, ct);
    public async Task<IReadOnlyList<ModuleStateDto>> GetModulesAsync(CancellationToken ct = default) => await http.GetFromJsonAsync<ModuleStateDto[]>("api/modules", ct) ?? [];
    public async Task<IReadOnlyList<SearchResultDto>> SearchAsync(string query, CancellationToken ct = default) => await http.GetFromJsonAsync<SearchResultDto[]>("api/search?q=" + Uri.EscapeDataString(query), ct) ?? [];
    public async Task<IReadOnlyList<RmsIncidentReportDto>> GetRmsIncidentReportsAsync(Guid? incidentId = null, CancellationToken ct = default)
    {
        var path = incidentId is Guid id ? $"api/rms/incident-reports?incidentId={id:D}" : "api/rms/incident-reports";
        return await http.GetFromJsonAsync<RmsIncidentReportDto[]>(path, ct) ?? [];
    }
    public Task<RmsIncidentReportDto?> GetRmsIncidentReportAsync(Guid id, CancellationToken ct = default) => http.GetFromJsonAsync<RmsIncidentReportDto>($"api/rms/incident-reports/{id:D}", ct);
    public async Task<IReadOnlyList<RmsReportVersionDto>> GetRmsReportVersionsAsync(Guid id, CancellationToken ct = default) => await http.GetFromJsonAsync<RmsReportVersionDto[]>($"api/rms/incident-reports/{id:D}/versions", ct) ?? [];
    public async Task<IReadOnlyList<RmsAttachmentReferenceDto>> GetRmsAttachmentsAsync(Guid id, CancellationToken ct = default) => await http.GetFromJsonAsync<RmsAttachmentReferenceDto[]>($"api/rms/incident-reports/{id:D}/attachments", ct) ?? [];
    public async Task<RmsValidationResultDto> GetRmsValidationAsync(Guid id, CancellationToken ct = default) => await http.GetFromJsonAsync<RmsValidationResultDto>($"api/rms/incident-reports/{id:D}/validation", ct) ?? throw new InvalidOperationException("RMS validation response was empty.");
    public Task<RmsIncidentReportDto> CreateRmsFromIncidentAsync(Guid incidentId, CreateRmsFromIncidentRequest request, CancellationToken ct = default) => PostAsync<RmsIncidentReportDto>($"api/rms/incident-reports/from-incident/{incidentId:D}", request, ct);
    public Task<RmsIncidentReportDto> UpdateRmsIncidentReportAsync(Guid id, UpdateRmsIncidentReportRequest request, CancellationToken ct = default) => PutAsync<RmsIncidentReportDto>($"api/rms/incident-reports/{id:D}", request, ct);
    public Task<RmsIncidentReportDto> RefreshRmsFromCadAsync(Guid id, RefreshRmsCadSnapshotRequest request, CancellationToken ct = default) => PostAsync<RmsIncidentReportDto>($"api/rms/incident-reports/{id:D}/refresh-cad", request, ct);
    public Task<RmsIncidentReportDto> SubmitRmsForReviewAsync(Guid id, SubmitRmsForReviewRequest request, CancellationToken ct = default) => PostAsync<RmsIncidentReportDto>($"api/rms/incident-reports/{id:D}/submit-review", request, ct);
    public Task<RmsIncidentReportDto> ReviewRmsAsync(Guid id, ReviewRmsIncidentReportRequest request, CancellationToken ct = default) => PostAsync<RmsIncidentReportDto>($"api/rms/incident-reports/{id:D}/review", request, ct);
    public Task<RmsIncidentReportDto> AmendRmsAsync(Guid id, AmendRmsIncidentReportRequest request, CancellationToken ct = default) => PostAsync<RmsIncidentReportDto>($"api/rms/incident-reports/{id:D}/amend", request, ct);
    public Task<RmsAttachmentReferenceDto> AddRmsAttachmentAsync(Guid id, AddRmsAttachmentReferenceRequest request, CancellationToken ct = default) => PostAsync<RmsAttachmentReferenceDto>($"api/rms/incident-reports/{id:D}/attachments", request, ct);

    public async Task<IReadOnlyList<NerisSubmissionDto>> GetNerisSubmissionsAsync(Guid? rmsReportId = null, CancellationToken ct = default)
    {
        var path = rmsReportId is Guid id ? $"api/rms/neris-submissions?rmsReportId={id:D}" : "api/rms/neris-submissions";
        return await http.GetFromJsonAsync<NerisSubmissionDto[]>(path, ct) ?? [];
    }
    public Task<NerisSubmissionDto?> GetNerisSubmissionAsync(Guid id, CancellationToken ct = default) => http.GetFromJsonAsync<NerisSubmissionDto>($"api/rms/neris-submissions/{id:D}", ct);
    public async Task<IReadOnlyList<NerisSubmissionAttemptDto>> GetNerisSubmissionAttemptsAsync(Guid id, CancellationToken ct = default) => await http.GetFromJsonAsync<NerisSubmissionAttemptDto[]>($"api/rms/neris-submissions/{id:D}/attempts", ct) ?? [];
    public Task<NerisSubmissionDto> BuildNerisSubmissionAsync(Guid rmsReportId, BuildNerisSubmissionRequest request, CancellationToken ct = default) => PostAsync<NerisSubmissionDto>($"api/rms/incident-reports/{rmsReportId:D}/neris/build", request, ct);
    public async Task<NerisProviderValidationDto> ValidateNerisProviderAsync(Guid id, string? providerKey, CancellationToken ct = default)
    {
        var path = $"api/rms/neris-submissions/{id:D}/provider-validation";
        if (!string.IsNullOrWhiteSpace(providerKey)) path += "?providerKey=" + Uri.EscapeDataString(providerKey.Trim());
        using var response = await http.PostAsync(path, null, ct);
        return await ReadAsync<NerisProviderValidationDto>(response, ct);
    }
    public Task<NerisSubmissionDto> QueueNerisSubmissionAsync(Guid id, QueueNerisSubmissionRequest request, CancellationToken ct = default) => PostAsync<NerisSubmissionDto>($"api/rms/neris-submissions/{id:D}/queue", request, ct);
    public Task<NerisSubmissionDto> RetryNerisSubmissionAsync(Guid id, RetryNerisSubmissionRequest request, CancellationToken ct = default) => PostAsync<NerisSubmissionDto>($"api/rms/neris-submissions/{id:D}/retry", request, ct);
    public Task<NerisSubmissionDto> ReconcileNerisSubmissionAsync(Guid id, ReconcileNerisSubmissionRequest request, CancellationToken ct = default) => PostAsync<NerisSubmissionDto>($"api/rms/neris-submissions/{id:D}/reconcile", request, ct);

    public async Task<IReadOnlyList<ReplayEventDto>> GetReplayAsync(Guid? incidentId = null, CancellationToken ct = default)
    {
        var path = incidentId is Guid id ? $"api/replay?incidentId={id:D}" : "api/replay";
        return await http.GetFromJsonAsync<ReplayEventDto[]>(path, ct) ?? [];
    }

    public async Task<ReplaySnapshotDto> GetReplaySnapshotAsync(Guid incidentId, DateTimeOffset? atUtc = null, CancellationToken ct = default)
    {
        var path = $"api/replay/incidents/{incidentId:D}/snapshot";
        if (atUtc is DateTimeOffset at) path += $"?atUtc={Uri.EscapeDataString(at.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture))}";
        return await http.GetFromJsonAsync<ReplaySnapshotDto>(path, ct) ?? throw new InvalidOperationException("Replay snapshot response was empty.");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, body, ct);
        return await ReadAsync<T>(response, ct);
    }
    private async Task<T> PutAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync(path, body, ct);
        return await ReadAsync<T>(response, ct);
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;
        var error = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"DISRoute API returned {(int)response.StatusCode}: {error}");
    }
}
