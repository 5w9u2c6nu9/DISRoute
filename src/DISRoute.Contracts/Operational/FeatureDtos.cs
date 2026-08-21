namespace DISRoute.Contracts.Operational;

public sealed record CallRecordDto(Guid Id, Guid AgencyId, string Source, string ProviderCallId, string CallerName, string CallbackNumber, string SuppliedLocation, string Nature, string Disposition, Guid? IncidentId, DateTimeOffset ReceivedAtUtc, DateTimeOffset? AnsweredAtUtc, DateTimeOffset? EndedAtUtc);
public sealed record CreateCallRecordRequest(Guid AgencyId, string Source, string ProviderCallId, string CallerName, string CallbackNumber, string SuppliedLocation, string Nature, Guid? IncidentId);
public sealed record LinkCallRequest(Guid IncidentId, string Actor);

public sealed record DispatchActionDto(Guid Id, Guid AgencyId, Guid IncidentId, IReadOnlyList<Guid> UnitIds, string State, DateTimeOffset CommittedAtUtc, string Actor);
public sealed record DispatchDeliveryDto(Guid Id, Guid DispatchActionId, string Provider, string Destination, string State, int Attempts, string? LastError, DateTimeOffset UpdatedAtUtc);
public sealed record CommitDispatchRequest(long ExpectedIncidentVersion, IReadOnlyList<Guid> UnitIds, IReadOnlyList<string> DeliveryProviders, string? IdempotencyKey);
public sealed record RetryDeliveryRequest(string Actor);

public sealed record FireWatchDto(Guid Id, Guid AgencyId, Guid IncidentId, Guid UnitId, string Supervisor, DateTimeOffset StartedAtUtc, DateTimeOffset? ExpectedEndUtc, int CheckIntervalMinutes, string Status, string Notes);
public sealed record CreateFireWatchRequest(Guid AgencyId, Guid IncidentId, Guid UnitId, string Supervisor, DateTimeOffset? ExpectedEndUtc, int CheckIntervalMinutes, string Notes);
public sealed record FireWatchCheckDto(Guid Id, Guid FireWatchId, string Actor, string Notes, DateTimeOffset CheckedAtUtc);
public sealed record RelocationDto(Guid Id, Guid AgencyId, Guid UnitId, Guid DestinationStationId, string Reason, string Status, DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc);
public sealed record CreateRelocationRequest(Guid AgencyId, Guid UnitId, Guid DestinationStationId, string Reason);

