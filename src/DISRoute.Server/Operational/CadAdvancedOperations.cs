using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, AdditionalAlarmDto> _additionalAlarms = [];
    private readonly Dictionary<Guid, AvlObservationDto> _avlObservations = [];
    private readonly Dictionary<Guid, AvlAdvisoryDto> _avlAdvisories = [];
    private readonly Dictionary<Guid, IncidentCorrectionDto> _corrections = [];

    public IReadOnlyList<AdditionalAlarmDto> AdditionalAlarms { get { lock (_gate) return _additionalAlarms.Values.OrderByDescending(x => x.CommittedAtUtc).ToArray(); } }
    public IReadOnlyList<AvlObservationDto> AvlObservations { get { lock (_gate) return _avlObservations.Values.OrderByDescending(x => x.ObservedAtUtc).ToArray(); } }
    public IReadOnlyList<AvlAdvisoryDto> AvlAdvisories { get { lock (_gate) return _avlAdvisories.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<IncidentCorrectionDto> Corrections { get { lock (_gate) return _corrections.Values.OrderByDescending(x => x.CorrectedAtUtc).ToArray(); } }

    public AdditionalAlarmDto CommitAdditionalAlarm(Guid incidentId, CommitAdditionalAlarmRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<AdditionalAlarmDto>(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, out var prior)) return prior;
            var current = _core.GetIncident(incidentId);
            if (current.Version != request.ExpectedIncidentVersion)
                throw new InvalidOperationException($"Concurrency conflict. Current version is {current.Version}; expected {request.ExpectedIncidentVersion}.");
            var alarmNumber = _additionalAlarms.Values.Count(x => x.IncidentId == incidentId) + 2;
            var dispatch = CommitDispatch(incidentId, new CommitDispatchRequest(current.Version, request.UnitIds, request.DeliveryProviders, request.IdempotencyKey is null ? null : request.IdempotencyKey + ":dispatch"), request.Actor);
            var item = new AdditionalAlarmDto(Guid.NewGuid(), current.AgencyId, incidentId, alarmNumber, dispatch.Action.Id, request.UnitIds.Distinct().ToArray(), request.Actor, DateTimeOffset.UtcNow);
            _additionalAlarms[item.Id] = item;
            AuditMutation(item.AgencyId, request.Actor, "AdditionalAlarmCommitted", "Incident", incidentId.ToString("D"), $"Alarm {alarmNumber}; dispatch {dispatch.Action.Id:D}");
            Remember(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, item);
            return item;
        }
    }

    public DispatchActionDto CancelDispatch(Guid dispatchActionId, CancelDispatchRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<DispatchActionDto>(request.IdempotencyKey, new { TargetId = dispatchActionId, Request = request }, out var prior)) return prior;
            if (!_dispatchActions.TryGetValue(dispatchActionId, out var current)) throw new KeyNotFoundException("Dispatch action not found.");
            if (current.State == "Cancelled") return current;
            var updated = current with { State = "Cancelled" };
            _dispatchActions[dispatchActionId] = updated;
            CaptureDispatchActionState(updated, "DispatchCancelled", request.Actor);
            var now = DateTimeOffset.UtcNow;
            foreach (var delivery in _dispatchDeliveries.Values.Where(x => x.DispatchActionId == dispatchActionId).ToArray())
            {
                if (delivery.State == "Pending")
                {
                    var cancelledDelivery = delivery with { State = "Cancelled", UpdatedAtUtc = now, LastError = request.Reason };
                    _dispatchDeliveries[delivery.Id] = cancelledDelivery;
                    CaptureDispatchDeliveryState(cancelledDelivery, current.IncidentId, "DispatchCancelled", request.Actor);
                }
                if (delivery.State == "Confirmed")
                {
                    var cancelDelivery = new DispatchDeliveryDto(Guid.NewGuid(), dispatchActionId, "Cancel:" + delivery.Provider, delivery.Destination, "Pending", 0, null, now);
                    _dispatchDeliveries[cancelDelivery.Id] = cancelDelivery;
                    CaptureDispatchDeliveryState(cancelDelivery, current.IncidentId, "DispatchCancellationQueued", request.Actor);
                }
            }
            foreach (var station in _stationDeliveries.Values.Where(x => x.DispatchActionId == dispatchActionId).ToArray())
            {
                var cancelledStation = station with { DisplayState = "CancellationPending", AlertState = station.AlertState == "Confirmed" ? "CancellationPending" : station.AlertState, PrintState = station.PrintState == "Completed" ? "UpdatePending" : station.PrintState, UpdatedAtUtc = now };
                _stationDeliveries[station.Id] = cancelledStation;
                CaptureStationDeliveryState(cancelledStation, "DispatchCancelled", request.Actor);
            }
            AuditMutation(current.AgencyId, request.Actor, "DispatchCancelled", "DispatchAction", dispatchActionId.ToString("D"), request.Reason);
            Remember(request.IdempotencyKey, new { TargetId = dispatchActionId, Request = request }, updated);
            return updated;
        }
    }

    public (AvlObservationDto Observation, AvlAdvisoryDto? Advisory) ObserveAvl(UpsertAvlObservationRequest request)
    {
        lock (_gate)
        {
            var unit = _core.Units.FirstOrDefault(x => x.Id == request.UnitId) ?? throw new KeyNotFoundException("Unit not found.");
            var item = new AvlObservationDto(Guid.NewGuid(), request.AgencyId, request.UnitId, unit.IncidentId, request.Latitude, request.Longitude, Math.Max(0, request.SpeedKph), request.IncidentDistanceMeters, request.Source.Trim(), request.ObservedAtUtc);
            _avlObservations[item.Id] = item;
            AvlAdvisoryDto? advisory = null;
            if (unit.Status == "Assigned" && item.SpeedKph >= 8)
                advisory = CreateAvlAdvisory(unit, item, "Normal", "AppearsMoving", $"{unit.CallSign} appears to be moving while still Assigned.", "EnRoute");
            else if (unit.Status == "EnRoute" && item.IncidentDistanceMeters is double distance && distance <= 150)
                advisory = CreateAvlAdvisory(unit, item, "Normal", "NearIncident", $"{unit.CallSign} is within {distance:0} m of the incident while still En Route.", "OnScene");
            else if (unit.Status == "EnRoute" && item.SpeedKph < 1 && item.ObservedAtUtc - unit.UpdatedAtUtc > TimeSpan.FromMinutes(5))
                advisory = CreateAvlAdvisory(unit, item, "High", "StationaryEnRoute", $"{unit.CallSign} has an En Route status but AVL currently reports no movement.", "EnRoute");
            return (item, advisory);
        }
    }

    public AvlAdvisoryDto ResolveAvlAdvisory(Guid id, ResolveAvlAdvisoryRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<AvlAdvisoryDto>(request.IdempotencyKey, new { TargetId = id, Request = request }, out var prior)) return prior;
            if (!_avlAdvisories.TryGetValue(id, out var current)) throw new KeyNotFoundException("AVL advisory not found.");
            if (request.ApplySuggestedStatus)
                _core.SetUnitStatus(current.UnitId, new SetUnitStatusRequest(current.SuggestedStatus, request.IdempotencyKey is null ? null : request.IdempotencyKey + ":status"), request.Actor);
            var updated = current with { State = request.ApplySuggestedStatus ? "Applied" : "Dismissed", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _avlAdvisories[id] = updated;
            AuditMutation(current.AgencyId, request.Actor, "AvlAdvisoryResolved", "AVLAdvisory", id.ToString("D"), updated.State);
            Remember(request.IdempotencyKey, new { TargetId = id, Request = request }, updated);
            return updated;
        }
    }

    public IncidentCorrectionDto CorrectIncident(Guid incidentId, CorrectIncidentFieldRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentCorrectionDto>(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, out var prior)) return prior;
            var current = _core.GetIncident(incidentId);
            if (current.Version != request.ExpectedVersion)
                throw new InvalidOperationException($"Concurrency conflict. Current version is {current.Version}; expected {request.ExpectedVersion}.");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Correction reason is required.");
            var field = request.Field.Trim().ToLowerInvariant();
            var original = field switch
            {
                "nature" => current.Nature,
                "priority" => current.Priority,
                "address" => current.Address,
                "crossstreets" or "cross-streets" => current.CrossStreets,
                "municipality" => current.Municipality,
                "callername" or "caller-name" => current.CallerName,
                "callerphone" or "caller-phone" => current.CallerPhone,
                "notes" => current.Notes,
                _ => throw new ArgumentException($"Field '{request.Field}' cannot be corrected through the generic correction workflow.")
            };
            var value = request.CorrectedValue ?? string.Empty;
            var update = new UpdateIncidentRequest(current.Version,
                field == "nature" ? value : current.Nature,
                field == "priority" ? value : current.Priority,
                field == "address" ? value : current.Address,
                field is "crossstreets" or "cross-streets" ? value : current.CrossStreets,
                field == "municipality" ? value : current.Municipality,
                field is "callername" or "caller-name" ? value : current.CallerName,
                field is "callerphone" or "caller-phone" ? value : current.CallerPhone,
                field == "notes" ? value : current.Notes,
                request.IdempotencyKey is null ? null : request.IdempotencyKey + ":update");
            var updatedIncident = _core.UpdateIncident(incidentId, update, request.Actor);
            var correction = new IncidentCorrectionDto(Guid.NewGuid(), current.AgencyId, incidentId, request.Field.Trim(), original, value, request.Reason.Trim(), request.Actor, DateTimeOffset.UtcNow, updatedIncident.Version);
            _corrections[correction.Id] = correction;
            AuditMutation(current.AgencyId, request.Actor, "IncidentCorrected", "Incident", incidentId.ToString("D"), $"{request.Field}: '{original}' -> '{value}'; reason={request.Reason.Trim()}");
            Remember(request.IdempotencyKey, new { TargetId = incidentId, Request = request }, correction);
            return correction;
        }
    }

    private AvlAdvisoryDto CreateAvlAdvisory(UnitDto unit, AvlObservationDto observation, string severity, string kind, string summary, string suggestedStatus)
    {
        var existing = _avlAdvisories.Values.FirstOrDefault(x => x.UnitId == unit.Id && x.Kind == kind && x.State == "Open");
        if (existing is not null) return existing;
        var advisory = new AvlAdvisoryDto(Guid.NewGuid(), unit.AgencyId, unit.Id, unit.IncidentId, severity, kind, summary, suggestedStatus, "Open", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _avlAdvisories[advisory.Id] = advisory;
        return advisory;
    }
}
