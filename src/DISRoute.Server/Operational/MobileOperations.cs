using System.Text.Json;
using DISRoute.Application.Security;
using DISRoute.Contracts.Operational;
using DISRoute.Security;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    public MobileSyncResponseDto MobileSync(MobileSyncRequest request, string actor, OfflineAuthorizationLeaseValidationDto? lease = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceId)) throw new ArgumentException("Mobile device ID is required.");
            var actorAgency = ResolveActorAgency(actor);
            if (request.AgencyId != actorAgency) throw new UnauthorizedAccessException("Mobile sync is limited to the signed-in user's agency.");

            var device = _devices.Values.FirstOrDefault(x => x.DeviceId.Equals(request.DeviceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new UnauthorizedAccessException("This mobile device is not registered in DISRoute.");
            if (device.AgencyId != actorAgency || !device.State.Equals("Active", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("This mobile device is not active for the signed-in user's agency.");
            if (!device.Role.Equals("Mobile", StringComparison.OrdinalIgnoreCase) && !device.Role.Equals("MDT", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The registered device is not authorized for the Mobile/MDT client role.");

            var now = DateTimeOffset.UtcNow;
            var authorizedUnits = GetMobileAuthorizedUnits(actor, actorAgency, now);
            var selectedUnit = ResolveSelectedMobileUnit(request.UnitId, authorizedUnits);
            var results = new List<MobileOperationResultDto>();
            foreach (var operation in request.QueuedOperations.OrderBy(x => x.OccurredAtUtc))
                results.Add(ApplyMobileOperation(operation, request, actor, actorAgency, authorizedUnits, lease));

            // Re-read after reconciliation because a queued status may have changed unit/incident state.
            authorizedUnits = GetMobileAuthorizedUnits(actor, actorAgency, now);
            selectedUnit = ResolveSelectedMobileUnit(request.UnitId ?? selectedUnit?.Id, authorizedUnits);
            var package = BuildMobileOperationalPackage(actor, request.DeviceId, actorAgency, selectedUnit, now);
            var incidents = package.CurrentIncident is null ? Array.Empty<IncidentDto>() : [package.CurrentIncident];
            var units = authorizedUnits.ToArray();
            var preplans = package.SiteIntelligence?.PublishedPreplans?.ToArray() ?? [];
            var crgs = package.SiteIntelligence?.CrgAssets?.ToArray() ?? [];
            var messages = package.CurrentIncident is null
                ? Array.Empty<MessageDto>()
                : _core.Messages.Where(x => x.IncidentId == package.CurrentIncident.Id && (request.SinceUtc is null || x.SentAtUtc >= request.SinceUtc)).OrderBy(x => x.SentAtUtc).ToArray();

            Heartbeat(new HeartbeatRequest(actorAgency, request.DeviceId, "Mobile", device.Platform, "sync", actor, 0));
            var connectivity = new MobileConnectivityDto("Online", now, now, request.QueuedOperations.Count - results.Count(x => x.State is "Accepted" or "DuplicateIgnored"), 0, null);
            return new MobileSyncResponseDto(now, incidents, units, preplans, crgs, messages, results, package, connectivity);
        }
    }

    private MobileOperationResultDto ApplyMobileOperation(QueuedMobileOperationDto operation, MobileSyncRequest request, string actor, Guid actorAgency, UnitDto[] authorizedUnits, OfflineAuthorizationLeaseValidationDto? lease)
    {
        var reconciliationKey = $"mobile-reconcile:{operation.OperationId:D}";
        var commandKey = $"mobile-command:{operation.OperationId:D}";
        if (_idempotency.TryGet<bool>(reconciliationKey, "FeatureStore.MobileSyncOperation", new { Actor = actor, Operation = operation }, out _))
            return new MobileOperationResultDto(operation.OperationId, "DuplicateIgnored", null);

        try
        {
            switch (operation.Kind)
            {
                case "Narrative":
                {
                    var payload = DeserializeMobilePayload<MobileNarrativePayload>(operation);
                    var incident = RequireMobileIncident(operation.IncidentId, actorAgency, authorizedUnits, actor);
                    _core.AddNarrative(incident.Id, new AddNarrativeRequest(payload.Text, commandKey), actor);
                    break;
                }
                case "Message":
                {
                    var payload = DeserializeMobilePayload<MobileMessagePayload>(operation);
                    var incident = RequireMobileIncident(operation.IncidentId, actorAgency, authorizedUnits, actor);
                    _core.SendMessage(new SendMessageRequest(actorAgency, incident.Id, string.IsNullOrWhiteSpace(payload.Channel) ? "Mobile" : payload.Channel.Trim(), actor, payload.Text.Trim(), commandKey));
                    break;
                }
                case "ChatMessage":
                {
                    var payload = DeserializeMobilePayload<MobileChatPayload>(operation);
                    var incident = operation.IncidentId is Guid iid ? RequireMobileIncident(iid, actorAgency, authorizedUnits, actor) : null;
                    SendChatMessage(payload.ChannelId, new SendChatMessageRequest(payload.Text, "Normal", "Operational", incident?.Id, null, null, commandKey), actor);
                    break;
                }
                case "UnitStatus":
                {
                    var payload = DeserializeMobilePayload<MobileUnitStatusPayload>(operation);
                    ValidateOfflineUnitStatusLease(request, actor, actorAgency, lease);
                    var current = Array.Find(authorizedUnits, x => x.Id == payload.UnitId)
                        ?? throw new UnauthorizedAccessException("The signed-in user is not currently assigned to this unit.");
                    if (!current.Status.Equals(payload.FromStatus, StringComparison.OrdinalIgnoreCase) || current.UpdatedAtUtc != payload.ObservedUnitUpdatedAtUtc)
                        return new MobileOperationResultDto(operation.OperationId, "Conflict", $"Authoritative unit state changed to {current.Status} at {current.UpdatedAtUtc:O}; review before retrying.");
                    _core.SetUnitStatus(current.Id, new SetUnitStatusRequest(payload.ToStatus, commandKey), actor);
                    break;
                }
                default:
                    return new MobileOperationResultDto(operation.OperationId, "RequiresOnline", "This operation type is not approved for offline reconciliation.");
            }

            _idempotency.Remember(reconciliationKey, "FeatureStore.MobileSyncOperation", new { Actor = actor, Operation = operation }, true);
            return new MobileOperationResultDto(operation.OperationId, "Accepted", null);
        }
        catch (UnauthorizedAccessException ex) { return new MobileOperationResultDto(operation.OperationId, "Rejected", ex.Message); }
        catch (ArgumentException ex) { return new MobileOperationResultDto(operation.OperationId, "Rejected", ex.Message); }
        catch (JsonException ex) { return new MobileOperationResultDto(operation.OperationId, "Rejected", "Invalid offline payload: " + ex.Message); }
        catch (InvalidOperationException ex) { return new MobileOperationResultDto(operation.OperationId, "Conflict", ex.Message); }
    }

    private MobileOperationalPackageDto BuildMobileOperationalPackage(string actor, string deviceId, Guid actorAgency, UnitDto? selectedUnit, DateTimeOffset now)
    {
        IncidentDto? incident = null;
        IncidentSiteIntelligenceDto? intelligence = null;
        IReadOnlyList<WorkforceAssignmentDto> crew = [];
        IReadOnlyList<MaydayEventDto> maydays = [];
        IReadOnlyList<OperationalMessageDto> operationalMessages = [];
        var warnings = new List<string>();

        if (selectedUnit is not null)
        {
            crew = GetEffectiveWorkforceAssignments(now)
                .Where(x => x.AgencyId == actorAgency && x.UnitId == selectedUnit.Id)
                .OrderBy(x => x.Position, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selectedUnit.IncidentId is Guid incidentId)
            {
                incident = _core.GetIncident(incidentId);
                intelligence = GetSiteIntelligence(incidentId);
                maydays = _maydays.Values.Where(x => x.IncidentId == incidentId && x.State != "Resolved").OrderByDescending(x => x.DeclaredAtUtc).ToArray();
                operationalMessages = _operationalMessages.Values.Where(x => x.IncidentId == incidentId).OrderByDescending(x => x.CreatedAtUtc).Take(100).OrderBy(x => x.CreatedAtUtc).ToArray();
            }
            else warnings.Add($"{selectedUnit.CallSign} is not currently assigned to an incident.");
        }
        else warnings.Add("No current mobile/MDT unit assignment was resolved for this user.");

        var channels = GetChatChannels(actorAgency, actor, false);
        var accessibleChannelIds = channels.Select(x => x.Channel.Id).ToHashSet();
        var chatMessages = _chatMessages.Values
            .Where(x => accessibleChannelIds.Contains(x.ChannelId))
            .Where(x => incident is null || x.LinkedIncidentId == incident.Id || _chatChannels[x.ChannelId].IncidentId == incident.Id || _chatChannels[x.ChannelId].Type == "Direct")
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).OrderBy(x => x.CreatedAtUtc).ToArray();
        var user = _users[actor];
        var radios = _radioStreams.Values
            .Where(x => x.AgencyId == actorAgency && x.State.Equals("Configured", StringComparison.OrdinalIgnoreCase))
            .Where(x => IsMobileRadioAuthorized(x.AuthorizationScope, user.Roles))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var documents = BuildMobileDocuments(actor, actorAgency, incident?.Id);
        return new MobileOperationalPackageDto(actorAgency, actor, deviceId, now, selectedUnit?.Id, selectedUnit, incident, crew, intelligence, maydays, operationalMessages, channels, chatMessages, radios, documents, warnings);
    }

    private UnitDto[] GetMobileAuthorizedUnits(string actor, Guid agencyId, DateTimeOffset now)
    {
        if (ActorHasRole(actor, "Administrator") || ActorHasRole(actor, "Supervisor") || ActorHasRole(actor, "Dispatcher"))
            return _core.Units.Where(x => x.AgencyId == agencyId).OrderBy(x => x.CallSign, StringComparer.OrdinalIgnoreCase).ToArray();

        var assignmentUnitIds = GetEffectiveWorkforceAssignments(now)
            .Where(x => x.AgencyId == agencyId && x.UnitId is not null)
            .Where(x => string.Equals(x.EmployeeId, actor, StringComparison.OrdinalIgnoreCase) || string.Equals(x.EmployeeName, actor, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.UnitId!.Value).Distinct().ToHashSet();
        return _core.Units.Where(x => assignmentUnitIds.Contains(x.Id)).OrderBy(x => x.CallSign, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private WorkforceAssignmentDto[] GetEffectiveWorkforceAssignments(DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(now.UtcDateTime);
        return _workforceAssignments.Values
            .Where(x => x.Date == date && IsFilledWorkforceState(x.State) && x.EffectiveAtUtc <= now && (x.EndsAtUtc is null || x.EndsAtUtc > now))
            .ToArray();
    }

    private static bool IsFilledWorkforceState(string state) => state is "Assigned" or "Acting" or "Detail" or "Overtime" or "Training";

    private static UnitDto? ResolveSelectedMobileUnit(Guid? requestedUnitId, UnitDto[] authorizedUnits)
    {
        if (requestedUnitId is Guid requested)
            return Array.Find(authorizedUnits, x => x.Id == requested) ?? throw new UnauthorizedAccessException("The requested unit is outside this mobile user's current unit scope.");
        var assigned = Array.Find(authorizedUnits, x => x.IncidentId is not null);
        return assigned ?? (authorizedUnits.Length == 0 ? null : authorizedUnits[0]);
    }

    private IncidentDto RequireMobileIncident(Guid? incidentId, Guid actorAgency, UnitDto[] authorizedUnits, string actor)
    {
        if (incidentId is not Guid id) throw new ArgumentException("Offline incident operation requires an incident ID.");
        var incident = _core.GetIncident(id);
        if (incident.AgencyId != actorAgency) throw new UnauthorizedAccessException("Incident belongs to another agency.");
        if (ActorHasRole(actor, "Administrator") || ActorHasRole(actor, "Supervisor") || ActorHasRole(actor, "Dispatcher")) return incident;
        if (!authorizedUnits.Any(x => x.IncidentId == id)) throw new UnauthorizedAccessException("The incident is not assigned to the signed-in user's current unit.");
        return incident;
    }

    private static T DeserializeMobilePayload<T>(QueuedMobileOperationDto operation) where T : class
        => JsonSerializer.Deserialize<T>(operation.PayloadJson, JsonSerializerOptions.Web) ?? throw new JsonException("Offline payload was empty.");

    private static bool IsMobileRadioAuthorized(string scope, IReadOnlyList<string> roles)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("All", StringComparison.OrdinalIgnoreCase) || scope.Equals("Mobile", StringComparison.OrdinalIgnoreCase)) return true;
        var tokens = scope.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(token => roles.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private MobileDocumentDto[] BuildMobileDocuments(string actor, Guid agencyId, Guid? incidentId)
    {
        var linkedDocumentIds = incidentId is Guid iid
            ? _dmsDocumentLinks.Values.Where(x => x.AgencyId == agencyId && x.EntityType.Equals("Incident", StringComparison.OrdinalIgnoreCase) && x.EntityKey.Equals(iid.ToString("D"), StringComparison.OrdinalIgnoreCase)).Select(x => x.DocumentId).ToHashSet()
            : new HashSet<Guid>();
        var receiptDocumentIds = _dmsReadReceipts.Values.Where(x => x.Recipient.Equals(actor, StringComparison.OrdinalIgnoreCase)).Select(x => x.DocumentId).ToHashSet();
        linkedDocumentIds.UnionWith(receiptDocumentIds);

        var list = new List<MobileDocumentDto>();
        foreach (var documentId in linkedDocumentIds)
        {
            var document = _core.Documents.FirstOrDefault(x => x.Id == documentId);
            if (document is null || document.AgencyId != agencyId) continue;
            var revision = _dmsRevisions.Values.Where(x => x.DocumentId == documentId && x.State == "Published").OrderByDescending(x => x.Version).FirstOrDefault();
            if (revision is null) continue;
            var receipts = _dmsReadReceipts.Values.Where(x => x.DocumentId == documentId && x.DocumentVersion == revision.Version && x.Recipient.Equals(actor, StringComparison.OrdinalIgnoreCase)).ToArray();
            var assets = _dmsAssetReferences.Values.Where(x => x.DocumentId == documentId && x.DocumentVersion == revision.Version).OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToArray();
            list.Add(new MobileDocumentDto(documentId, revision.Version, document.Title, document.Classification, revision.ContentSha256, revision.Content, assets, receipts.Length > 0, receipts.Any(x => x.State == "Acknowledged")));
        }
        return list.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidateOfflineUnitStatusLease(MobileSyncRequest request, string actor, Guid agencyId, OfflineAuthorizationLeaseValidationDto? lease)
    {
        if (string.IsNullOrWhiteSpace(request.OfflineAuthorizationLease)) throw new UnauthorizedAccessException("Offline unit-status reconciliation requires an authorization lease.");
        if (lease is null || !lease.Valid) throw new UnauthorizedAccessException(lease?.Error ?? "Offline authorization lease could not be validated.");
        if (!string.Equals(lease.UserId, actor, StringComparison.OrdinalIgnoreCase) || lease.AgencyId != agencyId)
            throw new UnauthorizedAccessException("Offline authorization lease user/agency scope mismatch.");
        if (!lease.Permissions.Contains(Permissions.UnitChangeStatus, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Offline authorization lease does not permit unit status changes.");
    }
}
