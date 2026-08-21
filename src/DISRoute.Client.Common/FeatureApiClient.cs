using System.Net.Http.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Client.Common;

public sealed class FeatureApiClient(HttpClient http)
{
    public Task<CollaborativeNarrativeDto> GetCollaborativeNarrativeAsync(Guid incidentId, CancellationToken ct = default) => GetAsync<CollaborativeNarrativeDto>($"api/incidents/{incidentId:D}/collaborative-narrative", ct);
    public Task<IReadOnlyList<CollaborativeNarrativeOperationDto>> GetCollaborativeNarrativeHistoryAsync(Guid incidentId, long afterVersion = -1, CancellationToken ct = default) => GetAsync<IReadOnlyList<CollaborativeNarrativeOperationDto>>($"api/incidents/{incidentId:D}/collaborative-narrative/history?afterVersion={afterVersion}", ct);
    public Task<CollaborativeNarrativeDto> ReplaceCollaborativeNarrativeAsync(Guid incidentId, ReplaceCollaborativeNarrativeRequest request, CancellationToken ct = default) => PutAsync<CollaborativeNarrativeDto>($"api/incidents/{incidentId:D}/collaborative-narrative", request, ct);
    public Task<CollaborativeNarrativeDto> PatchCollaborativeNarrativeAsync(Guid incidentId, CollaborativeNarrativePatchRequest request, CancellationToken ct = default) => PostAsync<CollaborativeNarrativeDto>($"api/incidents/{incidentId:D}/collaborative-narrative/patch", request, ct);
    public Task<CallRecordDto[]> GetCallsAsync(CancellationToken ct = default) => GetAsync<CallRecordDto[]>("api/calls", ct);
    public Task<CallRecordDto> CreateCallAsync(CreateCallRecordRequest request, CancellationToken ct = default) => PostAsync<CallRecordDto>("api/calls", request, ct);
    public Task<CallRecordDto> LinkCallAsync(Guid callId, LinkCallRequest request, CancellationToken ct = default) => PostAsync<CallRecordDto>($"api/calls/{callId:D}/link", request, ct);
    public Task<DuplicateCandidateDto[]> CheckDuplicatesAsync(DuplicateCheckRequest request, CancellationToken ct = default) => PostAsync<DuplicateCandidateDto[]>("api/cad/duplicate-check", request, ct);
    public Task<ResponseRecommendationDto> GetResponseRecommendationAsync(ResponseRecommendationRequest request, CancellationToken ct = default) => PostAsync<ResponseRecommendationDto>("api/cad/response-recommendation", request, ct);
    public Task<IncidentRelationshipDto[]> GetIncidentRelationshipsAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<IncidentRelationshipDto[]>(incidentId is Guid id ? $"api/incidents/relationships?incidentId={id:D}" : "api/incidents/relationships", ct);
    public Task<IncidentRelationshipDto> CreateIncidentRelationshipAsync(Guid sourceIncidentId, CreateIncidentRelationshipRequest request, CancellationToken ct = default) => PostAsync<IncidentRelationshipDto>($"api/incidents/{sourceIncidentId:D}/relationships", request, ct);
    public Task<IncidentDto> MergeIncidentsAsync(Guid sourceIncidentId, MergeIncidentsRequest request, CancellationToken ct = default) => PostAsync<IncidentDto>($"api/incidents/{sourceIncidentId:D}/merge", request, ct);
    public Task<IncidentDto> SplitIncidentAsync(Guid sourceIncidentId, SplitIncidentRequest request, CancellationToken ct = default) => PostAsync<IncidentDto>($"api/incidents/{sourceIncidentId:D}/split", request, ct);
    public Task<IncidentCorrectionDto[]> GetIncidentCorrectionsAsync(Guid incidentId, CancellationToken ct = default) => GetAsync<IncidentCorrectionDto[]>($"api/incidents/{incidentId:D}/corrections", ct);
    public Task<IncidentCorrectionDto> CorrectIncidentAsync(Guid incidentId, CorrectIncidentFieldRequest request, CancellationToken ct = default) => PostAsync<IncidentCorrectionDto>($"api/incidents/{incidentId:D}/corrections", request, ct);
    public Task<DispatchActionDto[]> GetDispatchActionsAsync(CancellationToken ct = default) => GetAsync<DispatchActionDto[]>("api/dispatch/actions", ct);
    public Task<DispatchCommitResponse> CommitDispatchAsync(Guid incidentId, CommitDispatchRequest request, CancellationToken ct = default) => PostAsync<DispatchCommitResponse>($"api/incidents/{incidentId:D}/dispatch", request, ct);
    public Task<AdditionalAlarmDto[]> GetAdditionalAlarmsAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<AdditionalAlarmDto[]>(incidentId is Guid id ? $"api/dispatch/additional-alarms?incidentId={id:D}" : "api/dispatch/additional-alarms", ct);
    public Task<AdditionalAlarmDto> CommitAdditionalAlarmAsync(Guid incidentId, CommitAdditionalAlarmRequest request, CancellationToken ct = default) => PostAsync<AdditionalAlarmDto>($"api/incidents/{incidentId:D}/additional-alarm", request, ct);
    public Task<DispatchActionDto> CancelDispatchAsync(Guid dispatchActionId, CancelDispatchRequest request, CancellationToken ct = default) => PostAsync<DispatchActionDto>($"api/dispatch/actions/{dispatchActionId:D}/cancel", request, ct);
    public Task<DispatchDeliveryDto[]> GetDispatchDeliveriesAsync(CancellationToken ct = default) => GetAsync<DispatchDeliveryDto[]>("api/dispatch/deliveries", ct);
    public Task<FireWatchDto[]> GetFireWatchesAsync(CancellationToken ct = default) => GetAsync<FireWatchDto[]>("api/fire-watch", ct);
    public Task<FireWatchDto> CreateFireWatchAsync(CreateFireWatchRequest request, CancellationToken ct = default) => PostAsync<FireWatchDto>("api/fire-watch", request, ct);
    public Task<FireWatchCheckDto[]> GetFireWatchChecksAsync(Guid fireWatchId, CancellationToken ct = default) => GetAsync<FireWatchCheckDto[]>($"api/fire-watch/{fireWatchId:D}/checks", ct);
    public Task<FireWatchCheckDto> AddFireWatchCheckAsync(Guid fireWatchId, NotesActionRequest request, CancellationToken ct = default) => PostAsync<FireWatchCheckDto>($"api/fire-watch/{fireWatchId:D}/checks", request, ct);
    public Task<FireWatchDto> EndFireWatchAsync(Guid fireWatchId, NotesActionRequest request, CancellationToken ct = default) => PostAsync<FireWatchDto>($"api/fire-watch/{fireWatchId:D}/end", request, ct);
    public Task<RelocationDto[]> GetRelocationsAsync(CancellationToken ct = default) => GetAsync<RelocationDto[]>("api/relocations", ct);
    public Task<RelocationDto> CreateRelocationAsync(CreateRelocationRequest request, CancellationToken ct = default) => PostAsync<RelocationDto>("api/relocations", request, ct);
    public Task<RelocationDto> EndRelocationAsync(Guid relocationId, NotesActionRequest request, CancellationToken ct = default) => PostAsync<RelocationDto>($"api/relocations/{relocationId:D}/end", request, ct);

