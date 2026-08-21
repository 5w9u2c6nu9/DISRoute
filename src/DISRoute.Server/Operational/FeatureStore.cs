using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

/// <summary>
/// Broad operational feature store used by the direct implementation build.  It intentionally keeps
/// all state in-process until the PostgreSQL persistence layer can be compiled and migration-tested.
/// Every mutation emits an audit record so replacing this store with durable repositories does not
/// require changing the API semantics.
/// </summary>
public sealed partial class FeatureStore
{
    private readonly object _gate = new();
    private readonly OperationalStore _core;
    private readonly OperationalMutationBuffer _mutationQueue;
    private readonly Dictionary<Guid, CallRecordDto> _calls = [];
    private readonly Dictionary<Guid, DispatchActionDto> _dispatchActions = [];
    private readonly List<DispatchActionHistoryEntryDto> _dispatchActionHistory = [];
    private readonly Dictionary<Guid, DispatchDeliveryDto> _dispatchDeliveries = [];
    private readonly List<DispatchDeliveryHistoryEntryDto> _dispatchDeliveryHistory = [];
    private readonly Dictionary<Guid, FireWatchDto> _fireWatches = [];
    private readonly Dictionary<Guid, List<FireWatchCheckDto>> _fireWatchChecks = [];
    private readonly Dictionary<Guid, RelocationDto> _relocations = [];
    private readonly Dictionary<Guid, PremiseDto> _premises = [];
    private readonly Dictionary<Guid, PreplanDto> _preplans = [];
    private readonly Dictionary<Guid, CrgAssetDto> _crgs = [];
    private readonly Dictionary<Guid, HydrantDto> _hydrants = [];
    private readonly Dictionary<Guid, MapObjectDto> _mapObjects = [];
    private readonly Dictionary<string, VesselObservationDto> _vessels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VesselObservationDto> _vesselObservationHistory = [];
    private readonly Dictionary<Guid, AlertDto> _alerts = [];
    private readonly List<AlertHistoryEntryDto> _alertHistory = [];
    private readonly Dictionary<Guid, BulletinDto> _bulletins = [];
    private readonly Dictionary<Guid, StationDeliveryDto> _stationDeliveries = [];
    private readonly List<StationDeliveryHistoryEntryDto> _stationDeliveryHistory = [];
    private readonly Dictionary<Guid, RunSheetDto> _runSheets = [];
    private readonly Dictionary<string, ClientHeartbeatDto> _heartbeats = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderConfigDto> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, CameraFeedDto> _cameras = [];
    private readonly Dictionary<Guid, RadioStreamDto> _radioStreams = [];
    private readonly Dictionary<Guid, LeaveRequestDto> _leaveRequests = [];
    private readonly Dictionary<Guid, ShiftTradeDto> _shiftTrades = [];
    private readonly Dictionary<Guid, List<DocumentVersionDto>> _documentVersions = [];
    private readonly Dictionary<Guid, DocumentAcknowledgementDto> _documentAcks = [];
    private readonly Dictionary<Guid, FormTemplateDto> _formTemplates = [];
    private readonly Dictionary<Guid, FormInstanceDto> _forms = [];
    private readonly Dictionary<Guid, InspectionDto> _inspections = [];
    private readonly Dictionary<Guid, PermitDto> _permits = [];
    private readonly Dictionary<Guid, MaintenanceRecordDto> _maintenance = [];
    private readonly Dictionary<Guid, NerisReportDto> _neris = [];
    private readonly Dictionary<string, DeviceIdentityDto> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UserSecurityDto> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AuditRecordDto> _audit = [];
    private readonly List<AuditIntegrityRecordDto> _auditIntegrity = [];
    private readonly List<BackupStatusDto> _backups = [];
    private readonly List<ReplicationStatusDto> _replication = [];
    private readonly Dictionary<Guid, ConfigVersionDto> _configs = [];
    private readonly Dictionary<Guid, IncidentRelationshipDto> _incidentRelationships = [];
    private readonly Dictionary<Guid, OperationalGroupDto> _operationalGroups = [];
    private readonly Dictionary<Guid, CommandAssignmentDto> _commands = [];
    private readonly Dictionary<Guid, MutualAidRequestDto> _mutualAid = [];
    private readonly Dictionary<Guid, MaydayEventDto> _maydays = [];
    private readonly Dictionary<Guid, ResponsePlanDto> _responsePlans = [];
    private readonly Dictionary<Guid, WorkspaceLayoutDto> _workspaces = [];
    private readonly Dictionary<Guid, ThemePackageDto> _themes = [];
    private readonly Dictionary<Guid, CollaborativeNarrativeDto> _collaborativeNarratives = [];
    private readonly Dictionary<Guid, List<CollaborativeNarrativeOperationDto>> _collaborativeNarrativeOperations = [];
    private readonly DurableIdempotencyStore _idempotency;
    private int _permitSequence = 1;

    public FeatureStore(OperationalStore core, OperationalMutationBuffer mutationQueue, DurableIdempotencyStore idempotency)
    {
        _core = core;
        _mutationQueue = mutationQueue;
        _idempotency = idempotency;
        Seed();
    }