public sealed record PremiseDto(Guid Id, Guid AgencyId, string Name, string Address, string Occupancy, string AccessInformation, string FireProtection, string UtilityShutoffs, string Hazards, string Classification, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertPremiseRequest(Guid AgencyId, string Name, string Address, string Occupancy, string AccessInformation, string FireProtection, string UtilityShutoffs, string Hazards, string Classification);
public sealed record PreplanDto(Guid Id, Guid AgencyId, Guid PremiseId, int Version, string Status, string Title, string OperationalContent, string Author, DateTimeOffset EffectiveAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreatePreplanRequest(Guid AgencyId, Guid PremiseId, string Title, string OperationalContent, string Author);
public sealed record CrgAssetDto(Guid Id, Guid AgencyId, Guid PremiseId, string FileName, string Sha256, string? Floor, string? OverlayType, int? Revision, double? Rotation, double? Scale, double? Opacity, string Source, DateTimeOffset UploadedAtUtc);
public sealed record RegisterCrgAssetRequest(Guid AgencyId, Guid PremiseId, string FileName, string Sha256, string? Floor, string? OverlayType, int? Revision, double? Rotation, double? Scale, double? Opacity, string Source);
public sealed record HydrantDto(Guid Id, Guid AgencyId, string AssetId, string Address, double Latitude, double Longitude, int? MainSizeInches, int? StaticPsi, int? ResidualPsi, int? FlowGpm, string Status, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertHydrantRequest(Guid AgencyId, string AssetId, string Address, double Latitude, double Longitude, int? MainSizeInches, int? StaticPsi, int? ResidualPsi, int? FlowGpm, string Status);

public sealed record MapObjectDto(Guid Id, Guid AgencyId, Guid? IncidentId, string Kind, string Name, string GeometryGeoJson, string VisibilityScope, DateTimeOffset StartsAtUtc, DateTimeOffset? EndsAtUtc, string Actor, long Version);
public sealed record CreateMapObjectRequest(Guid AgencyId, Guid? IncidentId, string Kind, string Name, string GeometryGeoJson, string VisibilityScope, DateTimeOffset? EndsAtUtc);
public sealed record RouteRequest(Guid AgencyId, double FromLatitude, double FromLongitude, double ToLatitude, double ToLongitude, Guid? UnitId, Guid? IncidentId, bool AvoidRestrictions);
public sealed record RouteResultDto(string Provider, double DistanceMeters, int DurationSeconds, string Polyline, IReadOnlyList<string> Warnings, DateTimeOffset CalculatedAtUtc);
public sealed record SiteHydrantDto(HydrantDto Hydrant, double? DistanceMeters);
public sealed record SiteRoadRestrictionDto(RoadRestrictionDto Restriction, double? DistanceMeters);
public sealed record IncidentSiteIntelligenceDto(
    Guid IncidentId,
    IReadOnlyList<PremiseDto> Premises,
    IReadOnlyList<PreplanDto> PublishedPreplans,
    IReadOnlyList<CrgAssetDto> CrgAssets,
    IReadOnlyList<SiteHydrantDto> Hydrants,
    IReadOnlyList<SiteRoadRestrictionDto> RoadRestrictions,
    IReadOnlyList<MapObjectDto> MapObjects,
    double? SceneLatitude,
    double? SceneLongitude,
    IReadOnlyList<string> Warnings);
public sealed record VesselObservationDto(Guid Id, Guid AgencyId, string Mmsi, string Name, double Latitude, double Longitude, double SpeedKnots, double CourseDegrees, string Source, DateTimeOffset ObservedAtUtc);
public sealed record UpsertVesselObservationRequest(Guid AgencyId, string Mmsi, string Name, double Latitude, double Longitude, double SpeedKnots, double CourseDegrees, string Source, DateTimeOffset ObservedAtUtc);

public sealed record AlertDto(Guid Id, Guid AgencyId, Guid? IncidentId, string Type, string Severity, string CorrelationKey, string AffectedEntity, string State, string Summary, string? ClaimedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateAlertRequest(Guid AgencyId, Guid? IncidentId, string Type, string Severity, string CorrelationKey, string AffectedEntity, string Summary);
public sealed record AlertActionRequest(string Actor, string? Notes);
public sealed record BulletinDto(Guid Id, Guid AgencyId, string Category, string Title, string Body, string Severity, DateTimeOffset StartsAtUtc, DateTimeOffset? EndsAtUtc, string CreatedBy);
public sealed record CreateBulletinRequest(Guid AgencyId, string Category, string Title, string Body, string Severity, DateTimeOffset StartsAtUtc, DateTimeOffset? EndsAtUtc, string CreatedBy);

public sealed record StationDeliveryDto(Guid Id, Guid AgencyId, Guid StationId, Guid IncidentId, Guid DispatchActionId, string DisplayState, string AlertState, string PrintState, string AckState, int Attempt, DateTimeOffset UpdatedAtUtc);
public sealed record StationAcknowledgeRequest(string DeviceId, string Actor);
public sealed record RunSheetDto(Guid Id, Guid AgencyId, Guid StationId, Guid IncidentId, int Revision, string Status, string Content, DateTimeOffset GeneratedAtUtc, DateTimeOffset? PrintedAtUtc);
public sealed record StationSnapshotDto(Guid StationId, IReadOnlyList<IncidentDto> Incidents, IReadOnlyList<UnitDto> Units, IReadOnlyList<StationDeliveryDto> Deliveries, IReadOnlyList<RunSheetDto> RunSheets, DateTimeOffset AsOfUtc);
public sealed record StationDeliveryPathDto(Guid Id, Guid StationDeliveryId, Guid DispatchActionId, Guid IncidentId, string Provider, string Destination, string State, int Attempts, string? LastError, DateTimeOffset UpdatedAtUtc);
public sealed record StationPrintResultDto(Guid OperationId, Guid RunSheetId, string State, string? ProviderReference, string? Error, DateTimeOffset CompletedAtUtc);
public sealed record ClientHeartbeatDto(Guid Id, Guid AgencyId, string DeviceId, string ClientRole, string Platform, string Version, string User, string State, double? ClockOffsetSeconds, DateTimeOffset LastSeenUtc);
public sealed record HeartbeatRequest(Guid AgencyId, string DeviceId, string ClientRole, string Platform, string Version, string User, double? ClockOffsetSeconds);

public sealed record ProviderHealthDto(string Key, string DisplayName, string Category, string State, string? Detail, DateTimeOffset CheckedAtUtc);
public sealed record ProviderConfigDto(string Key, string DisplayName, string Category, bool Enabled, string Endpoint, string AuthenticationMode, IReadOnlyDictionary<string, string> Settings, int Version, string State);
public sealed record UpsertProviderConfigRequest(string DisplayName, string Category, bool Enabled, string Endpoint, string AuthenticationMode, Dictionary<string, string> Settings);
public sealed record CameraFeedDto(Guid Id, Guid AgencyId, Guid? StationId, Guid? PremiseId, string Name, string Uri, string Provider, string State, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertCameraFeedRequest(Guid AgencyId, Guid? StationId, Guid? PremiseId, string Name, string Uri, string Provider);
public sealed record RadioStreamDto(Guid Id, Guid AgencyId, string Name, string IcecastUri, string Codec, string AuthorizationScope, string State);
public sealed record UpsertRadioStreamRequest(Guid AgencyId, string Name, string IcecastUri, string Codec, string AuthorizationScope);

public sealed record LeaveRequestDto(Guid Id, Guid AgencyId, string EmployeeName, DateOnly StartDate, DateOnly EndDate, string LeaveType, string Reason, string Status, string? ReviewedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateLeaveRequest(Guid AgencyId, string EmployeeName, DateOnly StartDate, DateOnly EndDate, string LeaveType, string Reason);
public sealed record ReviewLeaveRequest(string Actor, bool Approved, string? Notes);
public sealed record ShiftTradeDto(Guid Id, Guid AgencyId, Guid OfferedShiftId, string OfferedBy, string RequestedWith, string Status, string? ReviewedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateShiftTradeRequest(Guid AgencyId, Guid OfferedShiftId, string OfferedBy, string RequestedWith);
public sealed record ReviewShiftTradeRequest(string Actor, bool Approved);
public sealed record StaffingSummaryDto(Guid AgencyId, DateOnly Date, int RequiredPositions, int FilledPositions, int Vacancies, IReadOnlyList<string> Warnings);

public sealed record DocumentVersionDto(Guid Id, Guid DocumentId, int Version, string Content, string Status, string Author, DateTimeOffset CreatedAtUtc);
public sealed record DocumentAcknowledgementDto(Guid Id, Guid DocumentId, string Recipient, string Status, DateTimeOffset AssignedAtUtc, DateTimeOffset? AcknowledgedAtUtc);
public sealed record AssignAcknowledgementRequest(IReadOnlyList<string> Recipients, string Actor);
public sealed record AcknowledgeDocumentRequest(string Recipient);
public sealed record FormTemplateDto(Guid Id, Guid AgencyId, string Name, int Version, string Status, string SchemaJson, string CreatedBy, DateTimeOffset UpdatedAtUtc);
public sealed record CreateFormTemplateRequest(Guid AgencyId, string Name, string SchemaJson, string CreatedBy);
public sealed record FormInstanceDto(Guid Id, Guid AgencyId, Guid TemplateId, Guid? IncidentId, string Status, string ValuesJson, string CreatedBy, string? SignedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateFormInstanceRequest(Guid AgencyId, Guid TemplateId, Guid? IncidentId, string ValuesJson, string CreatedBy);
public sealed record SignFormRequest(string Actor);

public sealed record InspectionDto(Guid Id, Guid AgencyId, Guid PremiseId, string Type, DateOnly InspectionDate, string Inspector, string Status, string Findings, DateTimeOffset UpdatedAtUtc);
public sealed record CreateInspectionRequest(Guid AgencyId, Guid PremiseId, string Type, DateOnly InspectionDate, string Inspector, string Findings);
public sealed record PermitDto(Guid Id, Guid AgencyId, Guid PremiseId, string PermitType, string Number, string Status, DateOnly IssuedDate, DateOnly? ExpiresDate, string Holder, string Notes);
public sealed record CreatePermitRequest(Guid AgencyId, Guid PremiseId, string PermitType, string Holder, DateOnly IssuedDate, DateOnly? ExpiresDate, string Notes);
public sealed record MaintenanceRecordDto(Guid Id, Guid AgencyId, Guid UnitId, string Category, string Summary, string Status, DateOnly OpenedDate, DateOnly? CompletedDate, decimal? Cost);
public sealed record CreateMaintenanceRequest(Guid AgencyId, Guid UnitId, string Category, string Summary, DateOnly OpenedDate, decimal? Cost);
public sealed record NerisReportDto(Guid Id, Guid AgencyId, Guid IncidentId, string Status, string PayloadJson, string ValidationState, IReadOnlyList<string> ValidationErrors, string? SubmissionId, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertNerisReportRequest(Guid AgencyId, Guid IncidentId, string PayloadJson);
public sealed record SubmitNerisReportRequest(string Actor);

public sealed record DeviceIdentityDto(Guid Id, Guid AgencyId, string DeviceId, string Name, string Role, string Platform, string State, string? CertificateThumbprint, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastSeenAtUtc);
public sealed record RegisterDeviceRequest(Guid AgencyId, string DeviceId, string Name, string Role, string Platform, string? CertificateThumbprint);
public sealed record UserSecurityDto(string UserId, string DisplayName, Guid AgencyId, IReadOnlyList<string> Roles, bool TotpEnrolled, bool PasskeyEnrolled, bool BreakGlass, string State, DateTimeOffset UpdatedAtUtc);
public sealed record UpdateUserSecurityRequest(IReadOnlyList<string> Roles, bool TotpEnrolled, bool PasskeyEnrolled, bool BreakGlass, string State);
public sealed record AuditRecordDto(Guid Id, Guid AgencyId, string Actor, string Action, string EntityType, string EntityId, string Summary, string Source, DateTimeOffset OccurredAtUtc);

public sealed record BackupStatusDto(string Component, string State, DateTimeOffset? LastSuccessfulBackupUtc, DateTimeOffset? LastVerifiedRestoreUtc, string? Detail);
public sealed record BackupArtifactDto(string FileName, long SizeBytes, DateTimeOffset CreatedAtUtc, int StateSchemaVersion, string SourceProvider, string StateSha256, string ArchiveSha256, bool Verified, string? Note);
public sealed record CreatePortableBackupRequest(string Actor, string? Note);
public sealed record BackupVerificationDto(string FileName, bool Valid, int? StateSchemaVersion, DateTimeOffset? CreatedAtUtc, string? StateSha256, string? ArchiveSha256, string? Error);
public sealed record StageRestoreRequest(string Actor, string Confirmation);
public sealed record StageRestoreResultDto(string FileName, bool Staged, bool RestartRequired, string Detail);
public sealed record ReplicationStatusDto(string Site, string Role, string State, long LagBytes, double LagSeconds, bool PromotionEligible, DateTimeOffset CheckedAtUtc);
public sealed record HaNodeInfoDto(string NodeId, Guid InstanceId, string Site, string ConfiguredRole, string EffectiveRole, string State, bool AcceptingWrites, bool Stale, string AdvertisedAddress, string Version, long LeaseEpoch, DateTimeOffset StartedAtUtc, DateTimeOffset LastHeartbeatUtc, double HeartbeatAgeSeconds, DateTimeOffset? DrainedUntilUtc, string? LastError);
public sealed record HaClusterEventDto(Guid Id, string NodeId, string EventType, string DetailsJson, DateTimeOffset OccurredAtUtc);
public sealed record HaDrainRequest(int Seconds, string Reason);
public sealed record HaResumeRequest(string Reason);
public sealed record HaControlResultDto(string NodeId, string State, bool AcceptingWrites, DateTimeOffset? DrainedUntilUtc, string Detail);
public sealed record HaStatusDto(string ActiveSite, string ActiveNode, string PreferredFailoverOrder, IReadOnlyList<ReplicationStatusDto> Replicas, IReadOnlyList<BackupStatusDto> Backups, bool SplitBrainProtected, DateTimeOffset AsOfUtc)
{
    public string TopologyState { get; init; } = "Unknown";
    public string CurrentNodeId { get; init; } = string.Empty;
    public string CurrentNodeRole { get; init; } = string.Empty;
    public long LeaseEpoch { get; init; }
    public DateTimeOffset? LastTransitionUtc { get; init; }
    public bool AutomaticGeographicPromotionEnabled { get; init; }
    public string DatabaseTopology { get; init; } = "SharedPrimary";
    public string DatabaseFencingMode { get; init; } = "None";
    public bool ExternalWitnessConfigured { get; init; }
    public IReadOnlyList<HaNodeInfoDto> Nodes { get; init; } = [];
    public string? Detail { get; init; }
}
public sealed record ConfigVersionDto(Guid Id, Guid AgencyId, string Area, int Version, string State, string ContentJson, string CreatedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset? PublishedAtUtc);
public sealed record CreateConfigDraftRequest(Guid AgencyId, string Area, string ContentJson, string CreatedBy);
public sealed record PublishConfigRequest(string Actor);

public sealed record MobileSyncRequest(
    Guid AgencyId,
    string DeviceId,
    DateTimeOffset? SinceUtc,
    IReadOnlyList<QueuedMobileOperationDto> QueuedOperations,
    Guid? UnitId = null,
    string? OfflineAuthorizationLease = null);
public sealed record QueuedMobileOperationDto(Guid OperationId, string Kind, Guid? IncidentId, string PayloadJson, DateTimeOffset OccurredAtUtc);
public sealed record MobileOperationResultDto(Guid OperationId, string State, string? Detail);
public sealed record MobileDocumentDto(Guid DocumentId, int Version, string Title, string Classification, string ContentSha256, string Content, IReadOnlyList<DmsAssetReferenceDto> Assets, bool RequiredRead, bool Acknowledged);
public sealed record MobileOperationalPackageDto(
    Guid AgencyId,
    string UserId,
    string DeviceId,
    DateTimeOffset GeneratedAtUtc,
    Guid? SelectedUnitId,
    UnitDto? SelectedUnit,
    IncidentDto? CurrentIncident,
    IReadOnlyList<WorkforceAssignmentDto> Crew,
    IncidentSiteIntelligenceDto? SiteIntelligence,
    IReadOnlyList<MaydayEventDto> Maydays,
    IReadOnlyList<OperationalMessageDto> OperationalMessages,
    IReadOnlyList<ChatChannelSummaryDto> ChatChannels,
    IReadOnlyList<ChatMessageDto> ChatMessages,
    IReadOnlyList<RadioStreamDto> RadioStreams,
    IReadOnlyList<MobileDocumentDto> Documents,
    IReadOnlyList<string> Warnings);
public sealed record MobileConnectivityDto(string State, DateTimeOffset ServerTimeUtc, DateTimeOffset? LastSuccessfulSyncUtc, int PendingOperations, double? CacheAgeSeconds, string? Detail);
public sealed record MobileSyncResponseDto(
    DateTimeOffset ServerTimeUtc,
    IReadOnlyList<IncidentDto> Incidents,
    IReadOnlyList<UnitDto> Units,
    IReadOnlyList<PreplanDto> Preplans,
    IReadOnlyList<CrgAssetDto> CrgAssets,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<MobileOperationResultDto> OperationResults,
    MobileOperationalPackageDto? OperationalPackage = null,
    MobileConnectivityDto? Connectivity = null);
public sealed record MobileUnitStatusPayload(Guid UnitId, string FromStatus, string ToStatus, DateTimeOffset ObservedUnitUpdatedAtUtc);
public sealed record MobileNarrativePayload(string Text);
public sealed record MobileMessagePayload(string Channel, string Text);
public sealed record MobileChatPayload(Guid ChannelId, string Text);

public sealed record CompleteDeliveryRequest(bool Success, string? Error, string Actor);
public sealed record NotesActionRequest(string Actor, string? Notes);

public sealed record DuplicateCandidateDto(Guid IncidentId, string IncidentNumber, string Nature, string Address, double Score, string Reason);
public sealed record DuplicateCheckRequest(Guid AgencyId, string Nature, string Address, string CallerPhone, double? Latitude, double? Longitude);
public sealed record ResponseRecommendationRequest(Guid AgencyId, Guid IncidentId, string Nature, string Priority, Guid StationId, IReadOnlyList<string> RequiredCapabilities);
public sealed record RecommendedUnitDto(Guid UnitId, string CallSign, string UnitType, double Score, IReadOnlyList<string> Reasons);
public sealed record ResponseRecommendationDto(Guid IncidentId, IReadOnlyList<RecommendedUnitDto> Units, IReadOnlyList<string> Warnings, DateTimeOffset CalculatedAtUtc);
public sealed record IncidentRelationshipDto(Guid Id, Guid AgencyId, Guid SourceIncidentId, Guid TargetIncidentId, string RelationshipType, string Actor, DateTimeOffset CreatedAtUtc);
public sealed record CreateIncidentRelationshipRequest(Guid AgencyId, Guid TargetIncidentId, string RelationshipType, string Actor);
public sealed record MergeIncidentsRequest(Guid TargetIncidentId, long ExpectedSourceVersion, long ExpectedTargetVersion, string Reason, string Actor, string? IdempotencyKey);
public sealed record SplitIncidentRequest(long ExpectedVersion, string Nature, string Priority, string Address, string Notes, string Actor, string? IdempotencyKey);
public sealed record OperationalGroupDto(Guid Id, Guid AgencyId, Guid IncidentId, string Name, string Type, string Leader, string Channel, string Status, DateTimeOffset CreatedAtUtc, IReadOnlyList<string>? MemberUserIds = null);
public sealed record CreateOperationalGroupRequest(Guid AgencyId, Guid IncidentId, string Name, string Type, string Leader, string Channel, IReadOnlyList<string>? MemberUserIds = null);
public sealed record UpdateOperationalGroupMembersRequest(IReadOnlyList<string> MemberUserIds);
public sealed record CommandAssignmentDto(Guid Id, Guid AgencyId, Guid IncidentId, string CommandName, Guid? UnitId, string Person, string Actor, DateTimeOffset EffectiveAtUtc, DateTimeOffset? EndedAtUtc);
public sealed record SetCommandRequest(Guid AgencyId, Guid IncidentId, string CommandName, Guid? UnitId, string Person, string Actor);
public sealed record MutualAidRequestDto(Guid Id, Guid AgencyId, Guid IncidentId, string RequestedAgency, string ResourceType, int Quantity, string State, string RequestedBy, DateTimeOffset RequestedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CreateMutualAidRequest(Guid AgencyId, Guid IncidentId, string RequestedAgency, string ResourceType, int Quantity, string RequestedBy);
public sealed record MaydayEventDto(Guid Id, Guid AgencyId, Guid IncidentId, Guid? UnitId, string Person, string Location, string Channel, string State, string DeclaredBy, DateTimeOffset DeclaredAtUtc, DateTimeOffset? ResolvedAtUtc, string? AcknowledgedBy = null, DateTimeOffset? AcknowledgedAtUtc = null, string? AssignedTo = null, int EscalationLevel = 0, string? Notes = null, DateTimeOffset? UpdatedAtUtc = null);
public sealed record DeclareMaydayRequest(Guid AgencyId, Guid IncidentId, Guid? UnitId, string Person, string Location, string Channel, string DeclaredBy);
public sealed record MaydayActionRequest(string? AssignedTo, string? Notes);
public sealed record ResponsePlanDto(Guid Id, Guid AgencyId, string Name, string NaturePattern, string Priority, IReadOnlyList<string> RequiredUnitTypes, IReadOnlyList<string> RequiredCapabilities, bool Enabled, int Version, string State, string UpdatedBy, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertResponsePlanRequest(Guid AgencyId, string Name, string NaturePattern, string Priority, IReadOnlyList<string> RequiredUnitTypes, IReadOnlyList<string> RequiredCapabilities, bool Enabled, string Actor);

public sealed record PresenceDto(string ConnectionId, Guid AgencyId, Guid? IncidentId, string User, string Panel, string? Field, DateTimeOffset UpdatedAtUtc);
public sealed record WorkspaceLayoutDto(Guid Id, Guid AgencyId, string UserId, string Name, string Role, string WorkstationId, string Theme, string Density, string LayoutJson, bool IsDefault, DateTimeOffset UpdatedAtUtc);
public sealed record SaveWorkspaceLayoutRequest(Guid AgencyId, string UserId, string Name, string Role, string WorkstationId, string Theme, string Density, string LayoutJson, bool IsDefault);
public sealed record ThemePackageDto(Guid Id, Guid AgencyId, string Key, string DisplayName, int ContractVersion, string State, string ResourcesJson, DateTimeOffset UpdatedAtUtc);
public sealed record UpsertThemePackageRequest(Guid AgencyId, string Key, string DisplayName, int ContractVersion, string ResourcesJson);

public sealed record CollaborativeNarrativeDto(Guid IncidentId, long Version, string Text, string LastEditor, DateTimeOffset UpdatedAtUtc);
public sealed record CollaborativeNarrativeOperationDto(Guid Id, Guid IncidentId, long ClientBaseVersion, long AppliedBaseVersion, long ResultVersion, int Start, int DeleteCount, string InsertText, string Actor, bool Rebased, DateTimeOffset AppliedAtUtc);
public sealed record CollaborativeNarrativePatchRequest(long ExpectedVersion, int Start, int DeleteCount, string InsertText, string Actor, string? IdempotencyKey);
public sealed record ReplaceCollaborativeNarrativeRequest(long ExpectedVersion, string Text, string Actor, string? IdempotencyKey);

public sealed record AdditionalAlarmDto(Guid Id, Guid AgencyId, Guid IncidentId, int AlarmNumber, Guid DispatchActionId, IReadOnlyList<Guid> UnitIds, string Actor, DateTimeOffset CommittedAtUtc);
public sealed record CommitAdditionalAlarmRequest(long ExpectedIncidentVersion, IReadOnlyList<Guid> UnitIds, IReadOnlyList<string> DeliveryProviders, string Actor, string? IdempotencyKey);
public sealed record CancelDispatchRequest(string Reason, string Actor, string? IdempotencyKey);
public sealed record AvlObservationDto(Guid Id, Guid AgencyId, Guid UnitId, Guid? IncidentId, double Latitude, double Longitude, double SpeedKph, double? IncidentDistanceMeters, string Source, DateTimeOffset ObservedAtUtc);
public sealed record UpsertAvlObservationRequest(Guid AgencyId, Guid UnitId, double Latitude, double Longitude, double SpeedKph, double? IncidentDistanceMeters, string Source, DateTimeOffset ObservedAtUtc);
public sealed record AvlAdvisoryDto(Guid Id, Guid AgencyId, Guid UnitId, Guid? IncidentId, string Severity, string Kind, string Summary, string SuggestedStatus, string State, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record ResolveAvlAdvisoryRequest(string Actor, bool ApplySuggestedStatus, string? IdempotencyKey);
public sealed record IncidentCorrectionDto(Guid Id, Guid AgencyId, Guid IncidentId, string Field, string OriginalValue, string CorrectedValue, string Reason, string Actor, DateTimeOffset CorrectedAtUtc, long IncidentVersion);
public sealed record CorrectIncidentFieldRequest(long ExpectedVersion, string Field, string CorrectedValue, string Reason, string Actor, string? IdempotencyKey);
