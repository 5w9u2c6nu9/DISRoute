using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed record OperationalPersistedState(
    IReadOnlyList<IncidentDto> Incidents,
    IReadOnlyList<UnitDto> Units,
    IReadOnlyList<StationDto> Stations,
    IReadOnlyDictionary<Guid, IReadOnlyList<IncidentActivityDto>> Activities,
    IReadOnlyList<ModuleStateDto> Modules,
    IReadOnlyList<RoadRestrictionDto> RoadRestrictions,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<WorkforceShiftDto> Workforce,
    IReadOnlyList<DocumentDto> Documents,
    IReadOnlyList<RmsRecordDto> RmsRecords,
    IReadOnlyList<ReplayEventDto> Replay,
    int IncidentSequence,
    int RmsSequence,
    IReadOnlyList<IncidentHistoryEntryDto>? IncidentHistory = null,
    IReadOnlyList<UnitHistoryEntryDto>? UnitHistory = null,
    IReadOnlyList<RmsIncidentReportDto>? RmsIncidentReports = null,
    IReadOnlyList<RmsReportVersionDto>? RmsReportVersions = null,
    IReadOnlyList<RmsAttachmentReferenceDto>? RmsAttachments = null,
    IReadOnlyList<NerisSubmissionDto>? NerisSubmissions = null,
    IReadOnlyList<NerisSubmissionAttemptDto>? NerisSubmissionAttempts = null);