    public IReadOnlyList<CallRecordDto> Calls { get { lock (_gate) return _calls.Values.OrderByDescending(x => x.ReceivedAtUtc).ToArray(); } }
    public IReadOnlyList<DispatchActionDto> DispatchActions { get { lock (_gate) return _dispatchActions.Values.OrderByDescending(x => x.CommittedAtUtc).ToArray(); } }
    public IReadOnlyList<DispatchActionHistoryEntryDto> DispatchActionHistory { get { lock (_gate) return _dispatchActionHistory.OrderBy(x => x.RecordedAtUtc).ToArray(); } }
    public IReadOnlyList<DispatchDeliveryDto> DispatchDeliveries { get { lock (_gate) return _dispatchDeliveries.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<DispatchDeliveryHistoryEntryDto> DispatchDeliveryHistory { get { lock (_gate) return _dispatchDeliveryHistory.OrderBy(x => x.RecordedAtUtc).ToArray(); } }
    public IReadOnlyList<FireWatchDto> FireWatches { get { lock (_gate) return _fireWatches.Values.OrderByDescending(x => x.StartedAtUtc).ToArray(); } }
    public IReadOnlyList<RelocationDto> Relocations { get { lock (_gate) return _relocations.Values.OrderByDescending(x => x.StartedAtUtc).ToArray(); } }
    public IReadOnlyList<PremiseDto> Premises { get { lock (_gate) return _premises.Values.OrderBy(x => x.Address).ToArray(); } }
    public IReadOnlyList<PreplanDto> Preplans { get { lock (_gate) return _preplans.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<CrgAssetDto> CrgAssets { get { lock (_gate) return _crgs.Values.OrderByDescending(x => x.UploadedAtUtc).ToArray(); } }
    public IReadOnlyList<HydrantDto> Hydrants { get { lock (_gate) return _hydrants.Values.OrderBy(x => x.AssetId).ToArray(); } }
    public IReadOnlyList<MapObjectDto> MapObjects { get { lock (_gate) return _mapObjects.Values.OrderByDescending(x => x.StartsAtUtc).ToArray(); } }
    public IReadOnlyList<VesselObservationDto> Vessels { get { lock (_gate) return _vessels.Values.OrderByDescending(x => x.ObservedAtUtc).ToArray(); } }
    public IReadOnlyList<VesselObservationDto> VesselObservationHistory { get { lock (_gate) return _vesselObservationHistory.OrderBy(x => x.ObservedAtUtc).ToArray(); } }
    public IReadOnlyList<AlertDto> Alerts { get { lock (_gate) return _alerts.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<AlertHistoryEntryDto> AlertHistory { get { lock (_gate) return _alertHistory.OrderBy(x => x.RecordedAtUtc).ToArray(); } }
    public IReadOnlyList<BulletinDto> Bulletins { get { lock (_gate) return _bulletins.Values.OrderByDescending(x => x.StartsAtUtc).ToArray(); } }
    public IReadOnlyList<StationDeliveryDto> StationDeliveries { get { lock (_gate) return _stationDeliveries.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<StationDeliveryHistoryEntryDto> StationDeliveryHistory { get { lock (_gate) return _stationDeliveryHistory.OrderBy(x => x.RecordedAtUtc).ToArray(); } }
    public IReadOnlyList<RunSheetDto> RunSheets { get { lock (_gate) return _runSheets.Values.OrderByDescending(x => x.GeneratedAtUtc).ToArray(); } }
    public IReadOnlyList<ClientHeartbeatDto> Heartbeats { get { lock (_gate) return _heartbeats.Values.OrderByDescending(x => x.LastSeenUtc).ToArray(); } }
    public IReadOnlyList<ProviderConfigDto> Providers { get { lock (_gate) return _providers.Values.OrderBy(x => x.Category).ThenBy(x => x.DisplayName).ToArray(); } }
    public IReadOnlyList<ProviderHealthDto> ProviderHealth { get { lock (_gate) return _providers.Values.Select(x => new ProviderHealthDto(x.Key, x.DisplayName, x.Category, x.State, x.Enabled ? null : "Disabled", DateTimeOffset.UtcNow)).ToArray(); } }
    public IReadOnlyList<CameraFeedDto> Cameras { get { lock (_gate) return _cameras.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<RadioStreamDto> RadioStreams { get { lock (_gate) return _radioStreams.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<LeaveRequestDto> LeaveRequests { get { lock (_gate) return _leaveRequests.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<ShiftTradeDto> ShiftTrades { get { lock (_gate) return _shiftTrades.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<FormTemplateDto> FormTemplates { get { lock (_gate) return _formTemplates.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<FormInstanceDto> Forms { get { lock (_gate) return _forms.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<InspectionDto> Inspections { get { lock (_gate) return _inspections.Values.OrderByDescending(x => x.InspectionDate).ToArray(); } }
    public IReadOnlyList<PermitDto> Permits { get { lock (_gate) return _permits.Values.OrderByDescending(x => x.IssuedDate).ToArray(); } }
    public IReadOnlyList<MaintenanceRecordDto> Maintenance { get { lock (_gate) return _maintenance.Values.OrderByDescending(x => x.OpenedDate).ToArray(); } }
    public IReadOnlyList<NerisReportDto> NerisReports { get { lock (_gate) return _neris.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<DeviceIdentityDto> Devices { get { lock (_gate) return _devices.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<UserSecurityDto> Users { get { lock (_gate) return _users.Values.OrderBy(x => x.DisplayName).ToArray(); } }
    public IReadOnlyList<AuditRecordDto> Audit { get { lock (_gate) return _audit.OrderByDescending(x => x.OccurredAtUtc).ToArray(); } }
    public IReadOnlyList<AuditIntegrityRecordDto> AuditIntegrity { get { lock (_gate) return _auditIntegrity.ToArray(); } }
    public IReadOnlyList<ConfigVersionDto> ConfigVersions { get { lock (_gate) return _configs.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<IncidentRelationshipDto> IncidentRelationships { get { lock (_gate) return _incidentRelationships.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<OperationalGroupDto> OperationalGroups { get { lock (_gate) return _operationalGroups.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<CommandAssignmentDto> Commands { get { lock (_gate) return _commands.Values.OrderByDescending(x => x.EffectiveAtUtc).ToArray(); } }
    public IReadOnlyList<MutualAidRequestDto> MutualAid { get { lock (_gate) return _mutualAid.Values.OrderByDescending(x => x.RequestedAtUtc).ToArray(); } }
    public IReadOnlyList<MaydayEventDto> Maydays { get { lock (_gate) return _maydays.Values.OrderByDescending(x => x.DeclaredAtUtc).ToArray(); } }
    public IReadOnlyList<ResponsePlanDto> ResponsePlans { get { lock (_gate) return _responsePlans.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<WorkspaceLayoutDto> Workspaces { get { lock (_gate) return _workspaces.Values.OrderByDescending(x => x.UpdatedAtUtc).ToArray(); } }
    public IReadOnlyList<ThemePackageDto> Themes { get { lock (_gate) return _themes.Values.OrderBy(x => x.DisplayName).ToArray(); } }
    public IReadOnlyList<CollaborativeNarrativeOperationDto> CollaborativeNarrativeOperations { get { lock (_gate) return _collaborativeNarrativeOperations.Values.SelectMany(x => x).OrderBy(x => x.AppliedAtUtc).ToArray(); } }

    public WorkspaceLayoutDto SaveWorkspace(Guid? id, SaveWorkspaceLayoutRequest request)
    {
        lock (_gate)
        {
            var key = id ?? Guid.NewGuid();
            if (request.IsDefault)
            {
                foreach (var existing in _workspaces.Values.Where(x => x.UserId.Equals(request.UserId, StringComparison.OrdinalIgnoreCase) && x.Role.Equals(request.Role, StringComparison.OrdinalIgnoreCase) && x.IsDefault).ToArray())
                    _workspaces[existing.Id] = existing with { IsDefault = false, UpdatedAtUtc = DateTimeOffset.UtcNow };
            }
            var theme = string.IsNullOrWhiteSpace(request.Theme) ? "Classic" : request.Theme.Trim();
            var density = request.Density.Trim().ToLowerInvariant() switch { "compact" => "Compact", "comfortable" => "Comfortable", _ => "Standard" };
            var item = new WorkspaceLayoutDto(key, request.AgencyId, request.UserId.Trim(), request.Name.Trim(), request.Role.Trim(), request.WorkstationId.Trim(), theme, density, request.LayoutJson, request.IsDefault, DateTimeOffset.UtcNow);
            _workspaces[key] = item;
            AuditMutation(item.AgencyId, item.UserId, id is null ? "WorkspaceCreated" : "WorkspaceUpdated", "Workspace", key.ToString(), item.Name);
            return item;
        }
    }

    public IReadOnlyList<WorkspaceLayoutDto> GetWorkspaces(string userId, string? workstationId = null)
    {
        lock (_gate)
        {
            IEnumerable<WorkspaceLayoutDto> source = _workspaces.Values.Where(x => x.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(workstationId)) source = source.Where(x => string.IsNullOrWhiteSpace(x.WorkstationId) || x.WorkstationId.Equals(workstationId, StringComparison.OrdinalIgnoreCase));
            return source.OrderByDescending(x => x.WorkstationId.Equals(workstationId, StringComparison.OrdinalIgnoreCase)).ThenByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).ToArray();
        }
    }

    public ThemePackageDto UpsertTheme(Guid? id, UpsertThemePackageRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.ContractVersion <= 0) throw new ArgumentException("Theme contract version is required.");
            var key = id ?? Guid.NewGuid();
            var item = new ThemePackageDto(key, request.AgencyId, request.Key.Trim(), request.DisplayName.Trim(), request.ContractVersion, "Enabled", request.ResourcesJson, DateTimeOffset.UtcNow);
            _themes[key] = item;
            AuditMutation(item.AgencyId, actor, id is null ? "ThemeInstalled" : "ThemeUpdated", "Theme", key.ToString(), item.DisplayName);
            return item;
        }
    }

    public IReadOnlyList<DuplicateCandidateDto> FindDuplicates(DuplicateCheckRequest request)
    {
        lock (_gate)
        {
            var address = request.Address.Trim();
            var nature = request.Nature.Trim();
            var phone = request.CallerPhone.Trim();
            var candidates = new List<DuplicateCandidateDto>();
            foreach (var incident in _core.GetIncidents(openOnly: true).Where(x => x.AgencyId == request.AgencyId))
            {
                double score = 0;
                var reasons = new List<string>();
                if (!string.IsNullOrWhiteSpace(address) && incident.Address.Equals(address, StringComparison.OrdinalIgnoreCase)) { score += .55; reasons.Add("same address"); }
                else if (!string.IsNullOrWhiteSpace(address) && (incident.Address.Contains(address, StringComparison.OrdinalIgnoreCase) || address.Contains(incident.Address, StringComparison.OrdinalIgnoreCase))) { score += .35; reasons.Add("similar address"); }
                if (!string.IsNullOrWhiteSpace(nature) && incident.Nature.Equals(nature, StringComparison.OrdinalIgnoreCase)) { score += .25; reasons.Add("same nature"); }
                if (!string.IsNullOrWhiteSpace(phone) && incident.CallerPhone.Equals(phone, StringComparison.OrdinalIgnoreCase)) { score += .35; reasons.Add("same callback"); }
                if ((DateTimeOffset.UtcNow - incident.ReceivedAtUtc) <= TimeSpan.FromMinutes(20)) { score += .10; reasons.Add("recent incident"); }
                if (score >= .35) candidates.Add(new DuplicateCandidateDto(incident.Id, incident.Number, incident.Nature, incident.Address, Math.Min(1, score), string.Join(", ", reasons)));
            }
            return candidates.OrderByDescending(x => x.Score).ToArray();
        }
    }

    public ResponseRecommendationDto RecommendResponse(ResponseRecommendationRequest request)
    {
        lock (_gate)
        {
            _core.GetIncident(request.IncidentId);
            var requiredTypes = _responsePlans.Values
                .Where(x => x.Enabled && (x.NaturePattern == "*" || request.Nature.Contains(x.NaturePattern, StringComparison.OrdinalIgnoreCase)) && (x.Priority == "*" || x.Priority.Equals(request.Priority, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault()?.RequiredUnitTypes ?? [];
            var units = _core.Units.Where(x => x.AgencyId == request.AgencyId && x.Status == "Available").Select(unit =>
            {
                double score = 0;
                var reasons = new List<string>();
                if (unit.StationId == request.StationId) { score += 50; reasons.Add("home/closest configured station"); }
                if (requiredTypes.Any(t => unit.UnitType.Contains(t, StringComparison.OrdinalIgnoreCase))) { score += 35; reasons.Add("response-plan unit type"); }
                if (request.RequiredCapabilities.Any(c => unit.UnitType.Contains(c, StringComparison.OrdinalIgnoreCase))) { score += 20; reasons.Add("requested capability match"); }
                if (unit.Discipline.Equals("Fire", StringComparison.OrdinalIgnoreCase)) score += 5;
                return new RecommendedUnitDto(unit.Id, unit.CallSign, unit.UnitType, score, reasons);
            }).OrderByDescending(x => x.Score).ThenBy(x => x.CallSign).Take(12).ToArray();
            var warnings = new List<string>();
            foreach (var required in requiredTypes)
                if (!units.Any(x => x.UnitType.Contains(required, StringComparison.OrdinalIgnoreCase))) warnings.Add($"No available {required} unit in current resource pool.");
            return new ResponseRecommendationDto(request.IncidentId, units, warnings, DateTimeOffset.UtcNow);
        }
    }

    public ResponsePlanDto UpsertResponsePlan(Guid? id, UpsertResponsePlanRequest request)
    {
        lock (_gate)
        {
            var key = id ?? Guid.NewGuid();
            var version = id is Guid existingId && _responsePlans.TryGetValue(existingId, out var existing) ? existing.Version + 1 : 1;
            var item = new ResponsePlanDto(key, request.AgencyId, request.Name.Trim(), string.IsNullOrWhiteSpace(request.NaturePattern) ? "*" : request.NaturePattern.Trim(), string.IsNullOrWhiteSpace(request.Priority) ? "*" : request.Priority.Trim(), request.RequiredUnitTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), request.RequiredCapabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), request.Enabled, version, "Published", request.Actor, DateTimeOffset.UtcNow);
            _responsePlans[key] = item;
            AuditMutation(item.AgencyId, request.Actor, "ResponsePlanPublished", "ResponsePlan", key.ToString(), $"{item.Name} v{item.Version}");
            return item;
        }
    }

    public IncidentRelationshipDto CreateIncidentRelationship(Guid sourceIncidentId, CreateIncidentRelationshipRequest request)
    {
        lock (_gate)
        {
            var source = _core.GetIncident(sourceIncidentId);
            var target = _core.GetIncident(request.TargetIncidentId);
            if (sourceIncidentId == request.TargetIncidentId) throw new ArgumentException("An incident cannot relate to itself.");
            if (source.AgencyId != target.AgencyId) throw new InvalidOperationException("Incidents from different agencies cannot be directly related by this workflow.");
            if (request.AgencyId != source.AgencyId) throw new InvalidOperationException("Relationship agency does not match the source incident.");
            var allowed = new[] { "Duplicate", "MergedInto", "SplitFrom", "SplitTo", "Related", "Rekindle", "Transfer", "MutualAid" };
            if (!allowed.Contains(request.RelationshipType, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported incident relationship type.");
            var item = new IncidentRelationshipDto(Guid.NewGuid(), request.AgencyId, sourceIncidentId, request.TargetIncidentId, allowed.First(x => x.Equals(request.RelationshipType, StringComparison.OrdinalIgnoreCase)), request.Actor, DateTimeOffset.UtcNow);
            _incidentRelationships[item.Id] = item;
            AuditMutation(item.AgencyId, request.Actor, "IncidentRelationshipCreated", "IncidentRelationship", item.Id.ToString(), $"{sourceIncidentId} {item.RelationshipType} {item.TargetIncidentId}");
            return item;
        }
    }

    public IncidentDto MergeIncidents(Guid sourceIncidentId, MergeIncidentsRequest request)
    {
        if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { TargetId = sourceIncidentId, Request = request }, out var prior)) return prior;
        lock (_gate)
        {
            var source = _core.GetIncident(sourceIncidentId);
            var target = _core.GetIncident(request.TargetIncidentId);
            if (source.AgencyId != target.AgencyId) throw new InvalidOperationException("Incidents from different agencies cannot be merged.");
            if (source.Version != request.ExpectedSourceVersion) throw new InvalidOperationException($"Concurrency conflict. Current version is {source.Version}; expected {request.ExpectedSourceVersion}.");
            if (target.Version != request.ExpectedTargetVersion) throw new InvalidOperationException($"Concurrency conflict. Current version is {target.Version}; expected {request.ExpectedTargetVersion}.");
            CreateIncidentRelationship(sourceIncidentId, new CreateIncidentRelationshipRequest(source.AgencyId, target.Id, "MergedInto", request.Actor));
            foreach (var unit in _core.Units.Where(x => x.IncidentId == source.Id).ToArray())
            {
                target = _core.AssignUnit(target.Id, new AssignUnitRequest(unit.Id, target.Version, true, $"merge:{request.IdempotencyKey}:{unit.Id:D}"), request.Actor);
            }
            var closed = _core.CloseIncident(source.Id, new CloseIncidentRequest(source.Version, $"Merged into {target.Number}: {request.Reason}", $"merge-close:{request.IdempotencyKey}"), request.Actor);
            AuditMutation(source.AgencyId, request.Actor, "IncidentsMerged", "Incident", source.Id.ToString(), $"{source.Number} -> {target.Number}");
            Remember(request.IdempotencyKey, new { TargetId = sourceIncidentId, Request = request }, closed);
            return closed;
        }
    }

    public IncidentDto SplitIncident(Guid sourceIncidentId, SplitIncidentRequest request)
    {
        if (TryIdempotent<IncidentDto>(request.IdempotencyKey, new { TargetId = sourceIncidentId, Request = request }, out var prior)) return prior;
        lock (_gate)
        {
            var source = _core.GetIncident(sourceIncidentId);
            if (source.Version != request.ExpectedVersion) throw new InvalidOperationException($"Concurrency conflict. Current version is {source.Version}; expected {request.ExpectedVersion}.");
            var created = _core.CreateIncident(new CreateIncidentRequest(source.AgencyId, source.StationId, request.Nature, request.Priority, request.Address, source.CrossStreets, source.Municipality, source.CallerName, source.CallerPhone, request.Notes, $"split-create:{request.IdempotencyKey}"), request.Actor);
            CreateIncidentRelationship(created.Id, new CreateIncidentRelationshipRequest(source.AgencyId, source.Id, "SplitFrom", request.Actor));
            AuditMutation(source.AgencyId, request.Actor, "IncidentSplit", "Incident", source.Id.ToString(), $"Created {created.Number}");
            Remember(request.IdempotencyKey, new { TargetId = sourceIncidentId, Request = request }, created);
            return created;
        }
    }

    public OperationalGroupDto CreateOperationalGroup(CreateOperationalGroupRequest request, string actor)
    {
        lock (_gate)
        {
            var incident = _core.GetIncident(request.IncidentId);
            if (incident.AgencyId != request.AgencyId) throw new InvalidOperationException("Operational group agency must match the incident agency.");
            var members = NormalizeStrings(request.MemberUserIds);
            foreach (var member in members)
            {
                if (!_users.TryGetValue(member, out var user)) throw new InvalidOperationException($"Operational group member '{member}' is not a provisioned DISRoute user.");
                if (user.AgencyId != request.AgencyId) throw new InvalidOperationException($"Operational group member '{member}' belongs to a different agency.");
            }
            if (_users.TryGetValue(request.Leader.Trim(), out var leaderUser) && leaderUser.AgencyId == request.AgencyId) AddUnique(members, request.Leader.Trim());
            var item = new OperationalGroupDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.Name.Trim(), request.Type.Trim(), request.Leader.Trim(), request.Channel.Trim(), "Active", DateTimeOffset.UtcNow, members);
            _operationalGroups[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "OperationalGroupCreated", "OperationalGroup", item.Id.ToString(), $"{item.Name}; members={members.Count}");
            return item;
        }
    }

    public OperationalGroupDto SetOperationalGroupMembers(Guid id, UpdateOperationalGroupMembersRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_operationalGroups.TryGetValue(id, out var current)) throw new KeyNotFoundException("Operational group not found.");
            var actorAgency = ResolveActorAgency(actor);
            if (actorAgency != current.AgencyId) throw new UnauthorizedAccessException("Operational group membership may only be changed within the signed-in user's agency.");
            if (!current.Leader.Equals(actor, StringComparison.OrdinalIgnoreCase) && !ActorHasRole(actor, "Supervisor") && !ActorHasRole(actor, "Administrator"))
                throw new UnauthorizedAccessException("Only the group leader or a supervisor/administrator may change operational group membership.");
            var members = NormalizeStrings(request.MemberUserIds);
            foreach (var member in members)
            {
                if (!_users.TryGetValue(member, out var user)) throw new InvalidOperationException($"Operational group member '{member}' is not a provisioned DISRoute user.");
                if (user.AgencyId != current.AgencyId) throw new InvalidOperationException($"Operational group member '{member}' belongs to a different agency.");
            }
            if (_users.TryGetValue(current.Leader, out var leaderUser) && leaderUser.AgencyId == current.AgencyId) AddUnique(members, current.Leader);
            var updated = current with { MemberUserIds = members };
            _operationalGroups[id] = updated;
            AuditMutation(updated.AgencyId, actor, "OperationalGroupMembershipChanged", "OperationalGroup", id.ToString(), $"{updated.Name}; members={members.Count}");
            return updated;
        }
    }

    public CommandAssignmentDto SetCommand(SetCommandRequest request)
    {
        lock (_gate)
        {
            _core.GetIncident(request.IncidentId);
            foreach (var active in _commands.Values.Where(x => x.IncidentId == request.IncidentId && x.EndedAtUtc is null).ToArray()) _commands[active.Id] = active with { EndedAtUtc = DateTimeOffset.UtcNow };
            var item = new CommandAssignmentDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.CommandName.Trim(), request.UnitId, request.Person.Trim(), request.Actor, DateTimeOffset.UtcNow, null);
            _commands[item.Id] = item;
            AuditMutation(item.AgencyId, request.Actor, "CommandChanged", "Incident", request.IncidentId.ToString(), $"{item.CommandName} {item.Person}");
            return item;
        }
    }

    public MutualAidRequestDto CreateMutualAid(CreateMutualAidRequest request)
    {
        lock (_gate)
        {
            _core.GetIncident(request.IncidentId);
            var now = DateTimeOffset.UtcNow;
            var item = new MutualAidRequestDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.RequestedAgency.Trim(), request.ResourceType.Trim(), Math.Max(1, request.Quantity), "Requested", request.RequestedBy.Trim(), now, now);
            _mutualAid[item.Id] = item;
            AuditMutation(item.AgencyId, item.RequestedBy, "MutualAidRequested", "MutualAid", item.Id.ToString(), $"{item.Quantity} {item.ResourceType} from {item.RequestedAgency}");
            return item;
        }
    }

    public MutualAidRequestDto UpdateMutualAid(Guid id, string state, string actor)
    {
        lock (_gate)
        {
            if (!_mutualAid.TryGetValue(id, out var current)) throw new KeyNotFoundException("Mutual aid request not found.");
            var allowed = new[] { "Requested", "Accepted", "EnRoute", "Arrived", "Declined", "Canceled", "Completed" };
            var normalized = allowed.FirstOrDefault(x => x.Equals(state, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Unsupported mutual-aid state.");
            var updated = current with { State = normalized, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _mutualAid[id] = updated;
            AuditMutation(updated.AgencyId, actor, "MutualAidStateChanged", "MutualAid", id.ToString(), normalized);
            return updated;
        }
    }

    public MaydayEventDto DeclareMayday(DeclareMaydayRequest request)
    {
        lock (_gate)
        {
            var incident = _core.GetIncident(request.IncidentId);
            if (incident.AgencyId != request.AgencyId) throw new InvalidOperationException("Mayday agency must match the incident agency.");
            if (request.UnitId is Guid unitId)
            {
                var unit = _core.Units.FirstOrDefault(x => x.Id == unitId) ?? throw new KeyNotFoundException("Mayday unit not found.");
                if (unit.AgencyId != incident.AgencyId) throw new InvalidOperationException("Mayday unit must belong to the incident agency.");
            }
            RequireText(request.Person, nameof(request.Person));
            RequireText(request.Location, nameof(request.Location));
            var actor = string.IsNullOrWhiteSpace(request.DeclaredBy) ? "unknown" : request.DeclaredBy.Trim();
            var now = DateTimeOffset.UtcNow;
            var item = new MaydayEventDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.UnitId, request.Person.Trim(), request.Location.Trim(), request.Channel.Trim(), "Active", actor, now, null, null, null, null, 0, null, now);
            _maydays[item.Id] = item;
            CreateAlert(new CreateAlertRequest(request.AgencyId, request.IncidentId, "Mayday", "Critical", $"mayday:{item.Id:D}", request.Person, $"MAYDAY — {request.Person} — {request.Location}"), actor);
            AuditMutation(item.AgencyId, actor, "MaydayDeclared", "Mayday", item.Id.ToString(), $"{item.Person} at {item.Location}");
            return item;
        }
    }

    public MaydayEventDto ActOnMayday(Guid id, string action, string actor, MaydayActionRequest request)
    {
        lock (_gate)
        {
            if (!_maydays.TryGetValue(id, out var current)) throw new KeyNotFoundException("Mayday event not found.");
            if (current.State == "Resolved") throw new InvalidOperationException("Resolved Maydays cannot be modified.");
            var now = DateTimeOffset.UtcNow;
            var normalized = action.Trim().ToLowerInvariant();
            var updated = normalized switch
            {
                "acknowledge" => current with { State = current.State == "Active" ? "Acknowledged" : current.State, AcknowledgedBy = actor, AcknowledgedAtUtc = current.AcknowledgedAtUtc ?? now, Notes = MergeNotes(current.Notes, request.Notes), UpdatedAtUtc = now },
                "assign" => current with { State = "Assigned", AssignedTo = RequireActionValue(request.AssignedTo, "AssignedTo"), AcknowledgedBy = current.AcknowledgedBy ?? actor, AcknowledgedAtUtc = current.AcknowledgedAtUtc ?? now, Notes = MergeNotes(current.Notes, request.Notes), UpdatedAtUtc = now },
                "escalate" => current with { State = "Escalated", EscalationLevel = current.EscalationLevel + 1, Notes = MergeNotes(current.Notes, request.Notes), UpdatedAtUtc = now },
                "resolve" => current with { State = "Resolved", ResolvedAtUtc = now, Notes = MergeNotes(current.Notes, request.Notes), UpdatedAtUtc = now },
                _ => throw new ArgumentException("Unsupported Mayday action.")
            };
            _maydays[id] = updated;
            var alert = _alerts.Values.FirstOrDefault(x => x.CorrelationKey.Equals($"mayday:{id:D}", StringComparison.OrdinalIgnoreCase));
            if (alert is not null && normalized != "escalate")
            {
                var alertAction = normalized switch { "acknowledge" => "acknowledge", "assign" => "claim", "resolve" => "resolve", _ => normalized };
                ActOnAlert(alert.Id, alertAction, new AlertActionRequest(actor, request.Notes));
            }
            AuditMutation(updated.AgencyId, actor, $"Mayday{char.ToUpperInvariant(normalized[0])}{normalized[1..]}", "Mayday", id.ToString(), request.Notes ?? updated.Person);
            return updated;
        }
    }

    public MaydayEventDto ResolveMayday(Guid id, string actor) => ActOnMayday(id, "resolve", actor, new MaydayActionRequest(null, null));

    private static string RequireActionValue(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();
    private static string? MergeNotes(string? existing, string? addition)
    {
        if (string.IsNullOrWhiteSpace(addition)) return existing;
        return string.IsNullOrWhiteSpace(existing) ? addition.Trim() : existing + " | " + addition.Trim();
    }

    public CallRecordDto CreateCall(CreateCallRecordRequest request, string actor)
    {
        lock (_gate)
        {
            RequireText(request.Source, nameof(request.Source));
            var now = DateTimeOffset.UtcNow;
            var item = new CallRecordDto(Guid.NewGuid(), request.AgencyId, request.Source.Trim(), request.ProviderCallId.Trim(), request.CallerName.Trim(), request.CallbackNumber.Trim(), request.SuppliedLocation.Trim(), request.Nature.Trim(), "Open", request.IncidentId, now, null, null);
            _calls[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "CallCreated", "CallRecord", item.Id.ToString(), $"{item.Source} {item.ProviderCallId}");
            return item;
        }
    }

    public CallRecordDto LinkCall(Guid id, LinkCallRequest request)
    {
        lock (_gate)
        {
            if (!_calls.TryGetValue(id, out var current)) throw new KeyNotFoundException("Call record not found.");
            _core.GetIncident(request.IncidentId);
            var updated = current with { IncidentId = request.IncidentId, Disposition = "Attached" };
            _calls[id] = updated;
            AuditMutation(updated.AgencyId, request.Actor, "CallLinked", "CallRecord", id.ToString(), $"Incident {request.IncidentId}");
            return updated;
        }
    }

    public (DispatchActionDto Action, IReadOnlyList<DispatchDeliveryDto> Deliveries, IncidentDto Incident) CommitDispatch(Guid incidentId, CommitDispatchRequest request, string actor)
    {
        if (TryIdempotent<(DispatchActionDto, IReadOnlyList<DispatchDeliveryDto>, IncidentDto)>(request.IdempotencyKey, new { TargetId = incidentId, Actor = actor, Request = request }, out var prior)) return prior;
        lock (_gate)
        {
            var incident = _core.GetIncident(incidentId);
            if (incident.Version != request.ExpectedIncidentVersion) throw new InvalidOperationException($"Concurrency conflict. Current version is {incident.Version}; expected {request.ExpectedIncidentVersion}.");
            if (request.UnitIds.Count == 0) throw new ArgumentException("At least one unit is required.");
            foreach (var unitId in request.UnitIds)
            {
                incident = _core.AssignUnit(incidentId, new AssignUnitRequest(unitId, incident.Version, true, $"dispatch:{request.IdempotencyKey}:{unitId:D}"), actor);
            }

            var now = DateTimeOffset.UtcNow;
            var action = new DispatchActionDto(Guid.NewGuid(), incident.AgencyId, incidentId, request.UnitIds.Distinct().ToArray(), "Committed", now, actor);
            _dispatchActions[action.Id] = action;
            CaptureDispatchActionState(action, "DispatchCommitted", actor);
            var providers = request.DeliveryProviders.Count == 0 ? new[] { "StationDisplay", "RunSheet" } : request.DeliveryProviders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var deliveries = new List<DispatchDeliveryDto>();
            foreach (var provider in providers)
            {
                var delivery = new DispatchDeliveryDto(Guid.NewGuid(), action.Id, provider, DestinationFor(provider, incident), "Pending", 0, null, now);
                _dispatchDeliveries[delivery.Id] = delivery;
                CaptureDispatchDeliveryState(delivery, incidentId, "DispatchCommitted", actor);
                deliveries.Add(delivery);
            }

            var stationDelivery = new StationDeliveryDto(Guid.NewGuid(), incident.AgencyId, incident.StationId, incidentId, action.Id, "Pending", providers.Contains("StationAlert", StringComparer.OrdinalIgnoreCase) ? "Pending" : "NotConfigured", providers.Contains("RunSheet", StringComparer.OrdinalIgnoreCase) ? "Pending" : "NotConfigured", "Pending", 0, now);
            _stationDeliveries[stationDelivery.Id] = stationDelivery;
            CaptureStationDeliveryState(stationDelivery, "DispatchCommitted", actor);
            var runSheet = new RunSheetDto(Guid.NewGuid(), incident.AgencyId, incident.StationId, incidentId, 1, "Generated", RenderRunSheet(incident, request.UnitIds), now, null);
            _runSheets[runSheet.Id] = runSheet;
            AuditMutation(incident.AgencyId, actor, "DispatchCommitted", "DispatchAction", action.Id.ToString(), $"{incident.Number}; units={string.Join(',', request.UnitIds)}");
            var result = (action, (IReadOnlyList<DispatchDeliveryDto>)deliveries, incident);
            Remember(request.IdempotencyKey, new { TargetId = incidentId, Actor = actor, Request = request }, result);
            return result;
        }
    }

    public DispatchDeliveryDto RetryDelivery(Guid deliveryId, RetryDeliveryRequest request)
    {
        lock (_gate)
        {
            if (!_dispatchDeliveries.TryGetValue(deliveryId, out var current)) throw new KeyNotFoundException("Delivery not found.");
            if (!string.Equals(current.State, "Failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Only failed deliveries can be retried. Current state is '{current.State}'.");
            var updated = current with { State = "Pending", Attempts = current.Attempts + 1, LastError = null, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _dispatchDeliveries[deliveryId] = updated;
            var action = _dispatchActions[current.DispatchActionId];
            CaptureDispatchDeliveryState(updated, action.IncidentId, "DeliveryRetried", request.Actor);
            AuditMutation(action.AgencyId, request.Actor, "DeliveryRetried", "DispatchDelivery", deliveryId.ToString(), current.Provider);
            return updated;
        }
    }

    public DispatchDeliveryDto CompleteDelivery(Guid deliveryId, bool success, string? error, string actor)
    {
        lock (_gate)
        {
            if (!_dispatchDeliveries.TryGetValue(deliveryId, out var current)) throw new KeyNotFoundException("Delivery not found.");
            var updated = current with { State = success ? "Confirmed" : "Failed", Attempts = current.Attempts + 1, LastError = success ? null : error, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _dispatchDeliveries[deliveryId] = updated;
            var action = _dispatchActions[current.DispatchActionId];
            CaptureDispatchDeliveryState(updated, action.IncidentId, success ? "DeliveryConfirmed" : "DeliveryFailed", actor);
            foreach (var station in _stationDeliveries.Values.Where(x => x.DispatchActionId == action.Id).ToArray())
            {
                var reconciled = current.Provider.ToLowerInvariant() switch
                {
                    "stationdisplay" => station with { DisplayState = success ? "Received" : "Failed", Attempt = station.Attempt + 1, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    "stationalert" => station with { AlertState = success ? "Confirmed" : "Failed", Attempt = station.Attempt + 1, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    "runsheet" => station with { PrintState = success ? "Completed" : "Failed", Attempt = station.Attempt + 1, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    _ => station
                };
                _stationDeliveries[station.Id] = reconciled;
                if (!Equals(reconciled, station)) CaptureStationDeliveryState(reconciled, success ? "DeliveryConfirmed" : "DeliveryFailed", actor);
            }
            AuditMutation(action.AgencyId, actor, success ? "DeliveryConfirmed" : "DeliveryFailed", "DispatchDelivery", deliveryId.ToString(), current.Provider);
            return updated;
        }
    }

    public FireWatchDto CreateFireWatch(CreateFireWatchRequest request, string actor)
    {
        lock (_gate)
        {
            _core.GetIncident(request.IncidentId);
            if (!_core.Units.Any(x => x.Id == request.UnitId)) throw new KeyNotFoundException("Unit not found.");
            var item = new FireWatchDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.UnitId, request.Supervisor.Trim(), DateTimeOffset.UtcNow, request.ExpectedEndUtc, Math.Max(1, request.CheckIntervalMinutes), "Active", request.Notes.Trim());
            _fireWatches[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "FireWatchStarted", "FireWatch", item.Id.ToString(), item.Notes);
            return item;
        }
    }

    public FireWatchCheckDto AddFireWatchCheck(Guid id, string actor, string notes)
    {
        lock (_gate)
        {
            if (!_fireWatches.ContainsKey(id)) throw new KeyNotFoundException("Fire watch not found.");
            var check = new FireWatchCheckDto(Guid.NewGuid(), id, actor, notes.Trim(), DateTimeOffset.UtcNow);
            if (!_fireWatchChecks.TryGetValue(id, out var list)) _fireWatchChecks[id] = list = [];
            list.Add(check);
            return check;
        }
    }

    public IReadOnlyList<FireWatchCheckDto> GetFireWatchChecks(Guid id)
    {
        lock (_gate) return _fireWatchChecks.TryGetValue(id, out var list) ? list.OrderByDescending(x => x.CheckedAtUtc).ToArray() : [];
    }

    public FireWatchDto EndFireWatch(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_fireWatches.TryGetValue(id, out var current)) throw new KeyNotFoundException("Fire watch not found.");
            var updated = current with { Status = "Completed", ExpectedEndUtc = DateTimeOffset.UtcNow };
            _fireWatches[id] = updated;
            AuditMutation(updated.AgencyId, actor, "FireWatchEnded", "FireWatch", id.ToString(), "Completed");
            return updated;
        }
    }

    public RelocationDto CreateRelocation(CreateRelocationRequest request, string actor)
    {
        lock (_gate)
        {
            var unit = _core.Units.FirstOrDefault(x => x.Id == request.UnitId) ?? throw new KeyNotFoundException("Unit not found.");
            if (!_core.Stations.Any(x => x.Id == request.DestinationStationId)) throw new KeyNotFoundException("Destination station not found.");
            var item = new RelocationDto(Guid.NewGuid(), request.AgencyId, unit.Id, request.DestinationStationId, request.Reason.Trim(), "Active", DateTimeOffset.UtcNow, null);
            _relocations[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "RelocationStarted", "Relocation", item.Id.ToString(), request.Reason);
            return item;
        }
    }

    public RelocationDto EndRelocation(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_relocations.TryGetValue(id, out var current)) throw new KeyNotFoundException("Relocation not found.");
            var updated = current with { Status = "Completed", EndedAtUtc = DateTimeOffset.UtcNow };
            _relocations[id] = updated;
            AuditMutation(updated.AgencyId, actor, "RelocationEnded", "Relocation", id.ToString(), current.Reason);
            return updated;
        }
    }

    public PremiseDto UpsertPremise(Guid? id, UpsertPremiseRequest request, string actor)
    {
        lock (_gate)
        {
            RequireText(request.Address, nameof(request.Address));
            var key = id ?? Guid.NewGuid();
            var item = new PremiseDto(key, request.AgencyId, request.Name.Trim(), request.Address.Trim(), request.Occupancy.Trim(), request.AccessInformation.Trim(), request.FireProtection.Trim(), request.UtilityShutoffs.Trim(), request.Hazards.Trim(), NormalizeClassification(request.Classification), DateTimeOffset.UtcNow);
            _premises[key] = item;
            AuditMutation(item.AgencyId, actor, id is null ? "PremiseCreated" : "PremiseUpdated", "Premise", key.ToString(), item.Address);
            return item;
        }
    }

    public PreplanDto CreatePreplan(CreatePreplanRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_premises.ContainsKey(request.PremiseId)) throw new KeyNotFoundException("Premise not found.");
            var version = _preplans.Values.Where(x => x.PremiseId == request.PremiseId).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
            var item = new PreplanDto(Guid.NewGuid(), request.AgencyId, request.PremiseId, version, "Draft", request.Title.Trim(), request.OperationalContent, request.Author.Trim(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            _preplans[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "PreplanCreated", "Preplan", item.Id.ToString(), item.Title);
            return item;
        }
    }

    public PreplanDto PublishPreplan(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_preplans.TryGetValue(id, out var current)) throw new KeyNotFoundException("Preplan not found.");
            foreach (var old in _preplans.Values.Where(x => x.PremiseId == current.PremiseId && x.Status == "Published" && x.Id != id).ToArray()) _preplans[old.Id] = old with { Status = "Retired", UpdatedAtUtc = DateTimeOffset.UtcNow };
            var updated = current with { Status = "Published", EffectiveAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _preplans[id] = updated;
            AuditMutation(updated.AgencyId, actor, "PreplanPublished", "Preplan", id.ToString(), updated.Title);
            return updated;
        }
    }

    public CrgAssetDto RegisterCrg(RegisterCrgAssetRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_premises.ContainsKey(request.PremiseId)) throw new KeyNotFoundException("Premise not found.");
            var item = new CrgAssetDto(Guid.NewGuid(), request.AgencyId, request.PremiseId, request.FileName.Trim(), request.Sha256.Trim(), NullIfBlank(request.Floor), NullIfBlank(request.OverlayType), request.Revision, request.Rotation, request.Scale, request.Opacity, request.Source.Trim(), DateTimeOffset.UtcNow);
            _crgs[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "CrgRegistered", "CRGAsset", item.Id.ToString(), item.FileName);
            return item;
        }
    }

    public HydrantDto UpsertHydrant(Guid? id, UpsertHydrantRequest request, string actor)
    {
        lock (_gate)
        {
            var key = id ?? Guid.NewGuid();
            var item = new HydrantDto(key, request.AgencyId, request.AssetId.Trim(), request.Address.Trim(), request.Latitude, request.Longitude, request.MainSizeInches, request.StaticPsi, request.ResidualPsi, request.FlowGpm, request.Status.Trim(), DateTimeOffset.UtcNow);
            _hydrants[key] = item;
            AuditMutation(item.AgencyId, actor, id is null ? "HydrantCreated" : "HydrantUpdated", "Hydrant", key.ToString(), item.AssetId);
            return item;
        }
    }

    public MapObjectDto CreateMapObject(CreateMapObjectRequest request, string actor)
    {
        lock (_gate)
        {
            if (request.IncidentId is Guid incidentId) _core.GetIncident(incidentId);
            var item = new MapObjectDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.Kind.Trim(), request.Name.Trim(), request.GeometryGeoJson.Trim(), request.VisibilityScope.Trim(), DateTimeOffset.UtcNow, request.EndsAtUtc, actor, 1);
            _mapObjects[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "MapObjectCreated", "MapObject", item.Id.ToString(), $"{item.Kind}: {item.Name}");
            return item;
        }
    }

    public IncidentSiteIntelligenceDto GetSiteIntelligence(Guid incidentId)
    {
        lock (_gate)
        {
            var incident = _core.GetIncident(incidentId);
            var now = DateTimeOffset.UtcNow;
            var warnings = new List<string>();
            var normalizedIncidentAddress = NormalizeLocationText(incident.Address);

            var premises = _premises.Values
                .Where(x => x.AgencyId == incident.AgencyId)
                .Select(x => new { Premise = x, Score = AddressMatchScore(normalizedIncidentAddress, NormalizeLocationText(x.Address)) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Premise.Address, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(x => x.Premise)
                .ToArray();

            var premiseIds = premises.Select(x => x.Id).ToHashSet();
            var preplans = _preplans.Values
                .Where(x => premiseIds.Contains(x.PremiseId) && x.Status == "Published")
                .OrderByDescending(x => x.EffectiveAtUtc)
                .ToArray();
            var crgs = _crgs.Values
                .Where(x => premiseIds.Contains(x.PremiseId))
                .OrderBy(x => x.Floor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.Revision ?? 0)
                .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var mapObjects = _mapObjects.Values
                .Where(x => x.AgencyId == incident.AgencyId && (x.IncidentId is null || x.IncidentId == incident.Id) && x.StartsAtUtc <= now && (x.EndsAtUtc is null || x.EndsAtUtc > now))
                .OrderByDescending(x => x.IncidentId == incident.Id)
                .ThenByDescending(x => x.StartsAtUtc)
                .ToArray();

            var scenePoint = mapObjects
                .Where(x => x.IncidentId == incident.Id)
                .OrderByDescending(x => IsSceneLocationKind(x.Kind))
                .ThenByDescending(x => x.StartsAtUtc)
                .Select(x => TryReadGeoJsonPoint(x.GeometryGeoJson, out var latitude, out var longitude) ? (Found: true, Latitude: latitude, Longitude: longitude) : default)
                .FirstOrDefault(x => x.Found);

            SiteHydrantDto[] hydrants;
            SiteRoadRestrictionDto[] roadRestrictions;
            if (scenePoint.Found)
            {
                hydrants = _hydrants.Values
                    .Where(x => x.AgencyId == incident.AgencyId && !string.Equals(x.Status, "OutOfService", StringComparison.OrdinalIgnoreCase))
                    .Select(x => new SiteHydrantDto(x, HaversineMeters(scenePoint.Latitude, scenePoint.Longitude, x.Latitude, x.Longitude)))
                    .Where(x => x.DistanceMeters <= 3_000)
                    .OrderBy(x => x.DistanceMeters)
                    .ThenBy(x => x.Hydrant.AssetId, StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToArray();

                roadRestrictions = _core.RoadRestrictions
                    .Where(x => x.AgencyId == incident.AgencyId && x.Active && x.StartsAtUtc <= now && (x.EndsAtUtc is null || x.EndsAtUtc > now))
                    .Select(x => new SiteRoadRestrictionDto(x, HaversineMeters(scenePoint.Latitude, scenePoint.Longitude, x.Latitude, x.Longitude)))
                    .Where(x => x.DistanceMeters <= 5_000 + x.Restriction.RadiusMeters)
                    .OrderBy(x => x.DistanceMeters)
                    .ThenBy(x => x.Restriction.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            else
            {
                var premiseAddresses = premises.Select(x => NormalizeLocationText(x.Address)).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
                hydrants = _hydrants.Values
                    .Where(x => x.AgencyId == incident.AgencyId && !string.Equals(x.Status, "OutOfService", StringComparison.OrdinalIgnoreCase))
                    .Where(x => premiseAddresses.Contains(NormalizeLocationText(x.Address)))
                    .OrderBy(x => x.AssetId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new SiteHydrantDto(x, null))
                    .ToArray();
                roadRestrictions = _core.RoadRestrictions
                    .Where(x => x.AgencyId == incident.AgencyId && x.Active && x.StartsAtUtc <= now && (x.EndsAtUtc is null || x.EndsAtUtc > now))
                    .OrderByDescending(x => x.StartsAtUtc)
                    .Select(x => new SiteRoadRestrictionDto(x, null))
                    .ToArray();
                warnings.Add("Incident coordinates are not established. Hydrant proximity and road-restriction distance cannot be calculated until an incident Point map object is recorded.");
            }

            if (premises.Length == 0) warnings.Add("No premise record matched the incident address. Verify the location before relying on site-specific preplans or CRG assets.");
            else if (preplans.Length == 0) warnings.Add("A premise record matched, but no published preplan is available for the matched site.");
            if (crgs.Length == 0 && premises.Length > 0) warnings.Add("No CRG/TIFF floor assets are registered for the matched premise.");

            return new IncidentSiteIntelligenceDto(
                incident.Id,
                premises,
                preplans,
                crgs,
                hydrants,
                roadRestrictions,
                mapObjects,
                scenePoint.Found ? scenePoint.Latitude : null,
                scenePoint.Found ? scenePoint.Longitude : null,
                warnings);
        }
    }

    public RouteResultDto CalculateRoute(RouteRequest request)
    {
        lock (_gate)
        {
            var distance = HaversineMeters(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude);
            var warnings = new List<string>();
            if (request.AvoidRestrictions && _core.RoadRestrictions.Any(x => x.Active)) warnings.Add("Active dispatcher road restrictions considered by fallback route estimator; configure a routing provider for turn-by-turn routing.");
            var seconds = (int)Math.Max(30, distance / 13.4); // rough 30 mph fallback, never authoritative
            var line = $"LINESTRING({request.FromLongitude:R} {request.FromLatitude:R},{request.ToLongitude:R} {request.ToLatitude:R})";
            return new RouteResultDto("FallbackGeodesic", distance, seconds, line, warnings, DateTimeOffset.UtcNow);
        }
    }

    public VesselObservationDto UpsertVessel(UpsertVesselObservationRequest request, string actor)
    {
        lock (_gate)
        {
            var item = new VesselObservationDto(Guid.NewGuid(), request.AgencyId, request.Mmsi.Trim(), request.Name.Trim(), request.Latitude, request.Longitude, request.SpeedKnots, request.CourseDegrees, request.Source.Trim(), request.ObservedAtUtc);
            _vessels[item.Mmsi] = item;
            _vesselObservationHistory.Add(item);
            AuditMutation(item.AgencyId, actor, "VesselObservation", "Vessel", item.Mmsi, item.Name);
            return item;
        }
    }

    public AlertDto CreateAlert(CreateAlertRequest request, string actor)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(request.CorrelationKey))
            {
                var existing = _alerts.Values.FirstOrDefault(x => x.CorrelationKey == request.CorrelationKey && x.State != "Resolved");
                if (existing is not null) return existing;
            }
            var now = DateTimeOffset.UtcNow;
            var item = new AlertDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, request.Type.Trim(), NormalizeSeverity(request.Severity), request.CorrelationKey.Trim(), request.AffectedEntity.Trim(), "Open", request.Summary.Trim(), null, now, now);
            _alerts[item.Id] = item;
            CaptureAlertState(item, "AlertCreated", actor);
            AuditMutation(item.AgencyId, actor, "AlertCreated", "Alert", item.Id.ToString(), item.Summary);
            return item;
        }
    }

    public AlertDto ActOnAlert(Guid id, string action, AlertActionRequest request)
    {
        lock (_gate)
        {
            if (!_alerts.TryGetValue(id, out var current)) throw new KeyNotFoundException("Alert not found.");
            var state = action.ToLowerInvariant() switch
            {
                "acknowledge" => "Acknowledged",
                "claim" => "Claimed",
                "resolve" => "Resolved",
                "escalate" => "Escalated",
                _ => throw new ArgumentException("Unsupported alert action.")
            };
            var updated = current with { State = state, ClaimedBy = action.Equals("claim", StringComparison.OrdinalIgnoreCase) ? request.Actor : current.ClaimedBy, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _alerts[id] = updated;
            CaptureAlertState(updated, $"Alert{state}", request.Actor);
            AuditMutation(updated.AgencyId, request.Actor, $"Alert{state}", "Alert", id.ToString(), request.Notes ?? updated.Summary);
            return updated;
        }
    }

    public IReadOnlyList<AlertDto> EscalateUnacknowledgedAlerts(DateTimeOffset now, TimeSpan advisoryAfter, TimeSpan warningAfter)
    {
        lock (_gate)
        {
            var changed = new List<AlertDto>();
            foreach (var current in _alerts.Values.Where(x => x.State == "Open" && !x.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var age = now - current.CreatedAtUtc;
                var severity = age >= warningAfter ? "Warning" : age >= advisoryAfter ? "Advisory" : current.Severity;
                if (severity.Equals(current.Severity, StringComparison.OrdinalIgnoreCase)) continue;
                var updated = current with { Severity = severity, UpdatedAtUtc = now };
                _alerts[current.Id] = updated;
                CaptureAlertState(updated, "AlertProgressiveEscalation", "alert-escalation-worker");
                AuditMutation(updated.AgencyId, "alert-escalation-worker", "AlertProgressiveEscalation", "Alert", updated.Id.ToString(), $"{updated.Summary} -> {severity}");
                changed.Add(updated);
            }
            return changed;
        }
    }

    public BulletinDto CreateBulletin(CreateBulletinRequest request)
    {
        lock (_gate)
        {
            var item = new BulletinDto(Guid.NewGuid(), request.AgencyId, request.Category.Trim(), request.Title.Trim(), request.Body.Trim(), NormalizeSeverity(request.Severity), request.StartsAtUtc, request.EndsAtUtc, request.CreatedBy.Trim());
            _bulletins[item.Id] = item;
            AuditMutation(item.AgencyId, item.CreatedBy, "BulletinCreated", "Bulletin", item.Id.ToString(), item.Title);
            return item;
        }
    }

    public StationDeliveryDto AcknowledgeStationDelivery(Guid id, StationAcknowledgeRequest request)
    {
        lock (_gate)
        {
            if (!_stationDeliveries.TryGetValue(id, out var current)) throw new KeyNotFoundException("Station delivery not found.");
            if (string.Equals(current.AckState, "Acknowledged", StringComparison.OrdinalIgnoreCase)) return current;
            var updated = current with { DisplayState = "Received", AckState = "Acknowledged", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _stationDeliveries[id] = updated;
            CaptureStationDeliveryState(updated, "StationAcknowledged", request.Actor);
            AuditMutation(updated.AgencyId, request.Actor, "StationAcknowledged", "StationDelivery", id.ToString(), request.DeviceId);
            return updated;
        }
    }

    public StationSnapshotDto GetStationSnapshot(Guid stationId)
    {
        lock (_gate)
        {
            var incidents = _core.GetIncidents(openOnly: true).Where(x => x.StationId == stationId).ToArray();
            var units = _core.Units.Where(x => x.StationId == stationId).ToArray();
            var deliveries = _stationDeliveries.Values.Where(x => x.StationId == stationId).ToArray();
            var sheets = _runSheets.Values.Where(x => x.StationId == stationId).ToArray();
            return new StationSnapshotDto(stationId, incidents, units, deliveries, sheets, DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<StationDeliveryPathDto> GetStationDeliveryPaths(Guid stationId)
    {
        lock (_gate)
        {
            var stationDeliveries = _stationDeliveries.Values.Where(x => x.StationId == stationId).ToArray();
            var rows = new List<StationDeliveryPathDto>();
            foreach (var stationDelivery in stationDeliveries)
            {
                foreach (var delivery in _dispatchDeliveries.Values.Where(x => x.DispatchActionId == stationDelivery.DispatchActionId))
                {
                    var provider = delivery.Provider.StartsWith("cancel:", StringComparison.OrdinalIgnoreCase) ? delivery.Provider[7..] : delivery.Provider;
                    if (provider is not ("StationDisplay" or "StationAlert" or "RunSheet")) continue;
                    rows.Add(new StationDeliveryPathDto(delivery.Id, stationDelivery.Id, stationDelivery.DispatchActionId, stationDelivery.IncidentId, delivery.Provider, delivery.Destination, delivery.State, delivery.Attempts, delivery.LastError, delivery.UpdatedAtUtc));
                }
            }
            return rows.OrderByDescending(x => x.UpdatedAtUtc).ToArray();
        }
    }

    public RunSheetDto MarkRunSheetPrinted(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_runSheets.TryGetValue(id, out var current)) throw new KeyNotFoundException("Run sheet not found.");
            if (string.Equals(current.Status, "Printed", StringComparison.OrdinalIgnoreCase)) return current;
            var updated = current with { Status = "Printed", PrintedAtUtc = DateTimeOffset.UtcNow };
            _runSheets[id] = updated;
            AuditMutation(updated.AgencyId, actor, "RunSheetPrinted", "RunSheet", id.ToString(), $"Revision {updated.Revision}");
            return updated;
        }
    }

    public ClientHeartbeatDto Heartbeat(HeartbeatRequest request)
    {
        lock (_gate)
        {
            var key = request.DeviceId.Trim();
            var existingId = _heartbeats.TryGetValue(key, out var existing) ? existing.Id : Guid.NewGuid();
            var state = Math.Abs(request.ClockOffsetSeconds ?? 0) >= 5 ? "ClockWarning" : "Connected";
            var item = new ClientHeartbeatDto(existingId, request.AgencyId, key, request.ClientRole.Trim(), request.Platform.Trim(), request.Version.Trim(), request.User.Trim(), state, request.ClockOffsetSeconds, DateTimeOffset.UtcNow);
            _heartbeats[key] = item;
            if (_devices.TryGetValue(key, out var device)) _devices[key] = device with { LastSeenAtUtc = item.LastSeenUtc, State = "Active" };
            return item;
        }
    }

    public ProviderConfigDto UpsertProvider(string key, UpsertProviderConfigRequest request, string actor)
    {
        lock (_gate)
        {
            var normalizedKey = key.Trim().ToLowerInvariant();
            var version = _providers.TryGetValue(normalizedKey, out var current) ? current.Version + 1 : 1;
            var sanitizedSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in request.Settings)
            {
                var sensitiveKey = pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Contains("api-key", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Contains("privatekey", StringComparison.OrdinalIgnoreCase);
                if (sensitiveKey && !pair.Value.StartsWith("secret://", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Sensitive provider setting '{pair.Key}' must be supplied as a secret:// reference, not plaintext.");
                sanitizedSettings[pair.Key] = pair.Value;
            }
            var item = new ProviderConfigDto(normalizedKey, request.DisplayName.Trim(), request.Category.Trim(), request.Enabled, request.Endpoint.Trim(), request.AuthenticationMode.Trim(), sanitizedSettings, version, request.Enabled ? "NotValidated" : "Disabled");
            _providers[normalizedKey] = item;
            AuditMutation(OperationalStore.DefaultAgencyId, actor, "ProviderConfigured", "Provider", normalizedKey, $"v{version}");
            return item;
        }
    }

    public ProviderConfigDto ValidateProvider(string key, string actor)
    {
        lock (_gate)
        {
            if (!_providers.TryGetValue(key, out var current)) throw new KeyNotFoundException("Provider not found.");
            var state = !current.Enabled ? "Disabled" : string.IsNullOrWhiteSpace(current.Endpoint) ? "NotConfigured" : "Available";
            var updated = current with { State = state };
            _providers[key] = updated;
            AuditMutation(OperationalStore.DefaultAgencyId, actor, "ProviderValidated", "Provider", key, state);
            return updated;
        }
    }

    public CameraFeedDto UpsertCamera(Guid? id, UpsertCameraFeedRequest request, string actor)
    {
        lock (_gate)
        {
            var key = id ?? Guid.NewGuid();
            var item = new CameraFeedDto(key, request.AgencyId, request.StationId, request.PremiseId, request.Name.Trim(), request.Uri.Trim(), request.Provider.Trim(), "Configured", DateTimeOffset.UtcNow);
            _cameras[key] = item;
            AuditMutation(item.AgencyId, actor, "CameraConfigured", "Camera", key.ToString(), item.Name);
            return item;
        }
    }

    public RadioStreamDto UpsertRadioStream(Guid? id, UpsertRadioStreamRequest request, string actor)
    {
        lock (_gate)
        {
            var key = id ?? Guid.NewGuid();
            var item = new RadioStreamDto(key, request.AgencyId, request.Name.Trim(), request.IcecastUri.Trim(), string.IsNullOrWhiteSpace(request.Codec) ? "Opus" : request.Codec.Trim(), request.AuthorizationScope.Trim(), "Configured");
            _radioStreams[key] = item;
            AuditMutation(item.AgencyId, actor, "RadioStreamConfigured", "RadioStream", key.ToString(), item.Name);
            return item;
        }
    }

    public LeaveRequestDto CreateLeave(CreateLeaveRequest request)
    {
        lock (_gate)
        {
            if (request.EndDate < request.StartDate) throw new ArgumentException("End date cannot precede start date.");
            var now = DateTimeOffset.UtcNow;
            var item = new LeaveRequestDto(Guid.NewGuid(), request.AgencyId, request.EmployeeName.Trim(), request.StartDate, request.EndDate, request.LeaveType.Trim(), request.Reason.Trim(), "Pending", null, now, now);
            _leaveRequests[item.Id] = item;
            AuditMutation(item.AgencyId, item.EmployeeName, "LeaveRequested", "LeaveRequest", item.Id.ToString(), item.LeaveType);
            return item;
        }
    }

    public LeaveRequestDto ReviewLeave(Guid id, ReviewLeaveRequest request)
    {
        lock (_gate)
        {
            if (!_leaveRequests.TryGetValue(id, out var current)) throw new KeyNotFoundException("Leave request not found.");
            var updated = current with { Status = request.Approved ? "Approved" : "Denied", ReviewedBy = request.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _leaveRequests[id] = updated;
            AuditMutation(updated.AgencyId, request.Actor, request.Approved ? "LeaveApproved" : "LeaveDenied", "LeaveRequest", id.ToString(), request.Notes ?? string.Empty);
            return updated;
        }
    }

    public ShiftTradeDto CreateShiftTrade(CreateShiftTradeRequest request)
    {
        lock (_gate)
        {
            var offeredShift = _core.Workforce.FirstOrDefault(x => x.Id == request.OfferedShiftId) ?? throw new KeyNotFoundException("Shift not found.");
            if (offeredShift.AgencyId != request.AgencyId) throw new InvalidOperationException("Shift trade cannot cross agency boundaries.");
            var now = DateTimeOffset.UtcNow;
            var item = new ShiftTradeDto(Guid.NewGuid(), request.AgencyId, request.OfferedShiftId, request.OfferedBy.Trim(), request.RequestedWith.Trim(), "Pending", null, now, now);
            _shiftTrades[item.Id] = item;
            AuditMutation(item.AgencyId, item.OfferedBy, "ShiftTradeRequested", "ShiftTrade", item.Id.ToString(), item.RequestedWith);
            return item;
        }
    }

    public ShiftTradeDto ReviewShiftTrade(Guid id, ReviewShiftTradeRequest request)
    {
        lock (_gate)
        {
            if (!_shiftTrades.TryGetValue(id, out var current)) throw new KeyNotFoundException("Shift trade not found.");
            var updated = current with { Status = request.Approved ? "Approved" : "Denied", ReviewedBy = request.Actor, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _shiftTrades[id] = updated;
            AuditMutation(updated.AgencyId, request.Actor, request.Approved ? "ShiftTradeApproved" : "ShiftTradeDenied", "ShiftTrade", id.ToString(), updated.OfferedBy);
            return updated;
        }
    }

    public StaffingSummaryDto Staffing(Guid agencyId, DateOnly date)
    {
        var evaluation = EvaluateStaffing(agencyId, date);
        return new StaffingSummaryDto(agencyId, date, evaluation.RequiredPositions, evaluation.FilledPositions, evaluation.Vacancies, evaluation.Warnings);
    }

    public DocumentVersionDto AddDocumentVersion(Guid documentId, string content, string actor)
    {
        lock (_gate)
        {
            var document = _core.Documents.FirstOrDefault(x => x.Id == documentId) ?? throw new KeyNotFoundException("Document not found.");
            var next = _documentVersions.TryGetValue(documentId, out var list) ? list.Select(x => x.Version).DefaultIfEmpty(document.Version).Max() + 1 : document.Version + 1;
            var item = new DocumentVersionDto(Guid.NewGuid(), documentId, next, content, "Draft", actor, DateTimeOffset.UtcNow);
            if (!_documentVersions.TryGetValue(documentId, out list)) _documentVersions[documentId] = list = [];
            list.Add(item);
            AuditMutation(document.AgencyId, actor, "DocumentVersionCreated", "Document", documentId.ToString(), $"v{next}");
            return item;
        }
    }

    public IReadOnlyList<DocumentVersionDto> GetDocumentVersions(Guid documentId)
    {
        lock (_gate) return _documentVersions.TryGetValue(documentId, out var list) ? list.OrderByDescending(x => x.Version).ToArray() : [];
    }

    public IReadOnlyList<DocumentAcknowledgementDto> AssignDocumentAcknowledgements(Guid documentId, AssignAcknowledgementRequest request)
    {
        lock (_gate)
        {
            var document = _core.Documents.FirstOrDefault(x => x.Id == documentId) ?? throw new KeyNotFoundException("Document not found.");
            var now = DateTimeOffset.UtcNow;
            var items = request.Recipients.Distinct(StringComparer.OrdinalIgnoreCase).Select(recipient => new DocumentAcknowledgementDto(Guid.NewGuid(), documentId, recipient.Trim(), "Pending", now, null)).ToArray();
            foreach (var item in items) _documentAcks[item.Id] = item;
            AuditMutation(document.AgencyId, request.Actor, "DocumentAcknowledgementsAssigned", "Document", documentId.ToString(), $"Recipients={items.Length}");
            return items;
        }
    }

    public IReadOnlyList<DocumentAcknowledgementDto> GetDocumentAcknowledgements(Guid documentId)
    {
        lock (_gate) return _documentAcks.Values.Where(x => x.DocumentId == documentId).OrderBy(x => x.Recipient).ToArray();
    }

    public DocumentAcknowledgementDto AcknowledgeDocument(Guid ackId, AcknowledgeDocumentRequest request)
    {
        lock (_gate)
        {
            if (!_documentAcks.TryGetValue(ackId, out var current)) throw new KeyNotFoundException("Acknowledgement not found.");
            if (!current.Recipient.Equals(request.Recipient, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Acknowledgement recipient mismatch.");
            var updated = current with { Status = "Acknowledged", AcknowledgedAtUtc = DateTimeOffset.UtcNow };
            _documentAcks[ackId] = updated;
            return updated;
        }
    }

    public FormTemplateDto CreateFormTemplate(CreateFormTemplateRequest request)
    {
        lock (_gate)
        {
            if (request.AgencyId == Guid.Empty) throw new ArgumentException("Agency is required.");
            if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Form template name is required.");
            ValidateJson(request.SchemaJson, "Form template schema");
            var version = _formTemplates.Values.Where(x => x.AgencyId == request.AgencyId && x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
            var item = new FormTemplateDto(Guid.NewGuid(), request.AgencyId, request.Name.Trim(), version, "Draft", request.SchemaJson, request.CreatedBy.Trim(), DateTimeOffset.UtcNow);
            _formTemplates[item.Id] = item;
            AuditMutation(item.AgencyId, item.CreatedBy, "FormTemplateCreated", "FormTemplate", item.Id.ToString(), $"{item.Name} v{item.Version}");
            return item;
        }
    }

    public FormTemplateDto PublishFormTemplate(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_formTemplates.TryGetValue(id, out var current)) throw new KeyNotFoundException("Form template not found.");
            if (current.Status == "Published") return current;
            ValidateJson(current.SchemaJson, "Form template schema");
            foreach (var prior in _formTemplates.Values.Where(x => x.Id != id && x.AgencyId == current.AgencyId && x.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase) && x.Status == "Published").ToArray())
                _formTemplates[prior.Id] = prior with { Status = "Superseded", UpdatedAtUtc = DateTimeOffset.UtcNow };
            var updated = current with { Status = "Published", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _formTemplates[id] = updated;
            AuditMutation(updated.AgencyId, actor, "FormTemplatePublished", "FormTemplate", id.ToString(), $"{updated.Name} v{updated.Version}");
            return updated;
        }
    }

    public FormInstanceDto CreateForm(CreateFormInstanceRequest request)
    {
        lock (_gate)
        {
            if (!_formTemplates.TryGetValue(request.TemplateId, out var template) || template.Status != "Published") throw new InvalidOperationException("Published form template required.");
            if (template.AgencyId != request.AgencyId) throw new InvalidOperationException("Form and template agency do not match.");
            ValidateJson(request.ValuesJson, "Form values");
            if (request.IncidentId is Guid incidentId && _core.GetIncident(incidentId).AgencyId != request.AgencyId) throw new InvalidOperationException("Form and incident agency do not match.");
            var now = DateTimeOffset.UtcNow;
            var item = new FormInstanceDto(Guid.NewGuid(), request.AgencyId, request.TemplateId, request.IncidentId, "Draft", request.ValuesJson, request.CreatedBy.Trim(), null, now, now);
            _forms[item.Id] = item;
            AuditMutation(item.AgencyId, item.CreatedBy, "FormCreated", "Form", item.Id.ToString(), $"{template.Name} v{template.Version}");
            return item;
        }
    }

    public FormInstanceDto SignForm(Guid id, SignFormRequest request)
    {
        SignFormStrong(id, new SignFormStrongRequest(), request.Actor, "legacy-direct");
        lock (_gate) return _forms[id];
    }

    public InspectionDto CreateInspection(CreateInspectionRequest request)
    {
        lock (_gate)
        {
            if (!_premises.ContainsKey(request.PremiseId)) throw new KeyNotFoundException("Premise not found.");
            var item = new InspectionDto(Guid.NewGuid(), request.AgencyId, request.PremiseId, request.Type.Trim(), request.InspectionDate, request.Inspector.Trim(), "Open", request.Findings.Trim(), DateTimeOffset.UtcNow);
            _inspections[item.Id] = item;
            AuditMutation(item.AgencyId, item.Inspector, "InspectionCreated", "Inspection", item.Id.ToString(), item.Type);
            return item;
        }
    }

    public InspectionDto CloseInspection(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_inspections.TryGetValue(id, out var current)) throw new KeyNotFoundException("Inspection not found.");
            var updated = current with { Status = "Completed", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _inspections[id] = updated;
            AuditMutation(updated.AgencyId, actor, "InspectionClosed", "Inspection", id.ToString(), updated.Type);
            return updated;
        }
    }

    public PermitDto CreatePermit(CreatePermitRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_premises.ContainsKey(request.PremiseId)) throw new KeyNotFoundException("Premise not found.");
            var number = $"PER-{DateTime.UtcNow:yy}-{_permitSequence++:00000}";
            var item = new PermitDto(Guid.NewGuid(), request.AgencyId, request.PremiseId, request.PermitType.Trim(), number, "Active", request.IssuedDate, request.ExpiresDate, request.Holder.Trim(), request.Notes.Trim());
            _permits[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "PermitIssued", "Permit", item.Id.ToString(), item.Number);
            return item;
        }
    }

    public MaintenanceRecordDto CreateMaintenance(CreateMaintenanceRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_core.Units.Any(x => x.Id == request.UnitId)) throw new KeyNotFoundException("Unit not found.");
            var item = new MaintenanceRecordDto(Guid.NewGuid(), request.AgencyId, request.UnitId, request.Category.Trim(), request.Summary.Trim(), "Open", request.OpenedDate, null, request.Cost);
            _maintenance[item.Id] = item;
            AuditMutation(item.AgencyId, actor, "MaintenanceOpened", "Maintenance", item.Id.ToString(), item.Summary);
            return item;
        }
    }

    public MaintenanceRecordDto CompleteMaintenance(Guid id, string actor)
    {
        lock (_gate)
        {
            if (!_maintenance.TryGetValue(id, out var current)) throw new KeyNotFoundException("Maintenance record not found.");
            var updated = current with { Status = "Completed", CompletedDate = DateOnly.FromDateTime(DateTime.UtcNow) };
            _maintenance[id] = updated;
            AuditMutation(updated.AgencyId, actor, "MaintenanceCompleted", "Maintenance", id.ToString(), updated.Summary);
            return updated;
        }
    }

    public NerisReportDto UpsertNeris(Guid? id, UpsertNerisReportRequest request, string actor)
    {
        lock (_gate)
        {
            var incident = _core.GetIncident(request.IncidentId);
            if (incident.AgencyId != request.AgencyId) throw new InvalidOperationException("NERIS report and incident agency do not match.");
            var key = id ?? Guid.NewGuid();
            var errors = ValidateJsonEnvelope(request.PayloadJson);
            var item = new NerisReportDto(key, request.AgencyId, request.IncidentId, "Draft", request.PayloadJson, errors.Count == 0 ? "Valid" : "Invalid", errors, null, DateTimeOffset.UtcNow);
            _neris[key] = item;
            AuditMutation(item.AgencyId, actor, id is null ? "NerisCreated" : "NerisUpdated", "NERIS", key.ToString(), item.ValidationState);
            return item;
        }
    }

    public NerisReportDto SubmitNeris(Guid id, SubmitNerisReportRequest request)
    {
        lock (_gate)
        {
            if (!_neris.TryGetValue(id, out var current)) throw new KeyNotFoundException("NERIS report not found.");
            if (current.ValidationErrors.Count > 0) throw new InvalidOperationException("NERIS report has validation errors.");
            var updated = current with { Status = "Submitted", SubmissionId = $"NERIS-{Guid.NewGuid():N}", UpdatedAtUtc = DateTimeOffset.UtcNow };
            _neris[id] = updated;
            AuditMutation(updated.AgencyId, request.Actor, "NerisSubmitted", "NERIS", id.ToString(), updated.SubmissionId!);
            return updated;
        }
    }

    public NerisReportDto CompleteNerisTransport(Guid id, string providerReference, string actor)
    {
        lock (_gate)
        {
            if (!_neris.TryGetValue(id, out var current)) throw new KeyNotFoundException("NERIS report not found.");
            if (current.ValidationErrors.Count > 0) throw new InvalidOperationException("NERIS report has validation errors.");
            if (string.IsNullOrWhiteSpace(providerReference)) throw new ArgumentException("Provider submission reference is required.");
            var updated = current with { Status = "Submitted", SubmissionId = providerReference.Trim(), UpdatedAtUtc = DateTimeOffset.UtcNow };
            _neris[id] = updated;
            AuditMutation(updated.AgencyId, actor, "NerisTransportConfirmed", "NERIS", id.ToString(), updated.SubmissionId);
            return updated;
        }
    }

    public DeviceIdentityDto RegisterDevice(RegisterDeviceRequest request, string actor)
    {
        lock (_gate)
        {
            var key = request.DeviceId.Trim();
            if (_devices.ContainsKey(key)) throw new InvalidOperationException("Device ID already registered.");
            var item = new DeviceIdentityDto(Guid.NewGuid(), request.AgencyId, key, request.Name.Trim(), request.Role.Trim(), request.Platform.Trim(), "Active", NullIfBlank(request.CertificateThumbprint), DateTimeOffset.UtcNow, null);
            _devices[key] = item;
            AuditMutation(item.AgencyId, actor, "DeviceRegistered", "Device", item.DeviceId, item.Name);
            return item;
        }
    }

    public DeviceIdentityDto RevokeDevice(string deviceId, string actor)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var current)) throw new KeyNotFoundException("Device not found.");
            var updated = current with { State = "Revoked" };
            _devices[deviceId] = updated;
            AuditMutation(updated.AgencyId, actor, "DeviceRevoked", "Device", deviceId, updated.Name);
            return updated;
        }
    }

    public UserSecurityDto UpsertUser(string userId, UpdateUserSecurityRequest request, string actor)
    {
        lock (_gate)
        {
            var current = _users.TryGetValue(userId, out var existing) ? existing : new UserSecurityDto(userId, userId, OperationalStore.DefaultAgencyId, [], false, false, false, "Active", DateTimeOffset.UtcNow);
            if (request.TotpEnrolled != current.TotpEnrolled)
                throw new InvalidOperationException("TOTP enrollment state must be changed through the TOTP enrollment/disable workflow.");
            var updated = current with { Roles = request.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), PasskeyEnrolled = request.PasskeyEnrolled, BreakGlass = request.BreakGlass, State = request.State.Trim(), UpdatedAtUtc = DateTimeOffset.UtcNow };
            _users[userId] = updated;
            AuditMutation(updated.AgencyId, actor, "UserSecurityUpdated", "User", userId, string.Join(',', updated.Roles));
            return updated;
        }
    }

    public ConfigVersionDto CreateConfigDraft(CreateConfigDraftRequest request)
    {
        lock (_gate)
        {
            var version = _configs.Values.Where(x => x.AgencyId == request.AgencyId && x.Area.Equals(request.Area, StringComparison.OrdinalIgnoreCase)).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
            var item = new ConfigVersionDto(Guid.NewGuid(), request.AgencyId, request.Area.Trim(), version, "Draft", request.ContentJson, request.CreatedBy.Trim(), DateTimeOffset.UtcNow, null);
            _configs[item.Id] = item;
            AuditMutation(item.AgencyId, item.CreatedBy, "ConfigDraftCreated", "Config", item.Id.ToString(), $"{item.Area} v{item.Version}");
            return item;
        }
    }

    public ConfigVersionDto PublishConfig(Guid id, PublishConfigRequest request)
    {
        lock (_gate)
        {
            if (!_configs.TryGetValue(id, out var current)) throw new KeyNotFoundException("Configuration version not found.");
            foreach (var old in _configs.Values.Where(x => x.AgencyId == current.AgencyId && x.Area.Equals(current.Area, StringComparison.OrdinalIgnoreCase) && x.State == "Published" && x.Id != id).ToArray()) _configs[old.Id] = old with { State = "Superseded" };
            var updated = current with { State = "Published", PublishedAtUtc = DateTimeOffset.UtcNow };
            _configs[id] = updated;
            AuditMutation(updated.AgencyId, request.Actor, "ConfigPublished", "Config", id.ToString(), $"{updated.Area} v{updated.Version}");
            return updated;
        }
    }

    public HaStatusDto HaStatus()
    {
        lock (_gate)
        {
            return new HaStatusDto("Dispatch", "Dispatch Primary", "Dispatch Primary -> Dispatch Local Standby -> HQ DR", _replication.ToArray(), _backups.ToArray(), true, DateTimeOffset.UtcNow);
        }
    }

    public void SetBackupStatus(BackupStatusDto status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            var index = _backups.FindIndex(x => x.Component.Equals(status.Component, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _backups[index] = status;
            else _backups.Add(status);
        }
    }


    public IReadOnlyList<SearchResultDto> ExtendedSearch(string query)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(query)) return [];
            var q = query.Trim();
            var list = new List<SearchResultDto>(_core.Search(q));
            list.AddRange(_premises.Values.Where(x => Has(x.Name, q) || Has(x.Address, q) || Has(x.Hazards, q)).Select(x => new SearchResultDto("Premise", x.Id, x.Name, x.Address, x.UpdatedAtUtc)));
            list.AddRange(_preplans.Values.Where(x => Has(x.Title, q) || Has(x.OperationalContent, q)).Select(x => new SearchResultDto("Preplan", x.Id, x.Title, $"v{x.Version} {x.Status}", x.UpdatedAtUtc)));
            list.AddRange(_alerts.Values.Where(x => Has(x.Summary, q) || Has(x.AffectedEntity, q)).Select(x => new SearchResultDto("Alert", x.Id, x.Summary, $"{x.Severity} {x.State}", x.UpdatedAtUtc)));
            list.AddRange(_bulletins.Values.Where(x => Has(x.Title, q) || Has(x.Body, q)).Select(x => new SearchResultDto("Bulletin", x.Id, x.Title, x.Category, x.StartsAtUtc)));
            list.AddRange(_permits.Values.Where(x => Has(x.Number, q) || Has(x.Holder, q)).Select(x => new SearchResultDto("Permit", x.Id, x.Number, x.Holder, x.IssuedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))));
            return list.OrderByDescending(x => x.UpdatedAtUtc).Take(200).ToArray();
        }
    }

    private void Seed()
    {
        var agency = OperationalStore.DefaultAgencyId;
        var premise = UpsertPremise(null, new UpsertPremiseRequest(agency, "Demo High-Rise", "123 Main Street", "Mixed Use", "Knox box at alpha side", "Sprinklered; standpipe", "Gas/electric main basement utility room", "Roof access limited", "Operational"), "system");
        var preplan = CreatePreplan(new CreatePreplanRequest(agency, premise.Id, "123 Main Street Preplan", "Five-story mixed-use; FDC alpha/bravo corner; standpipe stair A.", "system"), "system");
        PublishPreplan(preplan.Id, "system");
        UpsertHydrant(null, new UpsertHydrantRequest(agency, "H-001", "Main St / Oak St", 40.7357, -74.1724, 8, 62, 48, 1250, "InService"), "system");
        _providers["routing"] = new ProviderConfigDto("routing", "Routing Provider", "GIS", false, "", "None", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["911"] = new ProviderConfigDto("911", "9-1-1 Gateway", "Communications", false, "", "MutualTLS", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["radio"] = new ProviderConfigDto("radio", "Radio Gateway", "Communications", false, "", "MutualTLS", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["recording"] = new ProviderConfigDto("recording", "Recording Correlation", "Communications", false, "", "ApiKey", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["station-alert"] = new ProviderConfigDto("station-alert", "Station Alerting", "Station", false, "", "MutualTLS", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["onvif"] = new ProviderConfigDto("onvif", "ONVIF Camera Discovery", "Video", false, "", "CredentialReference", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["ais"] = new ProviderConfigDto("ais", "AIS Gateway", "Marine", false, "", "MutualTLS", new Dictionary<string, string>(), 1, "NotConfigured");
        _providers["traffic"] = new ProviderConfigDto("traffic", "Traffic / Road Feed", "GIS", false, "", "ApiKey", new Dictionary<string, string>(), 1, "NotConfigured");
        _backups.Add(new BackupStatusDto("PostgreSQL", "NotConfigured", null, null, "Configure production backup/PITR target."));
        _backups.Add(new BackupStatusDto("Documents", "NotConfigured", null, null, "Configure object-storage backup target."));
        _backups.Add(new BackupStatusDto("Configuration", "Development", DateTimeOffset.UtcNow, null, "In-memory direct-build configuration only."));
        _replication.Add(new ReplicationStatusDto("Dispatch", "LocalStandby", "NotConfigured", 0, 0, false, DateTimeOffset.UtcNow));
        _replication.Add(new ReplicationStatusDto("HQ", "GeographicDR", "NotConfigured", 0, 0, false, DateTimeOffset.UtcNow));
        _users["local-development"] = new UserSecurityDto("local-development", "Local Development User", agency, ["Dispatcher", "Supervisor", "Administrator"], false, false, true, "Development", DateTimeOffset.UtcNow);
        UpsertResponsePlan(null, new UpsertResponsePlanRequest(agency, "Default Structure Fire", "Structure Fire", "Emergency", ["Engine", "Engine", "Ladder", "Chief"], [], true, "system"));
        _themes[Guid.Parse("33333333-3333-3333-3333-333333333331")] = new ThemePackageDto(Guid.Parse("33333333-3333-3333-3333-333333333331"), agency, "classic", "Classic", 1, "BuiltIn", "{}", DateTimeOffset.UtcNow);
        _themes[Guid.Parse("33333333-3333-3333-3333-333333333332")] = new ThemePackageDto(Guid.Parse("33333333-3333-3333-3333-333333333332"), agency, "dark", "Dark", 1, "BuiltIn", "{}", DateTimeOffset.UtcNow);
        _themes[Guid.Parse("33333333-3333-3333-3333-333333333333")] = new ThemePackageDto(Guid.Parse("33333333-3333-3333-3333-333333333333"), agency, "light", "Light", 1, "BuiltIn", "{}", DateTimeOffset.UtcNow);
    }

    private void CaptureDispatchActionState(DispatchActionDto state, string cause, string actor)
    {
        _dispatchActionHistory.Add(new DispatchActionHistoryEntryDto(Guid.NewGuid(), state.Id, state.IncidentId, cause, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), state, DateTimeOffset.UtcNow));
    }

    private void CaptureDispatchDeliveryState(DispatchDeliveryDto state, Guid incidentId, string cause, string actor)
    {
        _dispatchDeliveryHistory.Add(new DispatchDeliveryHistoryEntryDto(Guid.NewGuid(), state.Id, incidentId, cause, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), state, state.UpdatedAtUtc));
    }

    private void CaptureStationDeliveryState(StationDeliveryDto state, string cause, string actor)
    {
        _stationDeliveryHistory.Add(new StationDeliveryHistoryEntryDto(Guid.NewGuid(), state.Id, state.IncidentId, cause, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), state, state.UpdatedAtUtc));
    }

    private void CaptureAlertState(AlertDto state, string cause, string actor)
    {
        _alertHistory.Add(new AlertHistoryEntryDto(Guid.NewGuid(), state.Id, state.IncidentId, cause, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(), state, state.UpdatedAtUtc));
    }

    private void AuditMutation(Guid agencyId, string actor, string action, string entityType, string entityId, string summary)
    {
        var record = new AuditRecordDto(Guid.NewGuid(), agencyId, string.IsNullOrWhiteSpace(actor) ? "unknown" : actor, action, entityType, entityId, summary, "DISRoute.Server", DateTimeOffset.UtcNow);
        _audit.Add(record);
        SealAuditRecord(record);
        _mutationQueue.Enqueue(agencyId, entityType, entityId, action, record.Actor, summary, record);
    }

    private void SealAuditRecord(AuditRecordDto record)
    {
        var previous = _auditIntegrity.Count == 0 ? string.Empty : _auditIntegrity[^1].Hash;
        var sequence = _auditIntegrity.Count == 0 ? 1 : _auditIntegrity[^1].Sequence + 1;
        var canonical = string.Join("|", sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), previous, record.Id.ToString("D"), record.AgencyId.ToString("D"), record.Actor, record.Action, record.EntityType, record.EntityId, record.Summary, record.Source, record.OccurredAtUtc.ToUniversalTime().ToString("O"));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
        _auditIntegrity.Add(new AuditIntegrityRecordDto(record.Id, sequence, previous, hash, DateTimeOffset.UtcNow));
    }

    public AuditIntegrityValidationDto VerifyAuditIntegrity()
    {
        lock (_gate)
        {
            var byId = _audit.ToDictionary(x => x.Id);
            var previous = string.Empty;
            long expectedSequence = 1;
            foreach (var seal in _auditIntegrity.OrderBy(x => x.Sequence))
            {
                if (seal.Sequence != expectedSequence) return new(false, (int)(expectedSequence - 1), $"Audit sequence gap at {expectedSequence}.");
                if (!string.Equals(seal.PreviousHash, previous, StringComparison.Ordinal)) return new(false, (int)(expectedSequence - 1), $"Previous-hash mismatch at sequence {seal.Sequence}.");
                if (!byId.TryGetValue(seal.AuditRecordId, out var record)) return new(false, (int)(expectedSequence - 1), $"Audit record {seal.AuditRecordId:D} is missing.");
                var canonical = string.Join("|", seal.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), seal.PreviousHash, record.Id.ToString("D"), record.AgencyId.ToString("D"), record.Actor, record.Action, record.EntityType, record.EntityId, record.Summary, record.Source, record.OccurredAtUtc.ToUniversalTime().ToString("O"));
                var calculated = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Convert.FromHexString(calculated), Convert.FromHexString(seal.Hash)))
                    return new(false, (int)(expectedSequence - 1), $"Audit hash mismatch at sequence {seal.Sequence}.");
                previous = seal.Hash;
                expectedSequence++;
            }
            return new(true, _auditIntegrity.Count, null);
        }
    }

    private static string DestinationFor(string provider, IncidentDto incident) => provider.ToLowerInvariant() switch
    {
        "stationdisplay" or "stationalert" or "runsheet" => incident.StationId.ToString("D"),
        _ => incident.Id.ToString("D")
    };

    private string RenderRunSheet(IncidentDto incident, IReadOnlyList<Guid> unitIds)
    {
        var calls = unitIds.Select(id => _core.Units.FirstOrDefault(x => x.Id == id)?.CallSign ?? id.ToString("D")).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"DISRoute Run Sheet — {incident.Number}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Nature: {incident.Nature} ({incident.Priority})");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Location: {incident.Address}, {incident.Municipality}");
        if (!string.IsNullOrWhiteSpace(incident.CrossStreets)) sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Cross: {incident.CrossStreets}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Units: {string.Join(", ", calls)}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Caller: {incident.CallerName} {incident.CallerPhone}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Notes: {incident.Notes}");
        return sb.ToString();
    }

    private static IReadOnlyList<string> ValidateJsonEnvelope(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return ["Payload is required."];
        var trimmed = json.Trim();
        if (!(trimmed.StartsWith('{') && trimmed.EndsWith('}'))) return ["Payload must be a JSON object."];
        return [];
    }

    private static string NormalizeClassification(string value) => value.Trim().ToLowerInvariant() switch
    {
        "public" => "Public",
        "internal" => "Internal",
        "sensitive" => "Sensitive",
        "restricted" => "Restricted",
        _ => "Operational"
    };

    private static string NormalizeSeverity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "critical" or "emergency" => "Critical",
        "high" => "High",
        "low" => "Low",
        _ => "Normal"
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void RequireText(string value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required."); }
    private static bool Has(string? source, string query) => source?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    private static string NormalizeLocationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                builder.Append(character);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString();
    }

    private static int AddressMatchScore(string incidentAddress, string premiseAddress)
    {
        if (incidentAddress.Length == 0 || premiseAddress.Length == 0) return 0;
        if (string.Equals(incidentAddress, premiseAddress, StringComparison.Ordinal)) return 100;
        if (incidentAddress.Contains(premiseAddress, StringComparison.Ordinal) || premiseAddress.Contains(incidentAddress, StringComparison.Ordinal)) return 70;
        return 0;
    }

    private static bool IsSceneLocationKind(string kind) => kind.Trim().ToLowerInvariant() is "incidentlocation" or "incident location" or "scene" or "location";

    private static bool TryReadGeoJsonPoint(string geoJson, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        try
        {
            using var document = JsonDocument.Parse(geoJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "Point", StringComparison.OrdinalIgnoreCase)) return false;
            if (!root.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() < 2) return false;
            longitude = coordinates[0].GetDouble();
            latitude = coordinates[1].GetDouble();
            return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6_371_000;
        static double Radians(double d) => d * Math.PI / 180d;
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return radius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private bool TryIdempotent<T>(string? key, object request, out T value, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
        => _idempotency.TryGet(key, $"FeatureStore.{operation}", request, out value);

    private void Remember<T>(string? key, object request, T value, [System.Runtime.CompilerServices.CallerMemberName] string operation = "") where T : notnull
        => _idempotency.Remember(key, $"FeatureStore.{operation}", request, value);
}