    public Task<OperationalGroupDto[]> GetOperationalGroupsAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<OperationalGroupDto[]>(incidentId is Guid id ? $"api/incident-operations/groups?incidentId={id:D}" : "api/incident-operations/groups", ct);
    public Task<OperationalGroupDto> CreateOperationalGroupAsync(CreateOperationalGroupRequest request, CancellationToken ct = default) => PostAsync<OperationalGroupDto>("api/incident-operations/groups", request, ct);
    public Task<OperationalGroupDto> SetOperationalGroupMembersAsync(Guid groupId, UpdateOperationalGroupMembersRequest request, CancellationToken ct = default) => PostAsync<OperationalGroupDto>($"api/incident-operations/groups/{groupId:D}/members", request, ct);
    public Task<CommandAssignmentDto[]> GetCommandAssignmentsAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<CommandAssignmentDto[]>(incidentId is Guid id ? $"api/incident-operations/command?incidentId={id:D}" : "api/incident-operations/command", ct);
    public Task<CommandAssignmentDto> SetCommandAsync(SetCommandRequest request, CancellationToken ct = default) => PostAsync<CommandAssignmentDto>("api/incident-operations/command", request, ct);
    public Task<MutualAidRequestDto[]> GetMutualAidAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<MutualAidRequestDto[]>(incidentId is Guid id ? $"api/mutual-aid?incidentId={id:D}" : "api/mutual-aid", ct);
    public Task<MutualAidRequestDto> CreateMutualAidAsync(CreateMutualAidRequest request, CancellationToken ct = default) => PostAsync<MutualAidRequestDto>("api/mutual-aid", request, ct);
    public Task<MutualAidRequestDto> UpdateMutualAidAsync(Guid mutualAidId, string state, CancellationToken ct = default) => PostAsync<MutualAidRequestDto>($"api/mutual-aid/{mutualAidId:D}/{Uri.EscapeDataString(state)}", new { }, ct);
    public Task<OperationalMessageDto[]> GetOperationalMessagesAsync(Guid agencyId, Guid? incidentId = null, string? recipient = null, string? channel = null, CancellationToken ct = default)
    {
        var query = $"agencyId={agencyId:D}";
        if (incidentId is Guid id) query += $"&incidentId={id:D}";
        if (!string.IsNullOrWhiteSpace(recipient)) query += "&recipient=" + Uri.EscapeDataString(recipient);
        if (!string.IsNullOrWhiteSpace(channel)) query += "&channel=" + Uri.EscapeDataString(channel);
        return GetAsync<OperationalMessageDto[]>("api/messaging/operational?" + query, ct);
    }
    public Task<OperationalMessageDto> SendOperationalMessageAsync(CreateOperationalMessageRequest request, CancellationToken ct = default) => PostAsync<OperationalMessageDto>("api/messaging/operational", request, ct);
    public Task<IncidentActivityDto> PromoteOperationalMessageToNarrativeAsync(Guid messageId, PromoteMessageToNarrativeRequest request, CancellationToken ct = default) => PostAsync<IncidentActivityDto>($"api/messaging/operational/{messageId:D}/promote-to-narrative", request, ct);
    public Task<ChatChannelSummaryDto[]> GetChatChannelsAsync(Guid agencyId, bool includeArchived = false, CancellationToken ct = default) => GetAsync<ChatChannelSummaryDto[]>($"api/chat/channels?agencyId={agencyId:D}&includeArchived={includeArchived.ToString().ToLowerInvariant()}", ct);
    public Task<ChatChannelDto> CreateChatChannelAsync(CreateChatChannelRequest request, CancellationToken ct = default) => PostAsync<ChatChannelDto>("api/chat/channels", request, ct);
    public Task<ChatChannelDto> ArchiveChatChannelAsync(Guid channelId, bool archived, CancellationToken ct = default) => PostAsync<ChatChannelDto>($"api/chat/channels/{channelId:D}/archive", new ArchiveChatChannelRequest(archived), ct);
    public Task<ChatMessageDto[]> GetChatMessagesAsync(Guid channelId, int limit = 100, CancellationToken ct = default) => GetAsync<ChatMessageDto[]>($"api/chat/channels/{channelId:D}/messages?limit={Math.Clamp(limit,1,200)}", ct);
    public Task<ChatMessageDto> SendChatMessageAsync(Guid channelId, SendChatMessageRequest request, CancellationToken ct = default) => PostAsync<ChatMessageDto>($"api/chat/channels/{channelId:D}/messages", request, ct);
    public Task<ChatReadStateDto> MarkChatReadAsync(Guid channelId, Guid? throughMessageId, CancellationToken ct = default) => PostAsync<ChatReadStateDto>($"api/chat/channels/{channelId:D}/read", new MarkChatReadRequest(throughMessageId), ct);
    public Task<ChatSearchResultDto[]> SearchChatAsync(Guid agencyId, string query, int limit = 100, CancellationToken ct = default) => GetAsync<ChatSearchResultDto[]>($"api/chat/search?agencyId={agencyId:D}&q={Uri.EscapeDataString(query)}&limit={Math.Clamp(limit,1,200)}", ct);
    public Task<PresenceDto[]> GetPresenceAsync(CancellationToken ct = default) => GetAsync<PresenceDto[]>("api/presence", ct);