public sealed record FeaturePersistedState(
    IReadOnlyList<CallRecordDto> Calls,
    IReadOnlyList<DispatchActionDto> DispatchActions,
    IReadOnlyList<DispatchDeliveryDto> DispatchDeliveries,
    IReadOnlyList<FireWatchDto> FireWatches,
    IReadOnlyDictionary<Guid, IReadOnlyList<FireWatchCheckDto>> FireWatchChecks,
    IReadOnlyList<RelocationDto> Relocations,
    IReadOnlyList<PremiseDto> Premises,
    IReadOnlyList<PreplanDto> Preplans,
    IReadOnlyList<CrgAssetDto> CrgAssets,
    IReadOnlyList<HydrantDto> Hydrants,
    IReadOnlyList<MapObjectDto> MapObjects,
    IReadOnlyList<VesselObservationDto> Vessels,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<BulletinDto> Bulletins,
    IReadOnlyList<StationDeliveryDto> StationDeliveries,
    IReadOnlyList<RunSheetDto> RunSheets,
    IReadOnlyList<ClientHeartbeatDto> Heartbeats,
    IReadOnlyList<ProviderConfigDto> Providers,
    IReadOnlyList<CameraFeedDto> Cameras,
    IReadOnlyList<RadioStreamDto> RadioStreams,
    IReadOnlyList<LeaveRequestDto> LeaveRequests,
    IReadOnlyList<ShiftTradeDto> ShiftTrades,
    IReadOnlyDictionary<Guid, IReadOnlyList<DocumentVersionDto>> DocumentVersions,
    IReadOnlyList<DocumentAcknowledgementDto> DocumentAcknowledgements,
    IReadOnlyList<FormTemplateDto> FormTemplates,
    IReadOnlyList<FormInstanceDto> Forms,
    IReadOnlyList<InspectionDto> Inspections,
    IReadOnlyList<PermitDto> Permits,
    IReadOnlyList<MaintenanceRecordDto> Maintenance,
    IReadOnlyList<NerisReportDto> NerisReports,
    IReadOnlyList<DeviceIdentityDto> Devices,
    IReadOnlyList<UserSecurityDto> Users,
    IReadOnlyList<AuditRecordDto> Audit,
    IReadOnlyList<BackupStatusDto> Backups,
    IReadOnlyList<ReplicationStatusDto> Replication,
    IReadOnlyList<ConfigVersionDto> ConfigVersions,
    IReadOnlyList<IncidentRelationshipDto> IncidentRelationships,
    IReadOnlyList<OperationalGroupDto> OperationalGroups,
    IReadOnlyList<CommandAssignmentDto> Commands,
    IReadOnlyList<MutualAidRequestDto> MutualAid,
    IReadOnlyList<MaydayEventDto> Maydays,
    IReadOnlyList<ResponsePlanDto> ResponsePlans,
    IReadOnlyList<WorkspaceLayoutDto> Workspaces,
    IReadOnlyList<ThemePackageDto> Themes,
    IReadOnlyList<CollaborativeNarrativeDto> CollaborativeNarratives,
    IReadOnlyList<AdditionalAlarmDto> AdditionalAlarms,
    IReadOnlyList<AvlObservationDto> AvlObservations,
    IReadOnlyList<AvlAdvisoryDto> AvlAdvisories,
    IReadOnlyList<IncidentCorrectionDto> Corrections,
    IReadOnlyList<RotationDto> Rotations,
    IReadOnlyList<OvertimeOfferDto> OvertimeOffers,
    IReadOnlyList<TimesheetEntryDto> Timesheets,
    IReadOnlyList<ApprovalWorkflowDto> ApprovalWorkflows,
    IReadOnlyList<ApprovalStepDto> ApprovalSteps,
    IReadOnlyList<ViolationDto> Violations,
    IReadOnlyList<HazmatRecordDto> Hazmat,
    IReadOnlyList<InvestigationDto> Investigations,
    IReadOnlyList<PersonnelQualificationDto> Qualifications,
    IReadOnlyList<string> WorkforceChangeHistory,
    IReadOnlyList<MfaCredentialState> MfaCredentials,
    IReadOnlyList<AuditIntegrityRecordDto> AuditIntegrity,
    IReadOnlyList<OperationalMessageDto> OperationalMessages,
    IReadOnlyList<MessageReceiptDto> MessageReceipts,
    int PermitSequence,
    IReadOnlyList<CollaborativeNarrativeOperationDto>? CollaborativeNarrativeOperations = null,
    IReadOnlyList<DispatchActionHistoryEntryDto>? DispatchActionHistory = null,
    IReadOnlyList<DispatchDeliveryHistoryEntryDto>? DispatchDeliveryHistory = null,
    IReadOnlyList<StationDeliveryHistoryEntryDto>? StationDeliveryHistory = null,
    IReadOnlyList<AlertHistoryEntryDto>? AlertHistory = null,
    IReadOnlyList<VesselObservationDto>? VesselObservationHistory = null,
    IReadOnlyList<StaffingRuleDto>? StaffingRules = null,
    IReadOnlyList<WorkforceAssignmentDto>? WorkforceAssignments = null,
    IReadOnlyList<WorkforceRosterSnapshotDto>? WorkforceRosterSnapshots = null,
    IReadOnlyList<WorkforceScheduleChangeDto>? WorkforceScheduleChanges = null,
    IReadOnlyList<DmsDocumentRevisionDto>? DmsDocumentRevisions = null,
    IReadOnlyList<DmsDistributionDto>? DmsDistributions = null,
    IReadOnlyList<DmsReadReceiptDto>? DmsReadReceipts = null,
    IReadOnlyList<DmsDocumentLinkDto>? DmsDocumentLinks = null,
    IReadOnlyList<DmsAssetReferenceDto>? DmsAssetReferences = null,
    IReadOnlyList<FormSignatureDto>? FormSignatures = null,
    IReadOnlyList<ChatChannelDto>? ChatChannels = null,
    IReadOnlyList<ChatMessageDto>? ChatMessages = null,
    IReadOnlyList<ChatReadStateDto>? ChatReadStates = null,
    IReadOnlyList<IntegrationEventDto>? IntegrationEvents = null,
    IReadOnlyList<IntegrationOperationDto>? IntegrationOperations = null,
    IReadOnlyList<IntegrationAttemptDto>? IntegrationAttempts = null,
    IReadOnlyList<RecordingReferenceDto>? RecordingReferences = null);

public sealed record DISRoutePersistedState(
    int SchemaVersion,
    DateTimeOffset SavedAtUtc,
    OperationalPersistedState Operational,
    FeaturePersistedState Features,
    IReadOnlyList<IdempotencyReceiptState>? IdempotencyReceipts = null);

