using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

/// <summary>
/// Reconstructs an incident-centric historical view from immutable state histories and timestamped
/// operational facts. Replay deliberately prefers historical snapshots over current projections.
/// When a domain does not yet retain enough version history, the response calls that out in
/// Limitations rather than silently substituting today's state.
/// </summary>
public sealed class ReplayService
{
    private static readonly TimeSpan AvlFreshness = TimeSpan.FromMinutes(2);
    private readonly OperationalStore _core;
    private readonly FeatureStore _features;

    public ReplayService(OperationalStore core, FeatureStore features)
    {
        _core = core;
        _features = features;
    }

    public ReplaySnapshotDto GetIncidentSnapshot(Guid incidentId, DateTimeOffset? atUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        var at = (atUtc ?? now).ToUniversalTime();
        if (at > now.AddMinutes(1)) throw new ArgumentException("Replay time cannot be in the future.", nameof(atUtc));

        var incidentHistory = _core.IncidentHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .OrderBy(x => x.RecordedAtUtc)
            .ThenBy(x => x.Version)
            .ThenBy(x => x.Id)
            .ToArray();
        if (incidentHistory.Length == 0) throw new KeyNotFoundException("Incident did not exist at the requested replay time, or historical state predates the retained replay ledger.");

        var incidentEntry = incidentHistory[^1];
        var incident = incidentEntry.State;
        var units = _core.UnitHistory
            .Where(x => x.RecordedAtUtc <= at)
            .GroupBy(x => x.UnitId)
            .Select(x => x.OrderBy(y => y.RecordedAtUtc).ThenBy(y => y.Id).Last())
            .Where(x => x.State.IncidentId == incidentId)
            .Select(x => x.State)
            .OrderBy(x => x.CallSign, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var activity = _core.GetActivity(incidentId)
            .Where(x => x.OccurredAtUtc <= at)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ToArray();

        var avl = _features.AvlObservations
            .Where(x => x.IncidentId == incidentId && x.ObservedAtUtc <= at)
            .GroupBy(x => x.UnitId)
            .Select(x => x.OrderBy(y => y.ObservedAtUtc).ThenBy(y => y.Id).Last())
            .Select(x =>
            {
                var age = Math.Max(0, (at - x.ObservedAtUtc).TotalSeconds);
                return new ReplayAvlStateDto(x, age, age <= AvlFreshness.TotalSeconds);
            })
            .OrderBy(x => x.Observation.UnitId)
            .ToArray();

        var ais = _features.VesselObservationHistory
            .Where(x => x.AgencyId == incident.AgencyId && x.ObservedAtUtc <= at)
            .GroupBy(x => x.Mmsi, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(y => y.ObservedAtUtc).ThenBy(y => y.Id).Last())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roadRestrictions = _core.RoadRestrictions
            .Where(x => x.AgencyId == incident.AgencyId && x.StartsAtUtc <= at && (x.EndsAtUtc is null || x.EndsAtUtc > at))
            .Select(x => x with { Active = true })
            .OrderBy(x => x.StartsAtUtc)
            .ToArray();

        var mapObjects = _features.MapObjects
            .Where(x => x.AgencyId == incident.AgencyId && (x.IncidentId is null || x.IncidentId == incidentId) && x.StartsAtUtc <= at && (x.EndsAtUtc is null || x.EndsAtUtc > at))
            .OrderBy(x => x.StartsAtUtc)
            .ToArray();

        var dispatchActions = LatestDispatchActions(incidentId, at);
        var dispatchActionIds = dispatchActions.Select(x => x.Id).ToHashSet();
        var dispatchDeliveries = _features.DispatchDeliveryHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at && dispatchActionIds.Contains(x.State.DispatchActionId))
            .GroupBy(x => x.DeliveryId)
            .Select(x => x.OrderBy(y => y.RecordedAtUtc).ThenBy(y => y.Id).Last().State)
            .OrderBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Destination, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var stationDeliveries = _features.StationDeliveryHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .GroupBy(x => x.DeliveryId)
            .Select(x => x.OrderBy(y => y.RecordedAtUtc).ThenBy(y => y.Id).Last().State)
            .OrderBy(x => x.UpdatedAtUtc)
            .ToArray();

        var runSheets = _features.RunSheets
            .Where(x => x.IncidentId == incidentId && x.GeneratedAtUtc <= at)
            .Select(x => x.PrintedAtUtc is DateTimeOffset printed && printed > at
                ? x with { Status = "Generated", PrintedAtUtc = null }
                : x)
            .OrderBy(x => x.GeneratedAtUtc)
            .ToArray();

        var premises = _features.Premises
            .Where(x => x.AgencyId == incident.AgencyId && x.Address.Equals(incident.Address, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var premiseIds = premises.Select(x => x.Id).ToHashSet();
        var preplans = _features.Preplans
            .Where(x => premiseIds.Contains(x.PremiseId) && !x.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase) && x.EffectiveAtUtc <= at)
            .GroupBy(x => x.PremiseId)
            .Select(x => x.OrderBy(y => y.EffectiveAtUtc).ThenBy(y => y.Version).Last())
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var crgs = _features.CrgAssets
            .Where(x => premiseIds.Contains(x.PremiseId) && x.UploadedAtUtc <= at)
            .OrderBy(x => x.UploadedAtUtc)
            .ToArray();

        var messages = _core.Messages
            .Where(x => x.IncidentId == incidentId && x.SentAtUtc <= at)
            .OrderBy(x => x.SentAtUtc)
            .ToArray();
        var operationalMessages = _features.OperationalMessages
            .Where(x => x.IncidentId == incidentId && x.CreatedAtUtc <= at)
            .OrderBy(x => x.CreatedAtUtc)
            .ToArray();

        var alerts = _features.AlertHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .GroupBy(x => x.AlertId)
            .Select(x => x.OrderBy(y => y.RecordedAtUtc).ThenBy(y => y.Id).Last().State)
            .OrderBy(x => x.CreatedAtUtc)
            .ToArray();

        var commands = _features.Commands
            .Where(x => x.IncidentId == incidentId && x.EffectiveAtUtc <= at && (x.EndedAtUtc is null || x.EndedAtUtc > at))
            .OrderBy(x => x.EffectiveAtUtc)
            .ToArray();
        var groups = _features.OperationalGroups
            .Where(x => x.IncidentId == incidentId && x.CreatedAtUtc <= at)
            .OrderBy(x => x.CreatedAtUtc)
            .ToArray();
        var mutualAid = _features.MutualAid
            .Where(x => x.IncidentId == incidentId && x.RequestedAtUtc <= at)
            .Select(x => x.UpdatedAtUtc > at ? x with { State = "Requested", UpdatedAtUtc = x.RequestedAtUtc } : x)
            .OrderBy(x => x.RequestedAtUtc)
            .ToArray();
        var maydays = _features.Maydays
            .Where(x => x.IncidentId == incidentId && x.DeclaredAtUtc <= at)
            .Select(x => x.ResolvedAtUtc is DateTimeOffset resolved && resolved > at ? x with { State = "Active", ResolvedAtUtc = null } : x)
            .OrderBy(x => x.DeclaredAtUtc)
            .ToArray();

        var correctedLater = _features.Corrections
            .Where(x => x.IncidentId == incidentId && x.CorrectedAtUtc > at)
            .OrderBy(x => x.CorrectedAtUtc)
            .ToArray();

        var timeline = BuildTimeline(incidentId, at, correctedLater);
        var limitations = BuildLimitations(incidentEntry, incidentId, at, premises, dispatchActions, dispatchDeliveries, stationDeliveries, alerts);

        return new ReplaySnapshotDto(
            incident.AgencyId, incidentId, at, incident, units, activity, avl, ais, roadRestrictions, mapObjects,
            dispatchActions, dispatchDeliveries, stationDeliveries, runSheets, preplans, crgs, messages,
            operationalMessages, alerts, commands, groups, mutualAid, maydays, correctedLater, timeline, limitations);
    }

    private DispatchActionDto[] LatestDispatchActions(Guid incidentId, DateTimeOffset at)
    {
        return _features.DispatchActionHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .GroupBy(x => x.DispatchActionId)
            .Select(x => x.OrderBy(y => y.RecordedAtUtc).ThenBy(y => y.Id).Last().State)
            .OrderBy(x => x.CommittedAtUtc)
            .ToArray();
    }

    private ReplayKnowledgeItemDto[] BuildTimeline(Guid incidentId, DateTimeOffset at, IReadOnlyList<IncidentCorrectionDto> correctedLater)
    {
        var items = new List<ReplayKnowledgeItemDto>();
        items.AddRange(_core.GetReplay(incidentId, null, at).Select(x =>
            new ReplayKnowledgeItemDto(x.Id, "KNOWN", x.Stream, x.Kind, x.Summary, x.Actor, x.OccurredAtUtc, x.OccurredAtUtc)));

        items.AddRange(_features.DispatchActionHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .Select(x => new ReplayKnowledgeItemDto(x.Id, "KNOWN", "Dispatch", x.Cause, $"Dispatch {x.State.Id:D}: {x.State.State}", x.Actor, x.RecordedAtUtc, x.RecordedAtUtc)));
        items.AddRange(_features.DispatchDeliveryHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .Select(x => new ReplayKnowledgeItemDto(x.Id, "KNOWN", "Delivery", x.Cause, $"{x.State.Provider} → {x.State.Destination}: {x.State.State}", x.Actor, x.RecordedAtUtc, x.RecordedAtUtc)));
        items.AddRange(_features.StationDeliveryHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .Select(x => new ReplayKnowledgeItemDto(x.Id, "KNOWN", "Station", x.Cause, $"display={x.State.DisplayState}; alert={x.State.AlertState}; print={x.State.PrintState}; ack={x.State.AckState}", x.Actor, x.RecordedAtUtc, x.RecordedAtUtc)));
        items.AddRange(_features.AlertHistory
            .Where(x => x.IncidentId == incidentId && x.RecordedAtUtc <= at)
            .Select(x => new ReplayKnowledgeItemDto(x.Id, "KNOWN", "Alerts", x.Cause, $"{x.State.Severity}: {x.State.Summary} [{x.State.State}]", x.Actor, x.RecordedAtUtc, x.RecordedAtUtc)));
        items.AddRange(_features.AvlObservations
            .Where(x => x.IncidentId == incidentId && x.ObservedAtUtc <= at)
            .Select(x => new ReplayKnowledgeItemDto(x.Id, "OBSERVED", "AVL", "PositionObserved", $"Unit {x.UnitId:D} at {x.Latitude:R},{x.Longitude:R}; {x.SpeedKph:R} km/h via {x.Source}", x.Source, x.ObservedAtUtc, x.ObservedAtUtc)));
        items.AddRange(correctedLater.Select(x =>
            new ReplayKnowledgeItemDto(x.Id, "CORRECTED LATER", "CAD", "IncidentCorrected", $"{x.Field}: '{x.OriginalValue}' → '{x.CorrectedValue}'; {x.Reason}", x.Actor, x.CorrectedAtUtc, x.CorrectedAtUtc)));

        return items
            .OrderBy(x => x.EffectiveAtUtc)
            .ThenBy(x => x.RecordedAtUtc)
            .ThenBy(x => x.Id)
            .ToArray();
    }

    private string[] BuildLimitations(
        IncidentHistoryEntryDto incidentEntry,
        Guid incidentId,
        DateTimeOffset at,
        IReadOnlyList<PremiseDto> premises,
        DispatchActionDto[] dispatchActions,
        DispatchDeliveryDto[] dispatchDeliveries,
        StationDeliveryDto[] stationDeliveries,
        AlertDto[] alerts)
    {
        var limitations = new List<string>();
        if (incidentEntry.Cause.Equals("ImportedCurrentState", StringComparison.Ordinal))
            limitations.Add("This incident's retained history began as an imported current-state snapshot; state before that import cannot be reconstructed exactly.");
        if (_core.UnitHistory.Any(x => x.Cause.Equals("ImportedCurrentState", StringComparison.Ordinal) && x.RecordedAtUtc <= at))
            limitations.Add("Some unit history began as imported current-state snapshots; earlier unit transitions may be unavailable.");
        if (_features.DispatchActionHistory.Any(x => x.IncidentId == incidentId && x.Cause.Equals("ImportedCurrentState", StringComparison.Ordinal)) ||
            _features.DispatchDeliveryHistory.Any(x => x.IncidentId == incidentId && x.Cause.Equals("ImportedCurrentState", StringComparison.Ordinal)) ||
            _features.StationDeliveryHistory.Any(x => x.IncidentId == incidentId && x.Cause.Equals("ImportedCurrentState", StringComparison.Ordinal)) ||
            _features.AlertHistory.Any(x => x.IncidentId == incidentId && x.Cause.Equals("ImportedCurrentState", StringComparison.Ordinal)))
            limitations.Add("One or more dispatch/delivery/alert histories were imported from a current projection; their pre-import transitions are not exact.");
        if (premises.Any(x => x.UpdatedAtUtc > at))
            limitations.Add("Premise-to-incident matching currently uses the retained premise address because full premise revision history is not yet stored.");
        if (_features.OperationalMessages.Any(x => x.IncidentId == incidentId && x.CreatedAtUtc <= at))
            limitations.Add("Operational message content/time is historical; delivery/read/ack receipt state is still shown from the current message projection.");
        if (_features.VesselObservationHistory.Any(x => x.ObservedAtUtc <= at))
            limitations.Add("AIS observations are agency-wide because an incident-to-vessel correlation model is not yet available.");
        if (dispatchActions.Length > 0 && dispatchDeliveries.Length == 0)
            limitations.Add("A dispatch existed at this time but no retained delivery-state snapshot was available.");
        if (dispatchActions.Length > 0 && stationDeliveries.Length == 0)
            limitations.Add("A dispatch existed at this time but no retained station-delivery snapshot was available.");
        if (_features.Alerts.Any(x => x.IncidentId == incidentId && x.CreatedAtUtc <= at) && alerts.Length == 0)
            limitations.Add("An alert existed at this time but no retained alert-state snapshot was available.");
        limitations.Add("Personnel/crew-to-radio mapping and recording-reference history require their production provider/domain adapters before they can be reconstructed in Replay.");
        return limitations.Distinct(StringComparer.Ordinal).ToArray();
    }
}