    public Task<PremiseDto[]> GetPremisesAsync(CancellationToken ct = default) => GetAsync<PremiseDto[]>("api/premises", ct);
    public Task<PreplanDto[]> GetPreplansAsync(Guid? premiseId = null, CancellationToken ct = default) => GetAsync<PreplanDto[]>(premiseId is Guid id ? $"api/preplans?premiseId={id:D}" : "api/preplans", ct);
    public Task<CrgAssetDto[]> GetCrgAssetsAsync(Guid? premiseId = null, CancellationToken ct = default) => GetAsync<CrgAssetDto[]>(premiseId is Guid id ? $"api/crg-assets?premiseId={id:D}" : "api/crg-assets", ct);
    public Task<HydrantDto[]> GetHydrantsAsync(CancellationToken ct = default) => GetAsync<HydrantDto[]>("api/gis/hydrants", ct);
    public Task<MapObjectDto[]> GetMapObjectsAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<MapObjectDto[]>(incidentId is Guid id ? $"api/gis/map-objects?incidentId={id:D}" : "api/gis/map-objects", ct);
    public Task<MapObjectDto> CreateMapObjectAsync(CreateMapObjectRequest request, CancellationToken ct = default) => PostAsync<MapObjectDto>("api/gis/map-objects", request, ct);
    public Task<RoadRestrictionDto[]> GetRoadRestrictionsAsync(CancellationToken ct = default) => GetAsync<RoadRestrictionDto[]>("api/gis/road-restrictions", ct);
    public Task<RoadRestrictionDto> CreateRoadRestrictionAsync(CreateRoadRestrictionRequest request, CancellationToken ct = default) => PostAsync<RoadRestrictionDto>("api/gis/road-restrictions", request, ct);
    public Task<RoadRestrictionDto> EndRoadRestrictionAsync(Guid restrictionId, NotesActionRequest request, CancellationToken ct = default) => PostAsync<RoadRestrictionDto>($"api/gis/road-restrictions/{restrictionId:D}/end", request, ct);
    public Task<IncidentSiteIntelligenceDto> GetSiteIntelligenceAsync(Guid incidentId, CancellationToken ct = default) => GetAsync<IncidentSiteIntelligenceDto>($"api/incidents/{incidentId:D}/site-intelligence", ct);
    public Task<RouteResultDto> CalculateRouteAsync(RouteRequest request, CancellationToken ct = default) => PostAsync<RouteResultDto>("api/gis/route", request, ct);
    public Task<VesselObservationDto[]> GetVesselsAsync(CancellationToken ct = default) => GetAsync<VesselObservationDto[]>("api/marine/vessels", ct);