public sealed partial class OperationalStore
{
    public OperationalPersistedState ExportState()
    {
        lock (_gate)
        {
            return new OperationalPersistedState(
                _incidents.Values.ToArray(), _units.Values.ToArray(), _stations.Values.ToArray(),
                _activities.ToDictionary(x => x.Key, x => (IReadOnlyList<IncidentActivityDto>)x.Value.ToArray()),
                _modules.Values.ToArray(), _roadRestrictions.Values.ToArray(), _messages.Values.ToArray(),
                _workforce.Values.ToArray(), _documents.Values.ToArray(), _rms.Values.ToArray(), _replay.ToArray(),
                _incidentSequence, _rmsSequence, _incidentHistory.ToArray(), _unitHistory.ToArray(),
                _rmsIncidentReports.Values.ToArray(), _rmsReportVersions.Values.SelectMany(x => x).ToArray(), _rmsAttachments.Values.SelectMany(x => x).ToArray(),
                _nerisSubmissions.Values.ToArray(), _nerisSubmissionAttempts.Values.SelectMany(x => x).ToArray());
        }
    }

    public void ImportState(OperationalPersistedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _incidents.Clear(); foreach (var x in state.Incidents) _incidents[x.Id] = x;
            _units.Clear(); foreach (var x in state.Units) _units[x.Id] = x;
            _stations.Clear(); foreach (var x in state.Stations) _stations[x.Id] = x;
            _activities.Clear(); foreach (var x in state.Activities) _activities[x.Key] = x.Value.ToList();
            _modules.Clear(); foreach (var x in state.Modules) _modules[x.Key] = x;
            _roadRestrictions.Clear(); foreach (var x in state.RoadRestrictions) _roadRestrictions[x.Id] = x;
            _messages.Clear(); foreach (var x in state.Messages) _messages[x.Id] = x;
            _workforce.Clear(); foreach (var x in state.Workforce) _workforce[x.Id] = x;
            _documents.Clear(); foreach (var x in state.Documents) _documents[x.Id] = x;
            _rms.Clear(); foreach (var x in state.RmsRecords) _rms[x.Id] = x;
            ImportRmsReportingState(state.RmsIncidentReports, state.RmsReportVersions, state.RmsAttachments);
            ImportNerisSubmissionState(state.NerisSubmissions, state.NerisSubmissionAttempts);
            _replay.Clear(); _replay.AddRange(state.Replay);
            _incidentHistory.Clear();
            if (state.IncidentHistory is { Count: > 0 }) _incidentHistory.AddRange(state.IncidentHistory);
            else foreach (var item in state.Incidents) _incidentHistory.Add(new IncidentHistoryEntryDto(Guid.NewGuid(), item.Id, item.Version, "ImportedCurrentState", "state-import", item, item.UpdatedAtUtc));
            _unitHistory.Clear();
            if (state.UnitHistory is { Count: > 0 }) _unitHistory.AddRange(state.UnitHistory);
            else foreach (var item in state.Units) _unitHistory.Add(new UnitHistoryEntryDto(Guid.NewGuid(), item.Id, "ImportedCurrentState", "state-import", item, item.UpdatedAtUtc));
            _incidentSequence = Math.Max(1, state.IncidentSequence);
            _rmsSequence = Math.Max(1, state.RmsSequence);
        }
    }
}

