using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class OperationalStore
{
    public static readonly Guid DefaultAgencyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid Station1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Station2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly object _gate = new();
    private readonly OperationalMutationBuffer _mutationQueue;
    private readonly Dictionary<Guid, IncidentDto> _incidents = [];
    private readonly Dictionary<Guid, UnitDto> _units = [];
    private readonly Dictionary<Guid, StationDto> _stations = [];
    private readonly Dictionary<Guid, List<IncidentActivityDto>> _activities = [];
    private readonly Dictionary<string, ModuleStateDto> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, RoadRestrictionDto> _roadRestrictions = [];
    private readonly Dictionary<Guid, MessageDto> _messages = [];
    private readonly Dictionary<Guid, WorkforceShiftDto> _workforce = [];
    private readonly Dictionary<Guid, DocumentDto> _documents = [];
    private readonly Dictionary<Guid, RmsRecordDto> _rms = [];
    private readonly List<ReplayEventDto> _replay = [];
    private readonly List<IncidentHistoryEntryDto> _incidentHistory = [];
    private readonly List<UnitHistoryEntryDto> _unitHistory = [];
    private readonly DurableIdempotencyStore _idempotency;
    private int _incidentSequence = 4820;
    private int _rmsSequence = 1;
    private static readonly Dictionary<string, string[]> ModuleDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gis"] = ["cad"], ["station"] = ["cad"], ["mobile"] = ["cad"], ["messaging"] = ["cad"],
        ["workforce"] = ["cad"], ["documents"] = ["cad"], ["rms"] = ["cad"], ["marine"] = ["gis"],
        ["cameras"] = ["cad"], ["neris"] = ["rms"], ["reporting"] = ["rms"]
    };

    public OperationalStore(OperationalMutationBuffer mutationQueue, DurableIdempotencyStore idempotency)
    {
        _mutationQueue = mutationQueue;
        _idempotency = idempotency;
        Seed();
    }

    public AgencyDto Agency { get; } = new(DefaultAgencyId, "DEMO", "DISRoute Demonstration Agency");

    public IReadOnlyList<StationDto> Stations { get { lock (_gate) return _stations.Values.OrderBy(x => x.Code).ToArray(); } }
    public IReadOnlyList<UnitDto> Units { get { lock (_gate) return _units.Values.OrderBy(x => x.CallSign).ToArray(); } }
    public IReadOnlyList<ModuleStateDto> Modules { get { lock (_gate) return _modules.Values.OrderBy(x => x.DisplayName).ToArray(); } }
    public IReadOnlyList<RoadRestrictionDto> RoadRestrictions { get { lock (_gate) return _roadRestrictions.Values.OrderByDescending(x => x.StartsAtUtc).ToArray(); } }
    public IReadOnlyList<MessageDto> Messages { get { lock (_gate) return _messages.Values.OrderBy(x => x.SentAtUtc).ToArray(); } }
    public IReadOnlyList<WorkforceShiftDto> Workforce { get { lock (_gate) return _workforce.Values.OrderBy(x => x.Date).ThenBy(x => x.ShiftName).ToArray(); } }
    public IReadOnlyList<DocumentDto> Documents { get { lock (_gate) return _documents.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<RmsRecordDto> RmsRecords { get { lock (_gate) return _rms.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<IncidentHistoryEntryDto> IncidentHistory { get { lock (_gate) return _incidentHistory.OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.Version).ToArray(); } }
    public IReadOnlyList<UnitHistoryEntryDto> UnitHistory { get { lock (_gate) return _unitHistory.OrderBy(x => x.RecordedAtUtc).ToArray(); } }

    public IReadOnlyList<IncidentDto> GetIncidents(bool openOnly = false, string? query = null)
    {
        lock (_gate)
        {
            IEnumerable<IncidentDto> source = _incidents.Values;
            if (openOnly) source = source.Where(x => !string.Equals(x.Status, "Closed", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                source = source.Where(x => Contains(x.Number, q) || Contains(x.Nature, q) || Contains(x.Address, q) || Contains(x.CallerName, q) || Contains(x.CallerPhone, q));
            }
            return source.OrderByDescending(x => x.ReceivedAtUtc).ToArray();
        }
    }

    public IncidentDto GetIncident(Guid id) { lock (_gate) return RequireIncident(id); }
    public IReadOnlyList<IncidentActivityDto> GetActivity(Guid incidentId) { lock (_gate) { RequireIncident(incidentId); return _activities.GetValueOrDefault(incidentId, []).OrderBy(x => x.OccurredAtUtc).ToArray(); } }

    public IncidentDto CreateIncident(CreateIncidentRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { Actor = actor, Request = request }, out var existing)) return existing;
            RequireStation(request.StationId);
            if (request.AgencyId != DefaultAgencyId) throw new InvalidOperationException("Unknown agency.");
            if (string.IsNullOrWhiteSpace(request.Nature) || string.IsNullOrWhiteSpace(request.Address)) throw new ArgumentException("Nature and address are required.");

            var now = DateTimeOffset.UtcNow;
            var incident = new IncidentDto(Guid.NewGuid(), request.AgencyId, $"{now:yy}-{_incidentSequence++:000000}", request.Nature.Trim(), NormalizePriority(request.Priority), request.Address.Trim(), request.CrossStreets.Trim(), request.Municipality.Trim(), request.CallerName.Trim(), request.CallerPhone.Trim(), request.Notes.Trim(), "Open", request.StationId, 1, now, now, null);
            _incidents[incident.Id] = incident;
            CaptureIncidentState(incident, "IncidentCreated", actor);
            AddActivity(incident.Id, "CALL", $"Call received: {incident.Nature} at {incident.Address}", actor);
            AddReplay(incident.AgencyId, incident.Id, "CAD", "IncidentCreated", $"{incident.Number} {incident.Nature} at {incident.Address}", actor);
            Remember(request.IdempotencyKey, new { Actor = actor, Request = request }, incident);
            return incident;
        }
    }

    public IncidentDto UpdateIncident(Guid id, UpdateIncidentRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, out var existing)) return existing;
            var current = RequireIncident(id);
            EnsureVersion(current, request.ExpectedVersion);
            if (current.Status == "Closed") throw new InvalidOperationException("Closed incidents require reopen before editing.");
            var updated = current with
            {
                Nature = request.Nature.Trim(), Priority = NormalizePriority(request.Priority), Address = request.Address.Trim(), CrossStreets = request.CrossStreets.Trim(), Municipality = request.Municipality.Trim(), CallerName = request.CallerName.Trim(), CallerPhone = request.CallerPhone.Trim(), Notes = request.Notes.Trim(), Version = current.Version + 1, UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _incidents[id] = updated;
            CaptureIncidentState(updated, "IncidentUpdated", actor);
            AddActivity(id, "UPDATE", "Incident details updated.", actor);
            AddReplay(updated.AgencyId, id, "CAD", "IncidentUpdated", $"{updated.Number} details updated", actor);
            Remember(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, updated);
            return updated;
        }
    }

    public IncidentDto AssignUnit(Guid incidentId, AssignUnitRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { TargetId = incidentId, Actor = actor, Request = request }, out var existing)) return existing;
            var incident = RequireIncident(incidentId);
            EnsureVersion(incident, request.ExpectedIncidentVersion);
            if (incident.Status == "Closed") throw new InvalidOperationException("Closed incidents cannot receive assignments.");
            var unit = RequireUnit(request.UnitId);
            if (unit.AgencyId != incident.AgencyId) throw new InvalidOperationException("Cross-agency assignment requires an explicit mutual-aid workflow.");
            if (unit.Status == "OutOfService") throw new InvalidOperationException("Out-of-service units cannot be assigned.");
            if (unit.IncidentId is Guid oldIncidentId && oldIncidentId != incidentId)
            {
                if (!request.AllowReassignment) throw new InvalidOperationException("Unit is already committed; reassignment requires confirmation.");
                var old = RequireIncident(oldIncidentId);
                AddActivity(oldIncidentId, "REASSIGN", $"{unit.CallSign} reassigned from {old.Number} to {incident.Number}.", actor);
            }
            var assignedUnit = unit with { IncidentId = incidentId, Status = "Assigned", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _units[unit.Id] = assignedUnit;
            CaptureUnitState(assignedUnit, "UnitAssigned", actor);
            var updated = incident with { Status = incident.Status == "Open" ? "Dispatched" : incident.Status, Version = incident.Version + 1, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _incidents[incidentId] = updated;
            CaptureIncidentState(updated, "UnitAssigned", actor);
            AddActivity(incidentId, "ASSIGN", $"{unit.CallSign} assigned.", actor);
            AddReplay(updated.AgencyId, incidentId, "CAD", "UnitAssigned", $"{unit.CallSign} assigned to {updated.Number}", actor);
            Remember(request.IdempotencyKey, new { TargetId = incidentId, Actor = actor, Request = request }, updated);
            return updated;
        }
    }

    public UnitDto SetUnitStatus(Guid unitId, SetUnitStatusRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<UnitDto>(request.IdempotencyKey, new { TargetId = unitId, Actor = actor, Request = request }, out var existing)) return existing;
            var current = RequireUnit(unitId);
            var status = NormalizeUnitStatus(request.Status);
            var incidentId = status is "Available" or "OutOfService" or "OffDuty" or "Code55" ? null : current.IncidentId;
            var updated = current with { Status = status, IncidentId = incidentId, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _units[unitId] = updated;
            CaptureUnitState(updated, "UnitStatusChanged", actor);
            if (current.IncidentId is Guid iid)
            {
                AddActivity(iid, "STATUS", $"{current.CallSign} → {status}.", actor);
                var incident = RequireIncident(iid);
                AddReplay(incident.AgencyId, iid, "CAD", "UnitStatusChanged", $"{current.CallSign} → {status}", actor);
            }
            Remember(request.IdempotencyKey, new { TargetId = unitId, Actor = actor, Request = request }, updated);
            return updated;
        }
    }

    public IncidentDto CloseIncident(Guid id, CloseIncidentRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, out var existing)) return existing;
            var incident = RequireIncident(id);
            EnsureVersion(incident, request.ExpectedVersion);
            var now = DateTimeOffset.UtcNow;
            var updated = incident with { Status = "Closed", Version = incident.Version + 1, UpdatedAtUtc = now, ClosedAtUtc = now };
            _incidents[id] = updated;
            CaptureIncidentState(updated, "IncidentClosed", actor);
            foreach (var unit in _units.Values.Where(x => x.IncidentId == id).ToArray())
            {
                var released = unit with { Status = "Available", IncidentId = null, UpdatedAtUtc = now };
                _units[unit.Id] = released;
                CaptureUnitState(released, "IncidentClosed", actor);
            }
            AddActivity(id, "CLOSE", string.IsNullOrWhiteSpace(request.Reason) ? "Incident closed." : request.Reason.Trim(), actor);
            AddReplay(updated.AgencyId, id, "CAD", "IncidentClosed", $"{updated.Number} closed", actor);
            Remember(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, updated);
            return updated;
        }
    }

    public IncidentDto ReopenIncident(Guid id, ReopenIncidentRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, out var existing)) return existing;
            var incident = RequireIncident(id);
            if (incident.Status != "Closed") return incident;
            var updated = incident with { Status = "Open", Version = incident.Version + 1, UpdatedAtUtc = DateTimeOffset.UtcNow, ClosedAtUtc = null };
            _incidents[id] = updated;
            CaptureIncidentState(updated, "IncidentReopened", actor);
            AddActivity(id, "REOPEN", string.IsNullOrWhiteSpace(request.Reason) ? "Incident reopened." : request.Reason.Trim(), actor);
            AddReplay(updated.AgencyId, id, "CAD", "IncidentReopened", $"{updated.Number} reopened", actor);
            Remember(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, updated);
            return updated;
        }
    }

    public IncidentActivityDto AddNarrative(Guid id, AddNarrativeRequest request, string actor)
    {
        lock (_gate)
        {
            if (TryIdempotent<IncidentActivityDto>(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, out var existing)) return existing;
            RequireIncident(id);
            if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Narrative text is required.");
            var activity = AddActivity(id, "NARRATIVE", request.Text.Trim(), actor);
            var incident = RequireIncident(id);
            AddReplay(incident.AgencyId, id, "CAD", "NarrativeAdded", request.Text.Trim(), actor);
            Remember(request.IdempotencyKey, new { TargetId = id, Actor = actor, Request = request }, activity);
            return activity;
        }
    }

    public ModuleStateDto SetModule(string key, bool enabled)
    {
        lock (_gate)
        {
            if (!_modules.TryGetValue(key, out var current)) throw new KeyNotFoundException("Module not found.");
            if (current.Core && !enabled) throw new InvalidOperationException("Core integrity modules cannot be disabled.");
            if (enabled && ModuleDependencies.TryGetValue(key, out var required))
            {
                var missing = required.Where(dep => !_modules.TryGetValue(dep, out var depState) || !depState.Enabled).ToArray();
                if (missing.Length > 0) throw new InvalidOperationException($"Cannot enable {current.DisplayName}; required modules are disabled: {string.Join(", ", missing)}.");
            }
            if (!enabled)
            {
                var dependents = ModuleDependencies.Where(x => x.Value.Contains(key, StringComparer.OrdinalIgnoreCase) && _modules.TryGetValue(x.Key, out var state) && state.Enabled).Select(x => x.Key).ToArray();
                if (dependents.Length > 0) throw new InvalidOperationException($"Cannot disable {current.DisplayName}; enabled dependent modules: {string.Join(", ", dependents)}.");
            }
            var updated = current with { Enabled = enabled, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _modules[key] = updated;
            AddReplay(DefaultAgencyId, null, "Modules", enabled ? "ModuleEnabled" : "ModuleDisabled", updated.DisplayName, "administrator");
            return updated;
        }
    }

    public RoadRestrictionDto CreateRoadRestriction(CreateRoadRestrictionRequest request, string actor)
    {
        lock (_gate)
        {
            var item = new RoadRestrictionDto(Guid.NewGuid(), request.AgencyId, request.Name.Trim(), request.Description.Trim(), request.Latitude, request.Longitude, Math.Max(1, request.RadiusMeters), request.StartsAtUtc, request.EndsAtUtc, true);
            _roadRestrictions[item.Id] = item;
            AddReplay(item.AgencyId, null, "GIS", "RoadRestrictionCreated", item.Name, actor);
            return item;
        }
    }

    public RoadRestrictionDto EndRoadRestriction(Guid id, string actor, string? notes)
    {
        lock (_gate)
        {
            if (!_roadRestrictions.TryGetValue(id, out var current)) throw new KeyNotFoundException("Road restriction not found.");
            if (!current.Active) return current;
            var endedAt = DateTimeOffset.UtcNow;
            var updated = current with { Active = false, EndsAtUtc = endedAt };
            _roadRestrictions[id] = updated;
            var detail = string.IsNullOrWhiteSpace(notes) ? updated.Name : $"{updated.Name}: {notes.Trim()}";
            AddReplay(updated.AgencyId, null, "GIS", "RoadRestrictionEnded", detail, actor);
            return updated;
        }
    }

    public MessageDto SendMessage(SendMessageRequest request)
    {
        lock (_gate)
        {
            if (TryIdempotent<MessageDto>(request.IdempotencyKey, request, out var existing)) return existing;
            if (string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("Message text is required.");
            var item = new MessageDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.Channel.Trim(), request.Sender.Trim(), request.Text.Trim(), DateTimeOffset.UtcNow);
            _messages[item.Id] = item;
            AddReplay(item.AgencyId, item.IncidentId, "Messaging", "MessageSent", item.Text, item.Sender);
            Remember(request.IdempotencyKey, request, item);
            return item;
        }
    }

    public WorkforceShiftDto CreateShift(CreateWorkforceShiftRequest request)
    {
        lock (_gate)
        {
            RequireStation(request.StationId);
            var item = new WorkforceShiftDto(Guid.NewGuid(), request.AgencyId, request.Date, request.ShiftName.Trim(), request.StationId, request.Position.Trim(), request.EmployeeName.Trim(), request.Qualification.Trim(), request.Status.Trim());
            _workforce[item.Id] = item;
            AddReplay(item.AgencyId, null, "Workforce", "ShiftAssignmentCreated", $"{item.EmployeeName} {item.Position} {item.Date}", "system");
            return item;
        }
    }

    public DocumentDto CreateDocument(CreateDocumentRequest request)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var item = new DocumentDto(Guid.NewGuid(), request.AgencyId, request.Title.Trim(), request.Classification.Trim(), 1, "Draft", request.Content, request.CreatedBy.Trim(), now, now);
            _documents[item.Id] = item;
            AddReplay(item.AgencyId, null, "Documents", "DocumentCreated", item.Title, item.CreatedBy);
            return item;
        }
    }

    public DocumentDto PublishDocument(Guid id, PublishDocumentRequest request)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(id, out var current)) throw new KeyNotFoundException("Document not found.");
            if (current.Status == "Published") return current;
            var updated = current with { Status = "Published", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _documents[id] = updated;
            AddReplay(updated.AgencyId, null, "Documents", "DocumentPublished", updated.Title, request.Actor);
            return updated;
        }
    }

    public RmsRecordDto CreateRmsRecord(CreateRmsRecordRequest request)
    {
        lock (_gate)
        {
            if (request.IncidentId is Guid incidentId)
            {
                var incident = RequireIncident(incidentId);
                if (incident.AgencyId != request.AgencyId) throw new InvalidOperationException("RMS record and incident agency do not match.");
            }
            var now = DateTimeOffset.UtcNow;
            var type = string.IsNullOrWhiteSpace(request.RecordType) ? "General" : request.RecordType.Trim();
            var item = new RmsRecordDto(Guid.NewGuid(), request.AgencyId, type, $"RMS-{DateTime.UtcNow:yy}-{_rmsSequence++:000000}", request.Title.Trim(), "Open", request.Summary.Trim(), request.IncidentId, now, now);
            _rms[item.Id] = item;
            AddReplay(item.AgencyId, item.IncidentId, "RMS", "RecordCreated", $"{item.Number} {item.Title}", "system");
            return item;
        }
    }

    public IReadOnlyList<SearchResultDto> Search(string query)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query)) return [];
            var q = query.Trim();
            var results = new List<SearchResultDto>();
            results.AddRange(_incidents.Values.Where(x => Contains(x.Number, q) || Contains(x.Nature, q) || Contains(x.Address, q)).Select(x => new SearchResultDto("Incident", x.Id, $"{x.Number} — {x.Nature}", x.Address, x.UpdatedAtUtc)));
            results.AddRange(_units.Values.Where(x => Contains(x.CallSign, q) || Contains(x.UnitType, q)).Select(x => new SearchResultDto("Unit", x.Id, x.CallSign, $"{x.UnitType} • {x.Status}", x.UpdatedAtUtc)));
            results.AddRange(_documents.Values.Where(x => Contains(x.Title, q) || Contains(x.Content, q)).Select(x => new SearchResultDto("Document", x.Id, x.Title, $"v{x.Version} • {x.Status}", x.UpdatedAtUtc)));
            results.AddRange(_rms.Values.Where(x => Contains(x.Number, q) || Contains(x.Title, q) || Contains(x.Summary, q)).Select(x => new SearchResultDto("RMS", x.Id, $"{x.Number} — {x.Title}", x.RecordType, x.UpdatedAtUtc)));
            results.AddRange(_rmsIncidentReports.Values.Where(x => Contains(x.Number, q) || Contains(x.IncidentNumber, q) || Contains(x.Nature, q) || Contains(x.Summary, q)).Select(x => new SearchResultDto("RMS Incident Report", x.Id, $"{x.Number} — {x.IncidentNumber}", $"{x.Status} • {x.Nature}", x.UpdatedAtUtc)));
            return results.OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToArray();
        }
    }

    public IReadOnlyList<ReplayEventDto> GetReplay(Guid? incidentId = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null)
    {
        lock (_gate)
        {
            IEnumerable<ReplayEventDto> source = _replay;
            if (incidentId is Guid iid) source = source.Where(x => x.IncidentId == iid);
            if (fromUtc is DateTimeOffset from) source = source.Where(x => x.OccurredAtUtc >= from);
            if (toUtc is DateTimeOffset to) source = source.Where(x => x.OccurredAtUtc <= to);
            return source.OrderBy(x => x.OccurredAtUtc).ToArray();
        }
    }

    public IReadOnlyList<CapabilityDto> Capabilities =>
    [
        new("cad-core", "Available", null),
        new("realtime", "Available", "SignalR"),
        new("street-imagery", "NotConfigured", null),
        new("external-cameras", "NotConfigured", null),
        new("traffic-routing", "NotConfigured", null),
        new("ais", "NotConfigured", null),
        new("radio", "NotConfigured", null),
        new("911", "NotConfigured", null),
        new("recording", "NotConfigured", null),
        new("weather", "NotConfigured", null)
    ];

    private void Seed()
    {
        _stations[Station1Id] = new StationDto(Station1Id, DefaultAgencyId, "S1", "Station 1", "100 Firehouse Avenue");
        _stations[Station2Id] = new StationDto(Station2Id, DefaultAgencyId, "S2", "Station 2", "200 Engine Street");
        SeedUnit("10000000-0000-0000-0000-000000000001", Station1Id, "E1", "Fire", "Engine");
        SeedUnit("10000000-0000-0000-0000-000000000002", Station1Id, "L1", "Fire", "Ladder");
        SeedUnit("10000000-0000-0000-0000-000000000003", Station1Id, "BC1", "Fire", "Chief");
        SeedUnit("20000000-0000-0000-0000-000000000001", Station2Id, "E2", "Fire", "Engine");
        SeedUnit("20000000-0000-0000-0000-000000000002", Station2Id, "L2", "Fire", "Ladder");
        SeedUnit("20000000-0000-0000-0000-000000000003", Station2Id, "M2", "EMS", "Medic");

        AddModule("cad", "CAD", true, true);
        AddModule("gis", "GIS / Routing", true, false);
        AddModule("station", "Station", true, false);
        AddModule("mobile", "Mobile", true, false);
        AddModule("messaging", "Messaging", true, false);
        AddModule("workforce", "Workforce", true, false);
        AddModule("documents", "Documents / Forms", true, false);
        AddModule("rms", "RMS", true, false);
        AddModule("marine", "Marine / AIS", false, false);
        AddModule("cameras", "Cameras", false, false);
        AddModule("neris", "NERIS Adapter", true, false);
        AddModule("reporting", "Advanced Reporting", true, false);

        CreateIncident(new CreateIncidentRequest(DefaultAgencyId, Station1Id, "Structure Fire", "Emergency", "123 Main Street", "Oak St / Pine St", "Demo City", "Sample Caller", "555-0100", "Smoke reported from second floor.", "seed-incident"), "system");
    }

    private void SeedUnit(string id, Guid stationId, string callSign, string discipline, string unitType)
    {
        var now = DateTimeOffset.UtcNow;
        var guid = Guid.Parse(id);
        var unit = new UnitDto(guid, DefaultAgencyId, stationId, callSign, discipline, unitType, "Available", null, now);
        _units[guid] = unit;
        CaptureUnitState(unit, "UnitSeeded", "system");
    }

    private void AddModule(string key, string display, bool enabled, bool core) => _modules[key] = new ModuleStateDto(key, display, enabled, core, DateTimeOffset.UtcNow);
    private StationDto RequireStation(Guid id) => _stations.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException("Station not found.");
    private UnitDto RequireUnit(Guid id) => _units.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException("Unit not found.");
    private IncidentDto RequireIncident(Guid id) => _incidents.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException("Incident not found.");
    private static void EnsureVersion(IncidentDto incident, long expected) { if (incident.Version != expected) throw new InvalidOperationException($"Concurrency conflict. Current version is {incident.Version}; expected {expected}."); }
    private static bool Contains(string? source, string query) => source?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    private static string NormalizePriority(string value) => value.Trim().ToLowerInvariant() switch { "emergency" or "high" or "1" => "Emergency", "low" or "3" => "Low", _ => "Routine" };
    private static string NormalizeUnitStatus(string value) => value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "available" or "ataquarters" or "quarters" => "Available",
        "assigned" => "Assigned",
        "enroute" or "responding" => "EnRoute",
        "onscene" or "atscene" => "OnScene",
        "transporting" => "Transporting",
        "athospital" => "AtHospital",
        "clearing" => "Clearing",
        "command" => "Command",
        "training" => "Training",
        "firewatch" => "FireWatch",
        "offduty" => "OffDuty",
        "code55" or "temporarilyoffduty" => "Code55",
        "outofservice" or "oos" => "OutOfService",
        _ => throw new ArgumentException("Unsupported unit status.")
    };

    private IncidentActivityDto AddActivity(Guid incidentId, string category, string message, string actor)
    {
        var item = new IncidentActivityDto(Guid.NewGuid(), incidentId, category, message, actor, DateTimeOffset.UtcNow);
        if (!_activities.TryGetValue(incidentId, out var list)) _activities[incidentId] = list = [];
        list.Add(item);
        return item;
    }

    private void CaptureIncidentState(IncidentDto state, string cause, string actor)
    {
        _incidentHistory.Add(new IncidentHistoryEntryDto(Guid.NewGuid(), state.Id, state.Version, cause, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), state, state.UpdatedAtUtc));
    }

    private void CaptureUnitState(UnitDto state, string cause, string actor)
    {
        _unitHistory.Add(new UnitHistoryEntryDto(Guid.NewGuid(), state.Id, cause, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), state, state.UpdatedAtUtc));
    }

    private void AddReplay(Guid agencyId, Guid? incidentId, string stream, string kind, string summary, string actor)
    {
        var replay = new ReplayEventDto(Guid.NewGuid(), agencyId, incidentId, stream, kind, summary, actor, DateTimeOffset.UtcNow);
        _replay.Add(replay);
        _mutationQueue.Enqueue(agencyId, incidentId is null ? stream : "Incident", (incidentId ?? replay.Id).ToString("D"), kind, actor, summary, replay);
    }
    private bool TryIdempotent<T>(string? key, object request, out T value, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
        => _idempotency.TryGet(key, $"OperationalStore.{operation}", request, out value);

    private void Remember<T>(string? key, object request, T value, [System.Runtime.CompilerServices.CallerMemberName] string operation = "") where T : notnull
        => _idempotency.Remember(key, $"OperationalStore.{operation}", request, value);
}
