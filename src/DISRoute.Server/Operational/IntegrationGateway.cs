using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, IntegrationEventDto> _integrationEvents = [];
    private readonly Dictionary<Guid, IntegrationOperationDto> _integrationOperations = [];
    private readonly List<IntegrationAttemptDto> _integrationAttempts = [];
    private readonly Dictionary<Guid, RecordingReferenceDto> _recordingReferences = [];

    public IReadOnlyList<IntegrationEventDto> IntegrationEvents { get { lock (_gate) return _integrationEvents.Values.OrderByDescending(x => x.ReceivedAtUtc).ToArray(); } }
    public IReadOnlyList<IntegrationOperationDto> IntegrationOperations { get { lock (_gate) return _integrationOperations.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<IntegrationAttemptDto> IntegrationAttempts { get { lock (_gate) return _integrationAttempts.OrderByDescending(x => x.StartedAtUtc).ToArray(); } }
    public IReadOnlyList<RecordingReferenceDto> RecordingReferences { get { lock (_gate) return _recordingReferences.Values.OrderByDescending(x => x.StartUtc).ToArray(); } }

    public IntegrationEventDto Ingest911GatewayEvent(Ingest911GatewayEventRequest request, string actor)
    {
        lock (_gate)
        {
            EnsureActorAgency(actor, request.AgencyId);
            if (string.IsNullOrWhiteSpace(request.ProviderKey) || string.IsNullOrWhiteSpace(request.ProviderCallId)) throw new ArgumentException("Provider key and provider call ID are required.");
            var state = request.EventState.Trim().ToLowerInvariant() switch { "open" or "ringing" => "Open", "answered" or "connected" => "Answered", "ended" or "closed" or "disconnected" => "Ended", _ => throw new ArgumentException("9-1-1 event state must be Open, Answered, or Ended.") };
            var nativeId = $"{request.ProviderCallId.Trim()}:{state}:{request.SourceTimestamp.UtcTicks}";
            var correlation = $"911:{request.ProviderKey.Trim().ToLowerInvariant()}:{nativeId}";
            if (_integrationEvents.Values.FirstOrDefault(x => x.CorrelationKey.Equals(correlation, StringComparison.OrdinalIgnoreCase)) is { } existing) return existing;

            var call = _calls.Values.FirstOrDefault(x => x.AgencyId == request.AgencyId && x.Source.Equals(request.ProviderKey.Trim(), StringComparison.OrdinalIgnoreCase) && x.ProviderCallId.Equals(request.ProviderCallId.Trim(), StringComparison.OrdinalIgnoreCase));
            var now = DateTimeOffset.UtcNow;
            if (call is null)
            {
                call = new CallRecordDto(Guid.NewGuid(), request.AgencyId, request.ProviderKey.Trim(), request.ProviderCallId.Trim(), request.CallerName.Trim(), request.CallbackNumber.Trim(), request.SuppliedLocation.Trim(), request.Nature.Trim(), state, null, request.SourceTimestamp, state == "Answered" ? request.SourceTimestamp : null, state == "Ended" ? request.SourceTimestamp : null);
            }
            else
            {
                call = call with
                {
                    CallerName = string.IsNullOrWhiteSpace(request.CallerName) ? call.CallerName : request.CallerName.Trim(),
                    CallbackNumber = string.IsNullOrWhiteSpace(request.CallbackNumber) ? call.CallbackNumber : request.CallbackNumber.Trim(),
                    SuppliedLocation = string.IsNullOrWhiteSpace(request.SuppliedLocation) ? call.SuppliedLocation : request.SuppliedLocation.Trim(),
                    Nature = string.IsNullOrWhiteSpace(request.Nature) ? call.Nature : request.Nature.Trim(),
                    Disposition = state,
                    AnsweredAtUtc = state == "Answered" ? request.SourceTimestamp : call.AnsweredAtUtc,
                    EndedAtUtc = state == "Ended" ? request.SourceTimestamp : call.EndedAtUtc
                };
            }
            _calls[call.Id] = call;
            var payload = JsonSerializer.Serialize(request);
            var item = NewIntegrationEvent(request.AgencyId, request.ProviderKey, "911", nativeId, state, call.IncidentId, null, request.ProviderCallId, request.SourceTimestamp, payload, correlation, null);
            _integrationEvents[item.Id] = item;
            AuditMutation(request.AgencyId, actor, "Gateway911Event", "IntegrationEvent", item.Id.ToString(), $"{request.ProviderKey} {request.ProviderCallId} {state}");
            return item;
        }
    }

    public IntegrationEventDto IngestRadioGatewayEvent(IngestRadioGatewayEventRequest request, string actor)
    {
        lock (_gate)
        {
            EnsureActorAgency(actor, request.AgencyId);
            if (string.IsNullOrWhiteSpace(request.ProviderKey) || string.IsNullOrWhiteSpace(request.NativeEventId) || string.IsNullOrWhiteSpace(request.EventType)) throw new ArgumentException("Provider key, native event ID, and event type are required.");
            var correlation = $"radio:{request.ProviderKey.Trim().ToLowerInvariant()}:{request.NativeEventId.Trim()}";
            if (_integrationEvents.Values.FirstOrDefault(x => x.CorrelationKey.Equals(correlation, StringComparison.OrdinalIgnoreCase)) is { } existing) return existing;
            Guid? incidentId = null;
            if (request.UnitId is Guid unitId)
            {
                var unit = _core.Units.FirstOrDefault(x => x.Id == unitId) ?? throw new KeyNotFoundException("Unit not found.");
                if (unit.AgencyId != request.AgencyId) throw new UnauthorizedAccessException("Radio event unit belongs to another agency.");
                incidentId = unit.IncidentId;
                if (request.EventType.Equals("Status", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.Text))
                    _core.SetUnitStatus(unitId, new SetUnitStatusRequest(request.Text.Trim(), $"gateway:{correlation}"), $"gateway:{request.ProviderKey.Trim()}");
            }
            if (request.EventType.Equals("Emergency", StringComparison.OrdinalIgnoreCase))
                _ = CreateAlert(new CreateAlertRequest(request.AgencyId, incidentId, "RadioEmergency", "Critical", correlation, request.NativeRadioId.Trim(), $"Radio emergency on {request.Channel.Trim()} from {request.NativeRadioId.Trim()}"), actor);
            var payload = JsonSerializer.Serialize(request);
            var item = NewIntegrationEvent(request.AgencyId, request.ProviderKey, "Radio", request.NativeEventId, request.EventType.Trim(), incidentId, request.UnitId, request.NativeRadioId, request.SourceTimestamp, payload, correlation, null);
            _integrationEvents[item.Id] = item;
            AuditMutation(request.AgencyId, actor, "GatewayRadioEvent", "IntegrationEvent", item.Id.ToString(), $"{request.EventType} {request.NativeRadioId}");
            return item;
        }
    }

    public IntegrationEventDto IngestAisGatewayEvent(IngestAisGatewayEventRequest request, string actor)
    {
        lock (_gate)
        {
            EnsureActorAgency(actor, request.AgencyId);
            var correlation = $"ais:{request.ProviderKey.Trim().ToLowerInvariant()}:{request.NativeEventId.Trim()}";
            if (_integrationEvents.Values.FirstOrDefault(x => x.CorrelationKey.Equals(correlation, StringComparison.OrdinalIgnoreCase)) is { } existing) return existing;
            _ = UpsertVessel(new UpsertVesselObservationRequest(request.AgencyId, request.Mmsi, request.Name ?? string.Empty, request.Latitude, request.Longitude, request.SpeedKnots ?? 0d, request.CourseDegrees ?? 0d, request.ProviderKey, request.SourceTimestamp), actor);
            var payload = JsonSerializer.Serialize(request);
            var item = NewIntegrationEvent(request.AgencyId, request.ProviderKey, "AIS", request.NativeEventId, "Position", null, null, request.Mmsi, request.SourceTimestamp, payload, correlation, null);
            _integrationEvents[item.Id] = item;
            AuditMutation(request.AgencyId, actor, "GatewayAisEvent", "IntegrationEvent", item.Id.ToString(), request.Mmsi);
            return item;
        }
    }

    public IntegrationOperationDto BeginIntegrationOperation(Guid agencyId, string providerKey, string kind, Guid? incidentId, Guid? unitId, object request, string actor)
    {
        lock (_gate)
        {
            EnsureActorAgency(actor, agencyId);
            if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Provider key and operation kind are required.");
            if (incidentId is Guid iid && _core.GetIncident(iid).AgencyId != agencyId) throw new UnauthorizedAccessException("Integration incident belongs to another agency.");
            if (unitId is Guid uid)
            {
                var unit = _core.Units.FirstOrDefault(x => x.Id == uid) ?? throw new KeyNotFoundException("Integration unit not found.");
                if (unit.AgencyId != agencyId) throw new UnauthorizedAccessException("Integration unit belongs to another agency.");
            }
            var json = JsonSerializer.Serialize(request);
            var now = DateTimeOffset.UtcNow;
            var item = new IntegrationOperationDto(Guid.NewGuid(), agencyId, providerKey.Trim(), kind.Trim(), "Pending", incidentId, unitId, json, Sha256(json), 0, null, null, false, now, now);
            _integrationOperations[item.Id] = item;
            AuditMutation(agencyId, actor, "IntegrationOperationCreated", "IntegrationOperation", item.Id.ToString(), $"{kind} via {providerKey}");
            return item;
        }
    }

    public (IntegrationOperationDto Operation, IntegrationAttemptDto Attempt) MarkIntegrationSubmitting(Guid operationId, string actor)
    {
        lock (_gate)
        {
            if (!_integrationOperations.TryGetValue(operationId, out var current)) throw new KeyNotFoundException("Integration operation not found.");
            EnsureActorAgency(actor, current.AgencyId);
            if (current.State is "Confirmed" or "Reconciled") throw new InvalidOperationException("Completed integration operation cannot be submitted again.");
            var attemptNumber = current.AttemptCount + 1;
            var operationGuid = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var attempt = new IntegrationAttemptDto(Guid.NewGuid(), operationId, attemptNumber, operationGuid, "Submitting", null, null, now, null);
            _integrationAttempts.Add(attempt);
            var updated = current with { State = "Submitting", AttemptCount = attemptNumber, Error = null, UpdatedAtUtc = now };
            _integrationOperations[operationId] = updated;
            AuditMutation(updated.AgencyId, actor, "IntegrationOperationSubmitting", "IntegrationOperation", operationId.ToString(), $"{updated.Kind} attempt {attemptNumber} via {updated.ProviderKey}");
            return (updated, attempt);
        }
    }

    public IntegrationOperationDto CompleteIntegrationAttempt(Guid operationId, Guid attemptId, string state, string? providerReference, string? error, string actor)
    {
        lock (_gate)
        {
            if (!_integrationOperations.TryGetValue(operationId, out var current)) throw new KeyNotFoundException("Integration operation not found.");
            EnsureActorAgency(actor, current.AgencyId);
            var index = _integrationAttempts.FindIndex(x => x.Id == attemptId);
            if (index < 0) throw new KeyNotFoundException("Integration attempt not found.");
            var normalized = state.Equals("Confirmed", StringComparison.OrdinalIgnoreCase) || state.Equals("Accepted", StringComparison.OrdinalIgnoreCase) || state.Equals("Printed", StringComparison.OrdinalIgnoreCase) ? "Confirmed" : state.Equals("Failed", StringComparison.OrdinalIgnoreCase) ? "Failed" : "NeedsReconciliation";
            if (normalized == "Confirmed" && string.IsNullOrWhiteSpace(providerReference)) normalized = "NeedsReconciliation";
            var now = DateTimeOffset.UtcNow;
            _integrationAttempts[index] = _integrationAttempts[index] with { State = normalized, ProviderReference = providerReference, Error = error, CompletedAtUtc = now };
            var updated = current with { State = normalized, ProviderReference = providerReference, Error = error, PossibleDuplicate = normalized == "NeedsReconciliation", UpdatedAtUtc = now };
            _integrationOperations[operationId] = updated;
            AuditMutation(updated.AgencyId, actor, "IntegrationOperationCompleted", "IntegrationOperation", operationId.ToString(), $"{updated.Kind} {normalized}");
            return updated;
        }
    }

    public IntegrationOperationDto ReconcileIntegrationOperation(Guid id, ReconcileIntegrationOperationRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_integrationOperations.TryGetValue(id, out var current)) throw new KeyNotFoundException("Integration operation not found.");
            EnsureActorAgency(actor, current.AgencyId);
            if (current.State != "NeedsReconciliation") throw new InvalidOperationException("Only NeedsReconciliation operations may be reconciled.");
            var resolution = request.Resolution.Trim().ToLowerInvariant();
            var state = resolution switch { "confirmed" or "accepted" => "Reconciled", "not-sent" or "failed" => "Failed", _ => throw new ArgumentException("Resolution must be Confirmed or Not-Sent.") };
            if (state == "Reconciled" && string.IsNullOrWhiteSpace(request.ProviderReference)) throw new ArgumentException("Provider reference is required for confirmed reconciliation.");
            var updated = current with { State = state, ProviderReference = request.ProviderReference?.Trim(), Error = request.Notes?.Trim(), PossibleDuplicate = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _integrationOperations[id] = updated;
            AuditMutation(updated.AgencyId, actor, "IntegrationOperationReconciled", "IntegrationOperation", id.ToString(), $"{resolution} {request.Notes}");
            return updated;
        }
    }

    public RecordingReferenceDto AddRecordingReference(Guid agencyId, Guid? incidentId, string providerKey, string providerReference, string mediaType, DateTimeOffset start, DateTimeOffset end, string description, string? playbackUri, string actor)
    {
        lock (_gate)
        {
            EnsureActorAgency(actor, agencyId);
            if (incidentId is Guid iid && _core.GetIncident(iid).AgencyId != agencyId) throw new UnauthorizedAccessException("Recording incident belongs to another agency.");
            var existing = _recordingReferences.Values.FirstOrDefault(x => x.ProviderKey.Equals(providerKey, StringComparison.OrdinalIgnoreCase) && x.ProviderReference.Equals(providerReference, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            var item = new RecordingReferenceDto(Guid.NewGuid(), agencyId, incidentId, providerKey, providerReference, mediaType, start, end, description, playbackUri, DateTimeOffset.UtcNow);
            _recordingReferences[item.Id] = item;
            AuditMutation(agencyId, actor, "RecordingLinked", "RecordingReference", item.Id.ToString(), providerReference);
            return item;
        }
    }

    public CameraFeedDto ImportDiscoveredCamera(ImportDiscoveredCameraRequest request, string actor)
    {
        lock (_gate)
        {
            EnsureActorAgency(actor, request.AgencyId);
            if (request.Candidate.State.Equals("Unavailable", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unavailable discovery candidate cannot be imported.");
            return UpsertCamera(null, new UpsertCameraFeedRequest(request.AgencyId, request.StationId, request.PremiseId, request.Candidate.Name, request.Candidate.Uri, request.Candidate.ProviderKey), actor);
        }
    }

    public IReadOnlyList<string> CoreRoadRestrictionGeoJson()
    {
        lock (_gate)
        {
            return _core.RoadRestrictions.Where(x => x.Active).Select(x => $"{{\"type\":\"Point\",\"coordinates\":[{x.Longitude:R},{x.Latitude:R}],\"properties\":{{\"name\":{JsonSerializer.Serialize(x.Name)},\"radiusMeters\":{x.RadiusMeters:R}}}}}").ToArray();
        }
    }

    public void ImportIntegrationState(IEnumerable<IntegrationEventDto>? events, IEnumerable<IntegrationOperationDto>? operations, IEnumerable<IntegrationAttemptDto>? attempts, IEnumerable<RecordingReferenceDto>? recordings)
    {
        lock (_gate)
        {
            Fill(_integrationEvents, events ?? [], x => x.Id);
            Fill(_integrationOperations, operations ?? [], x => x.Id);
            _integrationAttempts.Clear(); _integrationAttempts.AddRange(attempts ?? []);
            Fill(_recordingReferences, recordings ?? [], x => x.Id);
            foreach (var current in _integrationOperations.Values.Where(x => x.State == "Submitting").ToArray())
            {
                var now = DateTimeOffset.UtcNow;
                _integrationOperations[current.Id] = current with { State = "NeedsReconciliation", PossibleDuplicate = true, Error = "Provider result unknown after state restore/failover.", UpdatedAtUtc = now };
                var idx = _integrationAttempts.FindIndex(x => x.OperationId == current.Id && x.State == "Submitting" && x.CompletedAtUtc is null);
                if (idx >= 0) _integrationAttempts[idx] = _integrationAttempts[idx] with { State = "UnknownAfterRestore", Error = "Provider result unknown after state restore/failover.", CompletedAtUtc = now };
            }
        }
    }

    private static IntegrationEventDto NewIntegrationEvent(Guid agencyId, string providerKey, string category, string nativeId, string eventType, Guid? incidentId, Guid? unitId, string? resourceKey, DateTimeOffset sourceTimestamp, string payload, string correlation, string? error)
        => new(Guid.NewGuid(), agencyId, providerKey.Trim(), category, nativeId.Trim(), eventType.Trim(), error is null ? "Accepted" : "Rejected", incidentId, unitId, resourceKey, sourceTimestamp, DateTimeOffset.UtcNow, Sha256(payload), payload, correlation, error);

    private void EnsureActorAgency(string actor, Guid agencyId)
    {
        if (ResolveActorAgency(actor) != agencyId) throw new UnauthorizedAccessException("Integration operation is limited to the signed-in user's agency.");
    }
}