    public Task<MaydayEventDto[]> GetMaydaysAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<MaydayEventDto[]>(incidentId is Guid id ? $"api/mayday?incidentId={id:D}" : "api/mayday", ct);
    public Task<MaydayEventDto> ActOnMaydayAsync(Guid maydayId, string action, MaydayActionRequest request, CancellationToken ct = default)
    {
        if (action is not ("acknowledge" or "assign" or "escalate" or "resolve")) throw new ArgumentOutOfRangeException(nameof(action));
        return PostAsync<MaydayEventDto>($"api/mayday/{maydayId:D}/{action}", request, ct);
    }

    public Task<AlertDto[]> GetAlertsAsync(bool activeOnly = true, CancellationToken ct = default) => GetAsync<AlertDto[]>($"api/alerts?activeOnly={activeOnly.ToString().ToLowerInvariant()}", ct);
    public Task<AlertDto> ActOnAlertAsync(Guid alertId, string action, AlertActionRequest request, CancellationToken ct = default)
    {
        if (action is not ("acknowledge" or "claim" or "resolve" or "escalate")) throw new ArgumentOutOfRangeException(nameof(action));
        return PostAsync<AlertDto>($"api/alerts/{alertId:D}/{action}", request, ct);
    }
    public Task<BulletinDto[]> GetBulletinsAsync(CancellationToken ct = default) => GetAsync<BulletinDto[]>("api/bulletins", ct);
    public Task<StationSnapshotDto> GetStationSnapshotAsync(Guid stationId, CancellationToken ct = default) => GetAsync<StationSnapshotDto>($"api/station/{stationId:D}/snapshot", ct);
    public Task<StationDeliveryPathDto[]> GetStationDeliveryPathsAsync(Guid stationId, CancellationToken ct = default) => GetAsync<StationDeliveryPathDto[]>($"api/station/{stationId:D}/delivery-paths", ct);
    public Task<StationDeliveryDto> AcknowledgeStationDeliveryAsync(Guid deliveryId, StationAcknowledgeRequest request, CancellationToken ct = default) => PostAsync<StationDeliveryDto>($"api/station/deliveries/{deliveryId:D}/acknowledge", request, ct);
    public Task<DispatchDeliveryDto> RetryDispatchDeliveryAsync(Guid deliveryId, RetryDeliveryRequest request, CancellationToken ct = default) => PostAsync<DispatchDeliveryDto>($"api/dispatch/deliveries/{deliveryId:D}/retry", request, ct);
    public Task<RunSheetDto> MarkRunSheetPrintedAsync(Guid runSheetId, NotesActionRequest request, CancellationToken ct = default) => PostAsync<RunSheetDto>($"api/station/run-sheets/{runSheetId:D}/printed", request, ct);
    public Task<StationPrintResultDto> PrintRunSheetAsync(Guid runSheetId, string? printerKey, CancellationToken ct = default)
    {
        var query = string.IsNullOrWhiteSpace(printerKey) ? string.Empty : "?printerKey=" + Uri.EscapeDataString(printerKey.Trim());
        return PostAsync<StationPrintResultDto>($"api/station/run-sheets/{runSheetId:D}/print{query}", new { }, ct);
    }
    public Task<ProviderHealthDto[]> GetProviderHealthAsync(CancellationToken ct = default) => GetAsync<ProviderHealthDto[]>("api/providers/health", ct);
    public Task<CameraFeedDto[]> GetCamerasAsync(CancellationToken ct = default) => GetAsync<CameraFeedDto[]>("api/video/cameras", ct);
    public Task<RadioStreamDto[]> GetRadioStreamsAsync(CancellationToken ct = default) => GetAsync<RadioStreamDto[]>("api/radio/streams", ct);