public sealed partial class FeatureStore
{
    public FeaturePersistedState ExportState()
    {
        lock (_gate)
        {
            return new FeaturePersistedState(
                _calls.Values.ToArray(), _dispatchActions.Values.ToArray(), _dispatchDeliveries.Values.ToArray(),
                _fireWatches.Values.ToArray(), _fireWatchChecks.ToDictionary(x => x.Key, x => (IReadOnlyList<FireWatchCheckDto>)x.Value.ToArray()),
                _relocations.Values.ToArray(), _premises.Values.ToArray(), _preplans.Values.ToArray(), _crgs.Values.ToArray(),
                _hydrants.Values.ToArray(), _mapObjects.Values.ToArray(), _vessels.Values.ToArray(), _alerts.Values.ToArray(),
                _bulletins.Values.ToArray(), _stationDeliveries.Values.ToArray(), _runSheets.Values.ToArray(), _heartbeats.Values.ToArray(),
                _providers.Values.ToArray(), _cameras.Values.ToArray(), _radioStreams.Values.ToArray(), _leaveRequests.Values.ToArray(),
                _shiftTrades.Values.ToArray(), _documentVersions.ToDictionary(x => x.Key, x => (IReadOnlyList<DocumentVersionDto>)x.Value.ToArray()),
                _documentAcks.Values.ToArray(), _formTemplates.Values.ToArray(), _forms.Values.ToArray(), _inspections.Values.ToArray(),
                _permits.Values.ToArray(), _maintenance.Values.ToArray(), _neris.Values.ToArray(), _devices.Values.ToArray(),
                _users.Values.ToArray(), _audit.ToArray(), _backups.ToArray(), _replication.ToArray(), _configs.Values.ToArray(),
                _incidentRelationships.Values.ToArray(), _operationalGroups.Values.ToArray(), _commands.Values.ToArray(),
                _mutualAid.Values.ToArray(), _maydays.Values.ToArray(), _responsePlans.Values.ToArray(), _workspaces.Values.ToArray(),
                _themes.Values.ToArray(), _collaborativeNarratives.Values.ToArray(), _additionalAlarms.Values.ToArray(),
                _avlObservations.Values.ToArray(), _avlAdvisories.Values.ToArray(), _corrections.Values.ToArray(),
                _rotations.Values.ToArray(), _overtimeOffers.Values.ToArray(), _timesheets.Values.ToArray(),
                _approvalWorkflows.Values.ToArray(), _approvalSteps.Values.ToArray(), _violations.Values.ToArray(),
                _hazmat.Values.ToArray(), _investigations.Values.ToArray(), _qualifications.Values.ToArray(),
                _workforceChangeHistory.ToArray(), _mfaCredentials.Values.ToArray(), _auditIntegrity.ToArray(), _operationalMessages.Values.ToArray(), _messageReceipts.Values.ToArray(), _permitSequence,
                _collaborativeNarrativeOperations.Values.SelectMany(x => x).OrderBy(x => x.ResultVersion).ToArray(),
                _dispatchActionHistory.ToArray(), _dispatchDeliveryHistory.ToArray(), _stationDeliveryHistory.ToArray(), _alertHistory.ToArray(), _vesselObservationHistory.ToArray(),
                _staffingRules.Values.ToArray(), _workforceAssignments.Values.ToArray(), _workforceRosterSnapshots.Values.ToArray(), _workforceScheduleChanges.ToArray(),
                _dmsRevisions.Values.ToArray(), _dmsDistributions.Values.ToArray(), _dmsReadReceipts.Values.ToArray(), _dmsDocumentLinks.Values.ToArray(), _dmsAssetReferences.Values.ToArray(), _formSignatures.Values.ToArray(),
                _chatChannels.Values.ToArray(), _chatMessages.Values.ToArray(), _chatReadStates.Values.ToArray(),
                _integrationEvents.Values.ToArray(), _integrationOperations.Values.ToArray(), _integrationAttempts.ToArray(), _recordingReferences.Values.ToArray());
        }
    }

