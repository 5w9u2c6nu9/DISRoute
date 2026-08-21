using System.Text.Json;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;

namespace DISRoute.Mobile.Core;

public sealed class MobileSession
{
    private readonly FeatureApiClient _api;
    private readonly OperationalApiClient? _operations;
    private readonly OfflineOperationStore _queue;
    private readonly MobileCacheStore? _cache;
    private readonly Guid _agencyId;
    private readonly string _deviceId;
    private DateTimeOffset? _lastSyncUtc;
    private string? _offlineAuthorizationLease;

    public MobileSession(FeatureApiClient api, OfflineOperationStore queue, Guid agencyId, string deviceId, MobileCacheStore? cache = null, OperationalApiClient? operations = null)
    {
        _api = api;
        _operations = operations;
        _queue = queue;
        _agencyId = agencyId;
        _deviceId = string.IsNullOrWhiteSpace(deviceId) ? throw new ArgumentException("Device ID is required.", nameof(deviceId)) : deviceId.Trim();
        _cache = cache;
        RestoreCachedSnapshot();
    }

    public IReadOnlyList<IncidentDto> Incidents { get; private set; } = [];
    public IReadOnlyList<UnitDto> Units { get; private set; } = [];
    public IReadOnlyList<PreplanDto> Preplans { get; private set; } = [];
    public IReadOnlyList<CrgAssetDto> CrgAssets { get; private set; } = [];
    public IReadOnlyList<MessageDto> Messages { get; private set; } = [];
    public MobileOperationalPackageDto? OperationalPackage { get; private set; }
    public string ConnectionState { get; private set; } = "Offline";
    public string? ConnectionDetail { get; private set; }
    public DateTimeOffset? LastSuccessfulSyncUtc => _lastSyncUtc;
    public DateTimeOffset? CachedAtUtc { get; private set; }
    public int PendingOperationCount => _queue.Items.Count;
    public IReadOnlyList<QueuedMobileOperationDto> PendingOperations => _queue.Items;
    public IReadOnlyList<MobileOperationResultDto> LastOperationResults { get; private set; } = [];
    public Guid? SelectedUnitId { get; set; }

    public void SetOfflineAuthorizationLease(string? token) => _offlineAuthorizationLease = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    public Task<MobileSyncResponseDto> SyncAsync(CancellationToken cancellationToken = default) => SyncCoreAsync(true, cancellationToken);
    public Task<MobileSyncResponseDto> RefreshReadOnlyAsync(CancellationToken cancellationToken = default) => SyncCoreAsync(false, cancellationToken);

    private async Task<MobileSyncResponseDto> SyncCoreAsync(bool reconcileQueuedOperations, CancellationToken cancellationToken)
    {
        try
        {
            var queued = reconcileQueuedOperations ? _queue.Items : Array.Empty<QueuedMobileOperationDto>();
            var response = await _api.MobileSyncAsync(new MobileSyncRequest(_agencyId, _deviceId, _lastSyncUtc, queued, SelectedUnitId, _offlineAuthorizationLease), cancellationToken);
            if (reconcileQueuedOperations)
            {
                LastOperationResults = response.OperationResults;
                _queue.ApplyResults(response.OperationResults);
            }
            ApplyResponse(response);
            _lastSyncUtc = response.ServerTimeUtc;
            CachedAtUtc = DateTimeOffset.UtcNow;
            ConnectionState = "Online";
            ConnectionDetail = null;
            _cache?.Save(response);
            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            ConnectionState = CachedAtUtc is null ? "Offline" : "Stale";
            ConnectionDetail = ex.Message;
            throw;
        }
    }

    public async Task<UnitDto> SetUnitStatusOnlineAsync(UnitDto unit, string status, CancellationToken cancellationToken = default)
    {
        if (_operations is null) throw new InvalidOperationException("Online unit status transport is not configured for this mobile session.");
        var updated = await _operations.SetUnitStatusAsync(unit.Id, new SetUnitStatusRequest(status, $"mobile-online:{Guid.NewGuid():N}"), cancellationToken);
        return updated;
    }

    public async Task<OfflineAuthorizationLeaseDto> AcquireOfflineUnitStatusLeaseAsync(string userId, int validMinutes = 30, CancellationToken cancellationToken = default)
    {
        var lease = await _api.IssueOfflineAuthorizationLeaseAsync(new IssueOfflineAuthorizationLeaseRequest(_agencyId, userId, _deviceId, ["unit.change-status"], validMinutes), cancellationToken);
        SetOfflineAuthorizationLease(lease.Token);
        return lease;
    }


    public bool DiscardQueuedOperation(Guid operationId) => _queue.Remove(operationId);

    public QueuedMobileOperationDto QueueNarrative(Guid incidentId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Narrative text is required.", nameof(text));
        return _queue.Enqueue("Narrative", incidentId, JsonSerializer.Serialize(new MobileNarrativePayload(text.Trim())));
    }

    public QueuedMobileOperationDto QueueMessage(Guid incidentId, string channel, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Message text is required.", nameof(text));
        return _queue.Enqueue("Message", incidentId, JsonSerializer.Serialize(new MobileMessagePayload(string.IsNullOrWhiteSpace(channel) ? "Mobile" : channel.Trim(), text.Trim())));
    }


    public QueuedMobileOperationDto QueueChatMessage(Guid channelId, Guid? incidentId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Chat message text is required.", nameof(text));
        return _queue.Enqueue("ChatMessage", incidentId, JsonSerializer.Serialize(new MobileChatPayload(channelId, text.Trim())));
    }

    public QueuedMobileOperationDto QueueUnitStatus(UnitDto unit, string toStatus)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (string.IsNullOrWhiteSpace(_offlineAuthorizationLease)) throw new InvalidOperationException("A valid offline authorization lease is required before a unit status can be queued offline.");
        var payload = new MobileUnitStatusPayload(unit.Id, unit.Status, toStatus, unit.UpdatedAtUtc);
        return _queue.Enqueue("UnitStatus", unit.IncidentId, JsonSerializer.Serialize(payload));
    }

    private void RestoreCachedSnapshot()
    {
        var cached = _cache?.Load();
        if (cached is null) return;
        CachedAtUtc = cached.CachedAtUtc;
        ApplyResponse(cached.Response);
        _lastSyncUtc = cached.Response.ServerTimeUtc;
        ConnectionState = "Stale";
        ConnectionDetail = "Showing encrypted cached data until the server is reachable.";
    }

    private void ApplyResponse(MobileSyncResponseDto response)
    {
        Incidents = response.Incidents;
        Units = response.Units;
        Preplans = response.Preplans;
        CrgAssets = response.CrgAssets;
        Messages = response.Messages;
        OperationalPackage = response.OperationalPackage;
        if (response.OperationalPackage?.SelectedUnitId is Guid unitId) SelectedUnitId = unitId;
    }
}