    public Task<LeaveRequestDto[]> GetLeaveRequestsAsync(CancellationToken ct = default) => GetAsync<LeaveRequestDto[]>("api/workforce/leave", ct);
    public Task<ShiftTradeDto[]> GetShiftTradesAsync(CancellationToken ct = default) => GetAsync<ShiftTradeDto[]>("api/workforce/trades", ct);
    public Task<StaffingSummaryDto> GetStaffingAsync(Guid agencyId, DateOnly date, CancellationToken ct = default) => GetAsync<StaffingSummaryDto>($"api/workforce/staffing?agencyId={agencyId:D}&date={date:yyyy-MM-dd}", ct);
    public Task<RotationDto[]> GetRotationsAsync(CancellationToken ct = default) => GetAsync<RotationDto[]>("api/workforce/rotations", ct);
    public Task<OvertimeOfferDto[]> GetOvertimeOffersAsync(CancellationToken ct = default) => GetAsync<OvertimeOfferDto[]>("api/workforce/overtime", ct);
    public Task<TimesheetEntryDto[]> GetTimesheetsAsync(CancellationToken ct = default) => GetAsync<TimesheetEntryDto[]>("api/workforce/timesheets", ct);
    public Task<StaffingRuleDto[]> GetStaffingRulesAsync(Guid? agencyId = null, CancellationToken ct = default)
    {
        var path = agencyId is Guid id ? $"api/workforce/staffing-rules?agencyId={id:D}" : "api/workforce/staffing-rules";
        return GetAsync<StaffingRuleDto[]>(path, ct);
    }
    public Task<StaffingRuleDto> CreateStaffingRuleAsync(UpsertStaffingRuleRequest request, CancellationToken ct = default) => PostAsync<StaffingRuleDto>("api/workforce/staffing-rules", request, ct);
    public Task<StaffingRuleDto> UpdateStaffingRuleAsync(Guid id, UpsertStaffingRuleRequest request, CancellationToken ct = default) => PutAsync<StaffingRuleDto>($"api/workforce/staffing-rules/{id:D}", request, ct);
    public Task<WorkforceAssignmentDto[]> GetWorkforceAssignmentsAsync(Guid agencyId, DateOnly date, CancellationToken ct = default) => GetAsync<WorkforceAssignmentDto[]>($"api/workforce/assignments?agencyId={agencyId:D}&date={date:yyyy-MM-dd}", ct);
    public Task<WorkforceAssignmentDto> CreateWorkforceAssignmentAsync(UpsertWorkforceAssignmentRequest request, CancellationToken ct = default) => PostAsync<WorkforceAssignmentDto>("api/workforce/assignments", request, ct);
    public Task<WorkforceAssignmentDto> UpdateWorkforceAssignmentAsync(Guid id, UpsertWorkforceAssignmentRequest request, CancellationToken ct = default) => PutAsync<WorkforceAssignmentDto>($"api/workforce/assignments/{id:D}", request, ct);
    public Task<WorkforceAssignmentDto> CallOutWorkforceAssignmentAsync(Guid id, WorkforceActionRequest request, CancellationToken ct = default) => PostAsync<WorkforceAssignmentDto>($"api/workforce/assignments/{id:D}/call-out", request, ct);
    public Task<WorkforceAssignmentDto> RelieveWorkforceAssignmentAsync(Guid id, WorkforceReliefRequest request, CancellationToken ct = default) => PostAsync<WorkforceAssignmentDto>($"api/workforce/assignments/{id:D}/relieve", request, ct);
    public Task<WorkforceStaffingEvaluationDto> GetWorkforceStaffingEvaluationAsync(Guid agencyId, DateOnly date, CancellationToken ct = default) => GetAsync<WorkforceStaffingEvaluationDto>($"api/workforce/staffing-evaluation?agencyId={agencyId:D}&date={date:yyyy-MM-dd}", ct);
    public Task<WorkforceRosterViewDto> GetWorkforceRosterAsync(Guid agencyId, DateOnly date, CancellationToken ct = default) => GetAsync<WorkforceRosterViewDto>($"api/workforce/roster/current?agencyId={agencyId:D}&date={date:yyyy-MM-dd}", ct);
    public Task<WorkforceRosterSnapshotDto[]> GetWorkforceRosterSnapshotsAsync(Guid agencyId, DateOnly date, CancellationToken ct = default) => GetAsync<WorkforceRosterSnapshotDto[]>($"api/workforce/roster/snapshots?agencyId={agencyId:D}&date={date:yyyy-MM-dd}", ct);
    public Task<WorkforceRosterSnapshotDto> PublishWorkforceRosterAsync(Guid agencyId, DateOnly date, PublishWorkforceRosterRequest request, CancellationToken ct = default) => PostAsync<WorkforceRosterSnapshotDto>($"api/workforce/roster/publish?agencyId={agencyId:D}&date={date:yyyy-MM-dd}", request, ct);
    public Task<WorkforceRosterPrintDto> GetWorkforceRosterPrintViewAsync(Guid agencyId, DateOnly date, bool includeChanges, CancellationToken ct = default) => GetAsync<WorkforceRosterPrintDto>($"api/workforce/roster/print-view?agencyId={agencyId:D}&date={date:yyyy-MM-dd}&includeChanges={includeChanges.ToString().ToLowerInvariant()}", ct);
    public Task<WorkforceRosterPrintResultDto> PrintWorkforceRosterAsync(Guid agencyId, DateOnly date, bool includeChanges, string? printerKey, CancellationToken ct = default)
    {
        var path = $"api/workforce/roster/print?agencyId={agencyId:D}&date={date:yyyy-MM-dd}&includeChanges={includeChanges.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(printerKey)) path += "&printerKey=" + Uri.EscapeDataString(printerKey.Trim());
        return PostAsync<WorkforceRosterPrintResultDto>(path, new { }, ct);
    }
    public Task<DmsDocumentSummaryDto[]> GetDmsDocumentsAsync(Guid? agencyId = null, CancellationToken ct = default)
        => GetAsync<DmsDocumentSummaryDto[]>(agencyId is Guid id ? $"api/documents/dms?agencyId={id:D}" : "api/documents/dms", ct);
    public Task<DmsDocumentSummaryDto> CreateDmsDocumentAsync(CreateDmsDocumentRequest request, CancellationToken ct = default) => PostAsync<DmsDocumentSummaryDto>("api/documents/dms", request, ct);
    public Task<DmsDocumentRevisionDto[]> GetDmsRevisionsAsync(Guid documentId, CancellationToken ct = default) => GetAsync<DmsDocumentRevisionDto[]>($"api/documents/dms/{documentId:D}/revisions", ct);
    public Task<DmsDocumentRevisionDto> CreateDmsRevisionAsync(Guid documentId, CreateDmsRevisionRequest request, CancellationToken ct = default) => PostAsync<DmsDocumentRevisionDto>($"api/documents/dms/{documentId:D}/revisions", request, ct);
    public Task<ApprovalWorkflowDto> StartDmsRevisionApprovalAsync(Guid documentId, int version, DmsApprovalRequest request, CancellationToken ct = default) => PostAsync<ApprovalWorkflowDto>($"api/documents/dms/{documentId:D}/revisions/{version}/approval", request, ct);
    public Task<DmsDocumentRevisionDto> PublishDmsRevisionAsync(Guid documentId, int version, PublishDmsRevisionRequest request, CancellationToken ct = default) => PostAsync<DmsDocumentRevisionDto>($"api/documents/dms/{documentId:D}/revisions/{version}/publish", request, ct);
    public Task<DmsDistributionDto[]> GetDmsDistributionsAsync(Guid documentId, CancellationToken ct = default) => GetAsync<DmsDistributionDto[]>($"api/documents/dms/{documentId:D}/distributions", ct);
    public Task<DmsDistributionDto> CreateDmsDistributionAsync(Guid documentId, CreateDmsDistributionRequest request, CancellationToken ct = default) => PostAsync<DmsDistributionDto>($"api/documents/dms/{documentId:D}/distributions", request, ct);
    public Task<DmsReadReceiptDto[]> GetDmsReadReceiptsAsync(Guid documentId, CancellationToken ct = default) => GetAsync<DmsReadReceiptDto[]>($"api/documents/dms/{documentId:D}/read-receipts", ct);
    public Task<DmsRequiredReadStatusDto> GetDmsRequiredReadStatusAsync(Guid documentId, int version, CancellationToken ct = default) => GetAsync<DmsRequiredReadStatusDto>($"api/documents/dms/{documentId:D}/revisions/{version}/required-read", ct);
    public Task<DmsReadReceiptDto> MarkDmsReadAsync(Guid receiptId, CancellationToken ct = default) => PostAsync<DmsReadReceiptDto>($"api/documents/dms/read-receipts/{receiptId:D}/read", new { }, ct);
    public Task<DmsReadReceiptDto> AcknowledgeDmsReadAsync(Guid receiptId, CancellationToken ct = default) => PostAsync<DmsReadReceiptDto>($"api/documents/dms/read-receipts/{receiptId:D}/acknowledge", new { }, ct);
    public Task<DmsAssetReferenceDto[]> GetDmsAssetReferencesAsync(Guid documentId, CancellationToken ct = default) => GetAsync<DmsAssetReferenceDto[]>($"api/documents/dms/{documentId:D}/assets", ct);
    public Task<DmsAssetReferenceDto> AddDmsAssetReferenceAsync(Guid documentId, AddDmsAssetReferenceRequest request, CancellationToken ct = default) => PostAsync<DmsAssetReferenceDto>($"api/documents/dms/{documentId:D}/assets", request, ct);
    public Task<DmsDocumentLinkDto[]> GetDmsDocumentLinksAsync(Guid documentId, CancellationToken ct = default) => GetAsync<DmsDocumentLinkDto[]>($"api/documents/dms/{documentId:D}/links", ct);
    public Task<DmsDocumentLinkDto> LinkDmsDocumentAsync(Guid documentId, CreateDmsDocumentLinkRequest request, CancellationToken ct = default) => PostAsync<DmsDocumentLinkDto>($"api/documents/dms/{documentId:D}/links", request, ct);
    public Task<DmsDocumentLinkDto[]> GetDmsEntityLinksAsync(string entityType, string entityKey, CancellationToken ct = default) => GetAsync<DmsDocumentLinkDto[]>($"api/documents/dms/entity-links?entityType={Uri.EscapeDataString(entityType)}&entityKey={Uri.EscapeDataString(entityKey)}", ct);