    public void ImportState(FeaturePersistedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            Fill(_calls, state.Calls, x => x.Id); Fill(_dispatchActions, state.DispatchActions, x => x.Id);
            Fill(_dispatchDeliveries, state.DispatchDeliveries, x => x.Id); Fill(_fireWatches, state.FireWatches, x => x.Id);
            _fireWatchChecks.Clear(); foreach (var x in state.FireWatchChecks) _fireWatchChecks[x.Key] = x.Value.ToList();
            Fill(_relocations, state.Relocations, x => x.Id); Fill(_premises, state.Premises, x => x.Id);
            Fill(_preplans, state.Preplans, x => x.Id); Fill(_crgs, state.CrgAssets, x => x.Id); Fill(_hydrants, state.Hydrants, x => x.Id);
            Fill(_mapObjects, state.MapObjects, x => x.Id);
            _vessels.Clear(); foreach (var x in state.Vessels) _vessels[x.Mmsi] = x;
            _vesselObservationHistory.Clear(); _vesselObservationHistory.AddRange(state.VesselObservationHistory ?? state.Vessels);
            Fill(_alerts, state.Alerts, x => x.Id); Fill(_bulletins, state.Bulletins, x => x.Id);
            Fill(_stationDeliveries, state.StationDeliveries, x => x.Id); Fill(_runSheets, state.RunSheets, x => x.Id);
            _heartbeats.Clear(); foreach (var x in state.Heartbeats) _heartbeats[x.DeviceId] = x;
            _providers.Clear(); foreach (var x in state.Providers) _providers[x.Key] = x;
            Fill(_cameras, state.Cameras, x => x.Id); Fill(_radioStreams, state.RadioStreams, x => x.Id);
            Fill(_leaveRequests, state.LeaveRequests, x => x.Id); Fill(_shiftTrades, state.ShiftTrades, x => x.Id);
            _documentVersions.Clear(); foreach (var x in state.DocumentVersions) _documentVersions[x.Key] = x.Value.ToList();
            Fill(_documentAcks, state.DocumentAcknowledgements, x => x.Id); Fill(_formTemplates, state.FormTemplates, x => x.Id);
            Fill(_forms, state.Forms, x => x.Id); Fill(_inspections, state.Inspections, x => x.Id); Fill(_permits, state.Permits, x => x.Id);
            Fill(_maintenance, state.Maintenance, x => x.Id); Fill(_neris, state.NerisReports, x => x.Id);
            _devices.Clear(); foreach (var x in state.Devices) _devices[x.DeviceId] = x;
            _users.Clear(); foreach (var x in state.Users) _users[x.UserId] = x;
            _audit.Clear(); _audit.AddRange(state.Audit); _backups.Clear(); _backups.AddRange(state.Backups); _replication.Clear(); _replication.AddRange(state.Replication);
            Fill(_configs, state.ConfigVersions, x => x.Id); Fill(_incidentRelationships, state.IncidentRelationships, x => x.Id);
            Fill(_operationalGroups, state.OperationalGroups, x => x.Id); Fill(_commands, state.Commands, x => x.Id);
            Fill(_mutualAid, state.MutualAid, x => x.Id); Fill(_maydays, state.Maydays, x => x.Id); Fill(_responsePlans, state.ResponsePlans, x => x.Id);
            Fill(_workspaces, state.Workspaces, x => x.Id); Fill(_themes, state.Themes, x => x.Id); Fill(_collaborativeNarratives, state.CollaborativeNarratives ?? [], x => x.IncidentId);
            Fill(_additionalAlarms, state.AdditionalAlarms ?? [], x => x.Id); Fill(_avlObservations, state.AvlObservations ?? [], x => x.Id);
            Fill(_avlAdvisories, state.AvlAdvisories ?? [], x => x.Id); Fill(_corrections, state.Corrections ?? [], x => x.Id);
            Fill(_rotations, state.Rotations ?? [], x => x.Id); Fill(_overtimeOffers, state.OvertimeOffers ?? [], x => x.Id);
            Fill(_timesheets, state.Timesheets ?? [], x => x.Id); Fill(_approvalWorkflows, state.ApprovalWorkflows ?? [], x => x.Id);
            Fill(_approvalSteps, state.ApprovalSteps ?? [], x => x.Id); Fill(_violations, state.Violations ?? [], x => x.Id);
            Fill(_hazmat, state.Hazmat ?? [], x => x.Id); Fill(_investigations, state.Investigations ?? [], x => x.Id);
            Fill(_qualifications, state.Qualifications ?? [], x => x.Id);
            Fill(_staffingRules, state.StaffingRules ?? [], x => x.Id);
            Fill(_workforceAssignments, state.WorkforceAssignments ?? [], x => x.Id);
            Fill(_workforceRosterSnapshots, state.WorkforceRosterSnapshots ?? [], x => x.Id);
            _workforceScheduleChanges.Clear(); _workforceScheduleChanges.AddRange(state.WorkforceScheduleChanges ?? []);
            Fill(_dmsRevisions, state.DmsDocumentRevisions ?? [], x => x.Id);
            Fill(_dmsDistributions, state.DmsDistributions ?? [], x => x.Id);
            Fill(_dmsReadReceipts, state.DmsReadReceipts ?? [], x => x.Id);
            Fill(_dmsDocumentLinks, state.DmsDocumentLinks ?? [], x => x.Id);
            Fill(_dmsAssetReferences, state.DmsAssetReferences ?? [], x => x.Id);
            Fill(_formSignatures, state.FormSignatures ?? [], x => x.Id);
            Fill(_chatChannels, state.ChatChannels ?? [], x => x.Id);
            Fill(_chatMessages, state.ChatMessages ?? [], x => x.Id);
            _chatReadStates.Clear(); foreach (var x in state.ChatReadStates ?? []) _chatReadStates[$"{x.ChannelId:D}|{x.UserId.Trim().ToLowerInvariant()}"] = x;
            ImportIntegrationState(state.IntegrationEvents, state.IntegrationOperations, state.IntegrationAttempts, state.RecordingReferences);
            if (_dmsRevisions.Count == 0)
            {
                foreach (var pair in _documentVersions)
                    foreach (var legacy in pair.Value)
                    {
                        var stateName = NormalizeRevisionState(legacy.Status);
                        _dmsRevisions[legacy.Id] = new DmsDocumentRevisionDto(legacy.Id, legacy.DocumentId, legacy.Version, legacy.Content, stateName, legacy.Author, Sha256(legacy.Content), legacy.CreatedAtUtc, null, null);
                    }
            }
            foreach (var form in _forms.Values.Where(x => x.Status == "Signed" && !_formSignatures.Values.Any(s => s.FormId == x.Id)))
            {
                if (_formTemplates.TryGetValue(form.TemplateId, out var template))
                    _formSignatures[form.Id] = new FormSignatureDto(form.Id, form.Id, form.TemplateId, template.Version, form.SignedBy ?? "legacy-unknown", "Legacy imported signature", Sha256(form.ValuesJson), "legacy-import", form.UpdatedAtUtc);
            }
            _workforceChangeHistory.Clear(); _workforceChangeHistory.AddRange(state.WorkforceChangeHistory ?? []);
            _mfaCredentials.Clear(); foreach (var x in state.MfaCredentials ?? []) _mfaCredentials[x.UserId] = x;
            _auditIntegrity.Clear(); _auditIntegrity.AddRange(state.AuditIntegrity ?? []);
            Fill(_operationalMessages, state.OperationalMessages ?? [], x => x.Id);
            Fill(_messageReceipts, state.MessageReceipts ?? [], x => x.Id);
            _collaborativeNarrativeOperations.Clear();
            foreach (var operation in state.CollaborativeNarrativeOperations ?? [])
            {
                if (!_collaborativeNarrativeOperations.TryGetValue(operation.IncidentId, out var operations))
                    _collaborativeNarrativeOperations[operation.IncidentId] = operations = [];
                operations.Add(operation);
            }
            foreach (var operations in _collaborativeNarrativeOperations.Values) operations.Sort((a, b) => a.ResultVersion.CompareTo(b.ResultVersion));
            _dispatchActionHistory.Clear(); _dispatchActionHistory.AddRange(state.DispatchActionHistory ?? []);
            if (_dispatchActionHistory.Count == 0)
                foreach (var item in state.DispatchActions)
                    _dispatchActionHistory.Add(new DispatchActionHistoryEntryDto(Guid.NewGuid(), item.Id, item.IncidentId, "ImportedCurrentState", "state-import", item, item.CommittedAtUtc));
            _dispatchDeliveryHistory.Clear(); _dispatchDeliveryHistory.AddRange(state.DispatchDeliveryHistory ?? []);
            if (_dispatchDeliveryHistory.Count == 0)
                foreach (var item in state.DispatchDeliveries)
                    if (_dispatchActions.TryGetValue(item.DispatchActionId, out var action))
                        _dispatchDeliveryHistory.Add(new DispatchDeliveryHistoryEntryDto(Guid.NewGuid(), item.Id, action.IncidentId, "ImportedCurrentState", "state-import", item, item.UpdatedAtUtc));
            _stationDeliveryHistory.Clear(); _stationDeliveryHistory.AddRange(state.StationDeliveryHistory ?? []);
            if (_stationDeliveryHistory.Count == 0)
                foreach (var item in state.StationDeliveries)
                    _stationDeliveryHistory.Add(new StationDeliveryHistoryEntryDto(Guid.NewGuid(), item.Id, item.IncidentId, "ImportedCurrentState", "state-import", item, item.UpdatedAtUtc));
            _alertHistory.Clear(); _alertHistory.AddRange(state.AlertHistory ?? []);
            if (_alertHistory.Count == 0)
                foreach (var item in state.Alerts)
                    _alertHistory.Add(new AlertHistoryEntryDto(Guid.NewGuid(), item.Id, item.IncidentId, "ImportedCurrentState", "state-import", item, item.UpdatedAtUtc));
            if (_auditIntegrity.Count == 0 && _audit.Count > 0)
                foreach (var record in _audit.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)) SealAuditRecord(record);
            _permitSequence = Math.Max(1, state.PermitSequence);
        }
    }

    private static void Fill<TKey, TValue>(Dictionary<TKey, TValue> target, IEnumerable<TValue> source, Func<TValue, TKey> keySelector) where TKey : notnull
    {
        target.Clear();
        foreach (var item in source) target[keySelector(item)] = item;
    }
}