    public Task<ApprovalWorkflowDto[]> GetApprovalWorkflowsAsync(CancellationToken ct = default) => GetAsync<ApprovalWorkflowDto[]>("api/documents/workflows", ct);
    public Task<ApprovalStepDto[]> GetApprovalStepsAsync(Guid workflowId, CancellationToken ct = default) => GetAsync<ApprovalStepDto[]>($"api/documents/workflows/{workflowId:D}/steps", ct);
    public Task<ApprovalStepDto> DecideApprovalStepAsync(Guid stepId, DecideApprovalStepRequest request, CancellationToken ct = default) => PostAsync<ApprovalStepDto>($"api/documents/workflow-steps/{stepId:D}/decide", request, ct);
    public Task<FormTemplateDto[]> GetFormTemplatesAsync(CancellationToken ct = default) => GetAsync<FormTemplateDto[]>("api/forms/templates", ct);
    public Task<FormTemplateDto> CreateFormTemplateAsync(CreateFormTemplateRequest request, CancellationToken ct = default) => PostAsync<FormTemplateDto>("api/forms/templates", request, ct);
    public Task<FormTemplateDto> PublishFormTemplateAsync(Guid templateId, CancellationToken ct = default) => PostAsync<FormTemplateDto>($"api/forms/templates/{templateId:D}/publish", new { }, ct);
    public Task<FormInstanceDto[]> GetFormsAsync(CancellationToken ct = default) => GetAsync<FormInstanceDto[]>("api/forms", ct);
    public Task<FormInstanceDto> CreateFormAsync(CreateFormInstanceRequest request, CancellationToken ct = default) => PostAsync<FormInstanceDto>("api/forms", request, ct);
    public Task<FormSignatureDto> SignFormStrongAsync(Guid formId, SignFormStrongRequest request, CancellationToken ct = default) => PostAsync<FormSignatureDto>($"api/forms/{formId:D}/sign-strong", request, ct);
    public Task<FormSignatureDto[]> GetFormSignaturesAsync(Guid formId, CancellationToken ct = default) => GetAsync<FormSignatureDto[]>($"api/forms/{formId:D}/signatures", ct);

    public Task<InspectionDto[]> GetInspectionsAsync(CancellationToken ct = default) => GetAsync<InspectionDto[]>("api/rms/inspections", ct);
    public Task<PermitDto[]> GetPermitsAsync(CancellationToken ct = default) => GetAsync<PermitDto[]>("api/rms/permits", ct);
    public Task<MaintenanceRecordDto[]> GetMaintenanceAsync(CancellationToken ct = default) => GetAsync<MaintenanceRecordDto[]>("api/rms/maintenance", ct);
    public Task<NerisReportDto[]> GetNerisReportsAsync(CancellationToken ct = default) => GetAsync<NerisReportDto[]>("api/rms/neris", ct);
    public Task<ViolationDto[]> GetViolationsAsync(CancellationToken ct = default) => GetAsync<ViolationDto[]>("api/rms/violations", ct);
    public Task<HazmatRecordDto[]> GetHazmatAsync(CancellationToken ct = default) => GetAsync<HazmatRecordDto[]>("api/rms/hazmat", ct);
    public Task<InvestigationDto[]> GetInvestigationsAsync(CancellationToken ct = default) => GetAsync<InvestigationDto[]>("api/rms/investigations", ct);
    public Task<PersonnelQualificationDto[]> GetQualificationsAsync(CancellationToken ct = default) => GetAsync<PersonnelQualificationDto[]>("api/rms/qualifications", ct);

    public Task<ClientHeartbeatDto[]> GetClientsAsync(CancellationToken ct = default) => GetAsync<ClientHeartbeatDto[]>("api/clients", ct);
    public Task<ProviderConfigDto[]> GetProvidersAsync(CancellationToken ct = default) => GetAsync<ProviderConfigDto[]>("api/providers", ct);
    public Task<IntegrationDashboardDto> GetIntegrationDashboardAsync(CancellationToken ct = default) => GetAsync<IntegrationDashboardDto>("api/integrations/dashboard", ct);
    public Task<IntegrationEventDto[]> GetIntegrationEventsAsync(Guid? agencyId = null, string? category = null, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (agencyId is Guid id) parts.Add($"agencyId={id:D}");
        if (!string.IsNullOrWhiteSpace(category)) parts.Add("category=" + Uri.EscapeDataString(category));
        return GetAsync<IntegrationEventDto[]>("api/integrations/events" + (parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts)), ct);
    }
    public Task<IntegrationOperationDto[]> GetIntegrationOperationsAsync(Guid? agencyId = null, CancellationToken ct = default) => GetAsync<IntegrationOperationDto[]>(agencyId is Guid id ? $"api/integrations/operations?agencyId={id:D}" : "api/integrations/operations", ct);
    public Task<IntegrationAttemptDto[]> GetIntegrationAttemptsAsync(Guid operationId, CancellationToken ct = default) => GetAsync<IntegrationAttemptDto[]>($"api/integrations/operations/{operationId:D}/attempts", ct);
    public Task<RecordingReferenceDto[]> GetRecordingReferencesAsync(Guid? incidentId = null, CancellationToken ct = default) => GetAsync<RecordingReferenceDto[]>(incidentId is Guid id ? $"api/integrations/recordings?incidentId={id:D}" : "api/integrations/recordings", ct);
    public Task<IntegrationEventDto> Ingest911GatewayEventAsync(Ingest911GatewayEventRequest request, CancellationToken ct = default) => PostAsync<IntegrationEventDto>("api/integrations/911/events", request, ct);
    public Task<IntegrationEventDto> IngestRadioGatewayEventAsync(IngestRadioGatewayEventRequest request, CancellationToken ct = default) => PostAsync<IntegrationEventDto>("api/integrations/radio/events", request, ct);
    public Task<IntegrationEventDto> IngestAisGatewayEventAsync(IngestAisGatewayEventRequest request, CancellationToken ct = default) => PostAsync<IntegrationEventDto>("api/integrations/ais/events", request, ct);
    public Task<ProviderRouteResultDto> GetProviderRouteAsync(ProviderRouteRequest request, CancellationToken ct = default) => PostAsync<ProviderRouteResultDto>("api/integrations/routing/route", request, ct);
    public Task<RecordingReferenceDto[]> QueryRecordingsAsync(RecordingLookupRequest request, CancellationToken ct = default) => PostAsync<RecordingReferenceDto[]>("api/integrations/recordings/query", request, ct);
    public Task<CameraDiscoveryCandidateDto[]> DiscoverCamerasAsync(CameraDiscoveryRequest request, CancellationToken ct = default) => PostAsync<CameraDiscoveryCandidateDto[]>("api/integrations/cameras/discover", request, ct);
    public Task<CameraFeedDto> ImportDiscoveredCameraAsync(ImportDiscoveredCameraRequest request, CancellationToken ct = default) => PostAsync<CameraFeedDto>("api/integrations/cameras/import", request, ct);
    public Task<IntegrationOperationDto> ClearRadioEmergencyAsync(RadioEmergencyClearRequest request, CancellationToken ct = default) => PostAsync<IntegrationOperationDto>("api/integrations/radio/clear-emergency", request, ct);
    public Task<IntegrationOperationDto> RetryIntegrationOperationAsync(Guid operationId, RetryIntegrationOperationRequest request, CancellationToken ct = default) => PostAsync<IntegrationOperationDto>($"api/integrations/operations/{operationId:D}/retry", request, ct);
    public Task<IntegrationOperationDto> ReconcileIntegrationOperationAsync(Guid operationId, ReconcileIntegrationOperationRequest request, CancellationToken ct = default) => PostAsync<IntegrationOperationDto>($"api/integrations/operations/{operationId:D}/reconcile", request, ct);
    public Task<DeviceIdentityDto[]> GetDevicesAsync(CancellationToken ct = default) => GetAsync<DeviceIdentityDto[]>("api/security/devices", ct);
    public Task<UserSecurityDto[]> GetUsersAsync(CancellationToken ct = default) => GetAsync<UserSecurityDto[]>("api/security/users", ct);
    public Task<AuditRecordDto[]> GetAuditAsync(CancellationToken ct = default) => GetAsync<AuditRecordDto[]>("api/audit", ct);
    public Task<AuditIntegrityValidationDto> VerifyAuditAsync(CancellationToken ct = default) => GetAsync<AuditIntegrityValidationDto>("api/audit/verify", ct);
    public Task<HaStatusDto> GetHaStatusAsync(CancellationToken ct = default) => GetAsync<HaStatusDto>("api/admin/ha", ct);
    public Task<HaClusterEventDto[]> GetHaEventsAsync(int limit = 50, CancellationToken ct = default) => GetAsync<HaClusterEventDto[]>($"api/admin/ha/events?limit={Math.Clamp(limit, 1, 500)}", ct);
    public Task<OperationsOverviewDto> GetOperationsOverviewAsync(CancellationToken ct = default) => GetAsync<OperationsOverviewDto>("api/admin/operations", ct);
    public Task<SearchResultDto[]> ExtendedSearchAsync(string query, CancellationToken ct = default) => GetAsync<SearchResultDto[]>("api/search/extended?q=" + Uri.EscapeDataString(query), ct);
    public Task<MobileSyncResponseDto> MobileSyncAsync(MobileSyncRequest request, CancellationToken ct = default) => PostAsync<MobileSyncResponseDto>("api/mobile/sync", request, ct);
    public Task<OfflineAuthorizationLeaseDto> IssueOfflineAuthorizationLeaseAsync(IssueOfflineAuthorizationLeaseRequest request, CancellationToken ct = default) => PostAsync<OfflineAuthorizationLeaseDto>("api/me/offline-lease", request, ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        var value = await http.GetFromJsonAsync<T>(path, ct);
        return value ?? throw new InvalidOperationException($"DISRoute API returned no content for {path}.");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        using var response = await http.PostAsJsonAsync(path, body, ct);
        return await ReadAsync<T>(response, ct);
    }
    private async Task<T> PutAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        using var response = await http.PutAsJsonAsync(path, body, ct);
        return await ReadAsync<T>(response, ct);
    }


    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        if (response.IsSuccessStatusCode) return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;
        var error = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"DISRoute API returned {(int)response.StatusCode}: {error}");
    }
}

public sealed record DispatchCommitResponse(DispatchActionDto Action, IReadOnlyList<DispatchDeliveryDto> Deliveries, IncidentDto Incident);
public sealed record OperationsOverviewDto(DateTimeOffset AsOfUtc, int ActiveIncidents, int Units, int ActiveAlerts, int ConnectedClients, IReadOnlyList<ProviderHealthDto> ProviderHealth, HaStatusDto Ha);
