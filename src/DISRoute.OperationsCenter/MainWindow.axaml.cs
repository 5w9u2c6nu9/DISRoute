using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;

namespace DISRoute.OperationsCenter;

public sealed partial class MainWindow : Window, IAsyncDisposable
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private readonly OperationalApiClient _core;
    private readonly FeatureApiClient _features;
    private Guid _workforceAgencyId;
    private WorkforceRosterViewDto? _workforceRoster;
    private DmsDocumentRevisionDto[] _dmsRevisions = [];
    private DmsDistributionDto[] _dmsDistributions = [];
    private DmsReadReceiptDto[] _dmsReceipts = [];
    private DmsAssetReferenceDto[] _dmsAssets = [];
    private DmsDocumentLinkDto[] _dmsLinks = [];
    private ApprovalStepDto[] _approvalSteps = [];
    private IntegrationDashboardDto? _integrationDashboard;
    private CameraDiscoveryCandidateDto[] _integrationCandidates = [];
    private static readonly JsonSerializerOptions NerisPrettyJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public MainWindow()
    {
        InitializeComponent();
        _core = new OperationalApiClient(SharedHttp);
        _features = new FeatureApiClient(SharedHttp);
        InitializeChatRealtime();
        WorkforceDateText.Text = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        FormTemplateSchemaText.Text = "{\"fields\":[]}";
        FormValuesText.Text = "{}";
        Opened += async (_, _) => await RefreshAsync();
    }

    private static HttpClient CreateHttpClient() => ClientHttpFactory.Create(TimeSpan.FromSeconds(15));

    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "Refreshing…";
            var selectedRmsIncidentId = (RmsIncidentItems.SelectedItem as RmsIncidentRow)?.Id;
            var selectedRmsReportId = (RmsReportItems.SelectedItem as RmsReportRow)?.Id;
            var selectedNerisRmsReportId = (NerisRmsReportItems.SelectedItem as NerisRmsReportRow)?.Id;
            var selectedNerisSubmissionId = (NerisSubmissionItems.SelectedItem as NerisSubmissionRow)?.Id;
            var selectedWorkforceAssignmentId = (WorkforceAssignmentItems.SelectedItem as WorkforceAssignmentRow)?.Id;
            var selectedDmsDocumentId = (DmsDocumentItems.SelectedItem as DmsDocumentRow)?.Document.Id;
            var selectedDmsRevisionId = (DmsRevisionItems.SelectedItem as DmsRevisionRow)?.Revision.Id;
            var selectedTemplateId = (FormTemplateItems.SelectedItem as FormTemplateRow)?.Template.Id;
            var selectedFormId = (FormItems.SelectedItem as FormRow)?.Form.Id;
            var selectedWorkflowId = (WorkflowItems.SelectedItem as WorkflowRow)?.Workflow.Id;
            var selectedChatChannelId = (ChatChannelItems.SelectedItem as ChatChannelRow)?.Id;
            var selectedIntegrationOperationId = (IntegrationOperationItems.SelectedItem as IntegrationOperationRow)?.Id;
            var stationsTask = _core.GetStationsAsync();
            var unitsTask = _core.GetUnitsAsync();
            var incidentsTask = _core.GetIncidentsAsync(openOnly: true);
            var rmsIncidentsTask = _core.GetIncidentsAsync(openOnly: false);
            var rmsReportsTask = _core.GetRmsIncidentReportsAsync();
            var nerisSubmissionsTask = _core.GetNerisSubmissionsAsync();
            var alertsTask = _features.GetAlertsAsync();
            var maydaysTask = _features.GetMaydaysAsync();
            var clientsTask = _features.GetClientsAsync();
            var providersTask = _features.GetProviderHealthAsync();
            var integrationTask = _features.GetIntegrationDashboardAsync();
            var leaveTask = _features.GetLeaveRequestsAsync();
            var tradesTask = _features.GetShiftTradesAsync();
            var rotationsTask = _features.GetRotationsAsync();
            var overtimeTask = _features.GetOvertimeOffersAsync();
            var timesheetsTask = _features.GetTimesheetsAsync();
            var dmsDocumentsTask = _features.GetDmsDocumentsAsync();
            var templatesTask = _features.GetFormTemplatesAsync();
            var formsTask = _features.GetFormsAsync();
            var workflowsTask = _features.GetApprovalWorkflowsAsync();
            var inspectionsTask = _features.GetInspectionsAsync();
            var permitsTask = _features.GetPermitsAsync();
            var nerisTask = _features.GetNerisReportsAsync();
            var violationsTask = _features.GetViolationsAsync();
            var hazmatTask = _features.GetHazmatAsync();
            var investigationsTask = _features.GetInvestigationsAsync();
            var qualificationsTask = _features.GetQualificationsAsync();
            var devicesTask = _features.GetDevicesAsync();
            var usersTask = _features.GetUsersAsync();
            var auditTask = _features.GetAuditAsync();
            var auditIntegrityTask = _features.VerifyAuditAsync();
            var overviewTask = _features.GetOperationsOverviewAsync();
            var haTask = _features.GetHaStatusAsync();
            var haEventsTask = _features.GetHaEventsAsync(20);
            await Task.WhenAll(stationsTask, unitsTask, incidentsTask, rmsIncidentsTask, rmsReportsTask, nerisSubmissionsTask, alertsTask, maydaysTask, clientsTask, providersTask, integrationTask, leaveTask, tradesTask, rotationsTask, overtimeTask, timesheetsTask, dmsDocumentsTask, templatesTask, formsTask, workflowsTask, inspectionsTask, permitsTask, nerisTask, violationsTask, hazmatTask, investigationsTask, qualificationsTask, devicesTask, usersTask, auditTask, auditIntegrityTask, overviewTask, haTask, haEventsTask);

            var stations = stationsTask.Result;
            var units = unitsTask.Result;
            _workforceAgencyId = stations.Count > 0 ? stations[0].AgencyId : Guid.Empty;
            var stationRows = stations.Where(x => _workforceAgencyId == Guid.Empty || x.AgencyId == _workforceAgencyId).OrderBy(x => x.Code).Select(x => new WorkforceStationRow(x.Id, x.Code, x.Name)).ToArray();
            WorkforceStationItems.ItemsSource = stationRows;
            WorkforceRuleStationItems.ItemsSource = stationRows;
            if (WorkforceStationItems.SelectedItem is null && stationRows.Length > 0) WorkforceStationItems.SelectedIndex = 0;
            if (WorkforceRuleStationItems.SelectedItem is null && stationRows.Length > 0) WorkforceRuleStationItems.SelectedIndex = 0;
            var unitRows = new[] { new WorkforceUnitRow(null, "— No apparatus —") }.Concat(units.Where(x => _workforceAgencyId == Guid.Empty || x.AgencyId == _workforceAgencyId).OrderBy(x => x.CallSign).Select(x => new WorkforceUnitRow(x.Id, x.CallSign))).ToArray();
            WorkforceUnitItems.ItemsSource = unitRows;
            if (WorkforceUnitItems.SelectedItem is null) WorkforceUnitItems.SelectedIndex = 0;
            if (_workforceAgencyId != Guid.Empty)
            {
                var workforceDate = GetWorkforceDate();
                var rosterTask = _features.GetWorkforceRosterAsync(_workforceAgencyId, workforceDate);
                var rulesTask = _features.GetStaffingRulesAsync(_workforceAgencyId);
                var snapshotsTask = _features.GetWorkforceRosterSnapshotsAsync(_workforceAgencyId, workforceDate);
                await Task.WhenAll(rosterTask, rulesTask, snapshotsTask);
                _workforceRoster = rosterTask.Result;
                var assignmentRows = _workforceRoster.Assignments.Select(x => new WorkforceAssignmentRow(x.Id, $"{StationCode(stations, x.StationId)}  {UnitCallSign(units, x.UnitId),-8} {x.ShiftName}  {x.Position}  {x.EmployeeName}  {x.Qualification}  [{x.State}/{x.AssignmentType}]")).ToArray();
                WorkforceAssignmentItems.ItemsSource = assignmentRows;
                if (selectedWorkforceAssignmentId is Guid selectedAssignment) WorkforceAssignmentItems.SelectedItem = assignmentRows.FirstOrDefault(x => x.Id == selectedAssignment);
                WorkforceStaffingText.Text = $"Staffing {_workforceRoster.Staffing.FilledPositions}/{_workforceRoster.Staffing.RequiredPositions} • vacancies={_workforceRoster.Staffing.Vacancies} • {(_workforceRoster.Staffing.MeetsMinimums ? "MEETS MINIMUMS" : "DEFICIENT")}\n" + string.Join(Environment.NewLine, _workforceRoster.Staffing.Warnings);
                WorkforceStaffingRuleResultItems.ItemsSource = _workforceRoster.Staffing.Rules.Select(x => $"{x.Scope}  {x.ShiftName} {x.Position}  {x.FilledCount}/{x.RequiredCount}  qual={x.RequiredQualification}  vacancies={x.Vacancies}");
                WorkforceChangeItems.ItemsSource = _workforceRoster.Changes.OrderByDescending(x => x.OccurredAtUtc).Select(x => $"{x.OccurredAtUtc.LocalDateTime:G}  {x.Kind}  {x.Summary}  — {x.Actor}");
                WorkforceRuleItems.ItemsSource = rulesTask.Result.Select(x => $"{(x.StationId is Guid sid ? StationCode(stations, sid) : "AGENCY")}  {x.ShiftName}  {x.Position}  min={x.MinimumCount}  qual={x.RequiredQualification}  [{(x.Enabled ? "Enabled" : "Disabled")}]");
                WorkforceSnapshotItems.ItemsSource = snapshotsTask.Result.OrderByDescending(x => x.Revision).Select(x => $"rev {x.Revision}  {x.PostedAtUtc.LocalDateTime:G}  {x.PostedBy}  [{x.State}]  {x.ContentSha256[..Math.Min(12, x.ContentSha256.Length)]}");
            }

            await RefreshChatAsync(_workforceAgencyId, selectedChatChannelId);

            IncidentItems.ItemsSource = incidentsTask.Result.Select(x => $"{x.Number}  {x.Nature}  {x.Address}  [{x.Status}]");
            var rmsIncidentRows = rmsIncidentsTask.Result.OrderByDescending(x => x.ReceivedAtUtc).Select(x => new RmsIncidentRow(x.Id, x.AgencyId, $"{x.Number}  {x.Nature}  [{x.Status}]")).ToArray();
            var rmsReportRows = rmsReportsTask.Result.OrderByDescending(x => x.UpdatedAtUtc).Select(x => new RmsReportRow(x.Id, x.IncidentId, x.Version, $"{x.Number}  {x.IncidentNumber}  v{x.Version}  [{x.Status}]")).ToArray();
            RmsIncidentItems.ItemsSource = rmsIncidentRows;
            RmsReportItems.ItemsSource = rmsReportRows;
            if (selectedRmsIncidentId is Guid selectedIncident) RmsIncidentItems.SelectedItem = rmsIncidentRows.FirstOrDefault(x => x.Id == selectedIncident);
            if (selectedRmsReportId is Guid selectedReport) RmsReportItems.SelectedItem = rmsReportRows.FirstOrDefault(x => x.Id == selectedReport);
            var nerisRmsRows = rmsReportsTask.Result.Where(x => x.Status.Equals("Finalized", StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.UpdatedAtUtc).Select(x => new NerisRmsReportRow(x.Id, x.Version, $"{x.Number}  {x.IncidentNumber}  v{x.Version}")).ToArray();
            var nerisRows = nerisSubmissionsTask.Result.OrderByDescending(x => x.UpdatedAtUtc).Select(x => new NerisSubmissionRow(x.Id, x.RmsReportId, x.RmsReportVersion, x.State, $"RMS v{x.RmsReportVersion}  {x.ProfileVersion}  [{x.State}]  attempts={x.AttemptCount}")).ToArray();
            NerisRmsReportItems.ItemsSource = nerisRmsRows;
            NerisSubmissionItems.ItemsSource = nerisRows;
            if (selectedNerisRmsReportId is Guid selectedNerisReport) NerisRmsReportItems.SelectedItem = nerisRmsRows.FirstOrDefault(x => x.Id == selectedNerisReport);
            if (selectedNerisSubmissionId is Guid selectedSubmission) NerisSubmissionItems.SelectedItem = nerisRows.FirstOrDefault(x => x.Id == selectedSubmission);
            AlertItems.ItemsSource = alertsTask.Result.Select(x => $"{x.Severity}  {x.Type}  {x.Summary}  [{x.State}]");
            MaydayItems.ItemsSource = maydaysTask.Result.Where(x => x.State != "Resolved").Select(x => new MaydayRow(x.Id, $"{x.State.ToUpperInvariant()}  {x.Person}  {x.Location}  ch={x.Channel}  owner={x.AssignedTo ?? "UNASSIGNED"}  escalation={x.EscalationLevel}")).ToArray();
            ClientItems.ItemsSource = clientsTask.Result.Select(x => $"{x.DeviceId}  {x.ClientRole}  {x.User}  {x.State}  {x.LastSeenUtc.LocalDateTime:g}");
            ProviderItems.ItemsSource = providersTask.Result.Select(x => $"{x.Category}  {x.DisplayName}  {x.State}  {x.Detail}");
            _integrationDashboard = integrationTask.Result;
            IntegrationEventItems.ItemsSource = _integrationDashboard.RecentEvents.OrderByDescending(x => x.ReceivedAtUtc).Select(x => $"{x.ReceivedAtUtc.LocalDateTime:G}  {x.Category}/{x.EventType}  {x.ProviderKey}  {x.ResourceKey ?? x.NativeEventId}  [{x.State}]");
            var integrationOperationRows = _integrationDashboard.Operations.OrderByDescending(x => x.UpdatedAtUtc).Select(x => new IntegrationOperationRow(x.Id, $"{x.UpdatedAtUtc.LocalDateTime:G}  {x.Kind}  {x.ProviderKey}  [{x.State}]  attempts={x.AttemptCount}  ref={x.ProviderReference ?? "-"}")).ToArray();
            IntegrationOperationItems.ItemsSource = integrationOperationRows;
            if (selectedIntegrationOperationId is Guid selectedIntegrationOperation) IntegrationOperationItems.SelectedItem = integrationOperationRows.FirstOrDefault(x => x.Id == selectedIntegrationOperation);
            IntegrationRecordingItems.ItemsSource = _integrationDashboard.Recordings.OrderByDescending(x => x.LinkedAtUtc).Select(x => $"{x.StartUtc.LocalDateTime:G}  {x.MediaType}  {x.ProviderKey}:{x.ProviderReference}  {x.Description}");
            IntegrationCameraItems.ItemsSource = _integrationDashboard.Cameras.OrderBy(x => x.Name).Select(x => $"{x.Name}  {x.Uri}  [{x.State}]");
            IntegrationVesselItems.ItemsSource = _integrationDashboard.Vessels.OrderByDescending(x => x.ObservedAtUtc).Select(x => $"{x.Mmsi}  {x.Name}  {x.Latitude:0.00000},{x.Longitude:0.00000}  {x.SpeedKnots:0.0}kt  {x.ObservedAtUtc.LocalDateTime:G}");
            await LoadSelectedIntegrationOperationAsync();
            LeaveItems.ItemsSource = leaveTask.Result.Select(x => $"LEAVE  {x.EmployeeName}  {x.StartDate:yyyy-MM-dd}–{x.EndDate:yyyy-MM-dd}  {x.LeaveType}  [{x.Status}]");
            TradeItems.ItemsSource = tradesTask.Result.Select(x => $"TRADE  {x.OfferedBy} → {x.RequestedWith}  [{x.Status}]");
            RotationItems.ItemsSource = rotationsTask.Result.Select(x => $"ROTATION  {x.Name}  {x.Pattern}  [{(x.Enabled ? "Enabled" : "Disabled")}]");
            OvertimeItems.ItemsSource = overtimeTask.Result.Select(x => $"OT  {x.Date:yyyy-MM-dd} {x.ShiftName} {x.Position}  {x.AcceptedEmployees.Count}/{x.Slots}  [{x.State}]");
            TimesheetItems.ItemsSource = timesheetsTask.Result.Select(x => $"TIME  {x.EmployeeName}  {x.WorkDate:yyyy-MM-dd}  {x.Hours:0.##}h {x.TimeCategory}  [{x.Status}]");
            var dmsRows = dmsDocumentsTask.Result.Select(x => new DmsDocumentRow(x)).ToArray();
            DmsDocumentItems.ItemsSource = dmsRows;
            DmsDocumentItems.SelectedItem = selectedDmsDocumentId is Guid dmsId ? dmsRows.FirstOrDefault(x => x.Document.Id == dmsId) : dmsRows.FirstOrDefault();
            var templateRows = templatesTask.Result.OrderBy(x => x.Name).ThenByDescending(x => x.Version).Select(x => new FormTemplateRow(x)).ToArray();
            FormTemplateItems.ItemsSource = templateRows;
            FormTemplateItems.SelectedItem = selectedTemplateId is Guid templateId ? templateRows.FirstOrDefault(x => x.Template.Id == templateId) : templateRows.FirstOrDefault();
            var formRows = formsTask.Result.OrderByDescending(x => x.UpdatedAtUtc).Select(x => new FormRow(x)).ToArray();
            FormItems.ItemsSource = formRows;
            FormItems.SelectedItem = selectedFormId is Guid formId ? formRows.FirstOrDefault(x => x.Form.Id == formId) : formRows.FirstOrDefault();
            var workflowRows = workflowsTask.Result.OrderByDescending(x => x.UpdatedAtUtc).Select(x => new WorkflowRow(x)).ToArray();
            WorkflowItems.ItemsSource = workflowRows;
            WorkflowItems.SelectedItem = selectedWorkflowId is Guid workflowId ? workflowRows.FirstOrDefault(x => x.Workflow.Id == workflowId) : workflowRows.FirstOrDefault();
            InspectionItems.ItemsSource = inspectionsTask.Result.Select(x => $"INSPECTION  {x.Type}  {x.Inspector}  {x.InspectionDate:yyyy-MM-dd}  [{x.Status}]");
            PermitItems.ItemsSource = permitsTask.Result.Select(x => $"PERMIT  {x.Number}  {x.PermitType}  {x.Holder}  [{x.Status}]");
            NerisItems.ItemsSource = nerisTask.Result.Select(x => $"NERIS  {x.IncidentId:D}  {x.ValidationState}  [{x.Status}]");
            ViolationItems.ItemsSource = violationsTask.Result.Select(x => $"VIOLATION  {x.Code}  {x.Severity}  [{x.State}]  due={x.DueDate:yyyy-MM-dd}");
            HazmatItems.ItemsSource = hazmatTask.Result.Select(x => $"HAZMAT  {x.Material}  {x.UnNumber}  {x.Quantity}  [{x.State}]");
            InvestigationItems.ItemsSource = investigationsTask.Result.Select(x => $"INVESTIGATION  {x.Title}  lead={x.LeadInvestigator}  [{x.State}]");
            QualificationItems.ItemsSource = qualificationsTask.Result.Select(x => $"QUAL  {x.PersonName}  {x.Qualification}  [{x.State}]");
            DeviceItems.ItemsSource = devicesTask.Result.Select(x => $"DEVICE  {x.Name}  {x.Role}  {x.Platform}  [{x.State}]");
            UserItems.ItemsSource = usersTask.Result.Select(x => $"USER  {x.DisplayName}  {string.Join(", ", x.Roles)}  [{x.State}]");
            AuditItems.ItemsSource = auditTask.Result.Select(x => $"{x.OccurredAtUtc.LocalDateTime:G}  {x.Actor}  {x.Action}  {x.EntityType}:{x.EntityId}  {x.Summary}");
            var auditIntegrity = auditIntegrityTask.Result;
            AuditIntegrityText.Text = auditIntegrity.Valid ? $"Audit chain verified — {auditIntegrity.RecordsChecked} records" : $"AUDIT INTEGRITY FAILURE — {auditIntegrity.Error}";

            var overview = overviewTask.Result;
            var ha = haTask.Result;
            HealthItems.ItemsSource = new[]
            {
                $"Core: {overview.ActiveIncidents} active incidents / {overview.Units} units / {overview.ActiveAlerts} alerts / {overview.ConnectedClients} clients",
                $"HA: {ha.TopologyState}  active={ha.ActiveNode} site={ha.ActiveSite} epoch={ha.LeaseEpoch} split-brain-protected={ha.SplitBrainProtected}",
                $"This node: {ha.CurrentNodeId} / {ha.CurrentNodeRole}  geo-auto-promotion={ha.AutomaticGeographicPromotionEnabled}",
                $"Failover: {ha.PreferredFailoverOrder}",
                ha.Detail ?? string.Empty,
            }.Where(x => !string.IsNullOrWhiteSpace(x))
             .Concat(ha.Nodes.Select(x => $"Node {x.Site}/{x.NodeId}: {x.EffectiveRole} {x.State}, writer={x.AcceptingWrites}, heartbeat={x.HeartbeatAgeSeconds:0.0}s, epoch={x.LeaseEpoch}"))
             .Concat(ha.Replicas.Select(x => $"Replica {x.Site}/{x.Role}: {x.State}, lag {x.LagSeconds:0.0}s / {x.LagBytes} bytes, promotable={x.PromotionEligible}"))
             .Concat(ha.Backups.Select(x => $"Backup {x.Component}: {x.State}, last backup={x.LastSuccessfulBackupUtc?.LocalDateTime:g}, last restore test={x.LastVerifiedRestoreUtc?.LocalDateTime:g}"))
             .Concat(haEventsTask.Result.Take(8).Select(x => $"HA EVENT {x.OccurredAtUtc.LocalDateTime:G}  {x.NodeId}  {x.EventType}"));
            await LoadSelectedDmsDocumentAsync(selectedDmsRevisionId);
            await LoadSelectedFormAsync();
            await LoadSelectedWorkflowAsync();
            StatusText.Text = "Connected";
            LastRefreshText.Text = DateTime.Now.ToString("G", CultureInfo.CurrentCulture);
        }
        catch (Exception ex)
        {
            StatusText.Text = "DEGRADED — " + ex.Message;
        }
    }

    private async void DmsDocumentSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedDmsDocumentAsync();

    private void DmsRevisionSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateDmsRevisionDetail();

    private async Task LoadSelectedDmsDocumentAsync(Guid? preferredRevisionId = null)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow row)
        {
            _dmsRevisions = []; _dmsDistributions = []; _dmsReceipts = []; _dmsAssets = []; _dmsLinks = [];
            DmsRevisionItems.ItemsSource = null; DmsDistributionItems.ItemsSource = null; DmsReceiptItems.ItemsSource = null; DmsAssetItems.ItemsSource = null; DmsLinkItems.ItemsSource = null;
            DmsRevisionDetailText.Text = "Select a document."; DmsRequiredReadSummaryText.Text = string.Empty;
            return;
        }
        try
        {
            var revisionsTask = _features.GetDmsRevisionsAsync(row.Document.Id);
            var distributionsTask = _features.GetDmsDistributionsAsync(row.Document.Id);
            var receiptsTask = _features.GetDmsReadReceiptsAsync(row.Document.Id);
            var assetsTask = _features.GetDmsAssetReferencesAsync(row.Document.Id);
            var linksTask = _features.GetDmsDocumentLinksAsync(row.Document.Id);
            await Task.WhenAll(revisionsTask, distributionsTask, receiptsTask, assetsTask, linksTask);
            _dmsRevisions = revisionsTask.Result;
            _dmsDistributions = distributionsTask.Result;
            _dmsReceipts = receiptsTask.Result;
            _dmsAssets = assetsTask.Result;
            _dmsLinks = linksTask.Result;
            var revisionRows = _dmsRevisions.Select(x => new DmsRevisionRow(x)).ToArray();
            DmsRevisionItems.ItemsSource = revisionRows;
            DmsRevisionItems.SelectedItem = preferredRevisionId is Guid revisionId ? revisionRows.FirstOrDefault(x => x.Revision.Id == revisionId) : revisionRows.FirstOrDefault();
            DmsDistributionItems.ItemsSource = _dmsDistributions.Select(x => $"v{x.DocumentVersion}  {x.ScopeType}:{x.ScopeValue}  required={x.RequiredRead}  due={x.DueAtUtc?.LocalDateTime:G}  [{x.State}]");
            DmsReceiptItems.ItemsSource = _dmsReceipts.Select(x => $"v{x.DocumentVersion}  {x.Recipient}  [{x.State}]  read={x.ReadAtUtc?.LocalDateTime:G}  ack={x.AcknowledgedAtUtc?.LocalDateTime:G}");
            DmsAssetItems.ItemsSource = _dmsAssets.Select(x => $"v{x.DocumentVersion}  {x.FileName}  {x.ContentType}  {x.StorageReference}  sha256={x.ContentSha256 ?? "unverified"}  [{x.Classification}]");
            DmsLinkItems.ItemsSource = _dmsLinks.Select(x => $"v{x.DocumentVersion}  {x.EntityType}:{x.EntityKey}  {x.Relationship}  — {x.LinkedBy}");
            UpdateDmsRevisionDetail();
        }
        catch (Exception ex) { StatusText.Text = "DMS LOAD FAILED — " + ex.Message; }
    }

    private void UpdateDmsRevisionDetail()
    {
        if (DmsRevisionItems.SelectedItem is not DmsRevisionRow row)
        {
            DmsRevisionDetailText.Text = "Select a revision.";
            DmsRequiredReadSummaryText.Text = string.Empty;
            return;
        }
        var revision = row.Revision;
        var receipts = _dmsReceipts.Where(x => x.DocumentVersion == revision.Version).ToArray();
        var distributionIds = receipts.Select(x => x.DistributionId).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var overdue = receipts.Count(x => x.AcknowledgedAtUtc is null && _dmsDistributions.Any(d => distributionIds.Contains(d.Id) && d.Id == x.DistributionId && d.RequiredRead && d.DueAtUtc is DateTimeOffset due && due < now));
        DmsRevisionDetailText.Text = $"v{revision.Version} [{revision.State}]\nSHA-256 {revision.ContentSha256}\nAuthor {revision.Author}\nPublished {revision.PublishedAtUtc?.LocalDateTime:G} by {revision.PublishedBy ?? "-"}\n\n{revision.Content}";
        DmsRequiredReadSummaryText.Text = $"Required-read tracking: assigned={receipts.Length} • read={receipts.Count(x => x.ReadAtUtc is not null)} • acknowledged={receipts.Count(x => x.AcknowledgedAtUtc is not null)} • overdue={overdue}";
    }

    private async void DmsCreateDocumentClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) { StatusText.Text = "No agency is available."; return; }
        try
        {
            var created = await _features.CreateDmsDocumentAsync(new CreateDmsDocumentRequest(_workforceAgencyId, DmsTitleText.Text ?? string.Empty, DmsClassificationText.Text ?? "Internal", DmsContentText.Text ?? string.Empty));
            DmsTitleText.Text = string.Empty; DmsContentText.Text = string.Empty;
            await RefreshAsync(); SelectDmsDocument(created.Id); await LoadSelectedDmsDocumentAsync();
        }
        catch (Exception ex) { StatusText.Text = "DMS CREATE FAILED — " + ex.Message; }
    }

    private async void DmsCreateRevisionClicked(object? sender, RoutedEventArgs e)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow document) { StatusText.Text = "Select a document."; return; }
        try
        {
            var created = await _features.CreateDmsRevisionAsync(document.Document.Id, new CreateDmsRevisionRequest(DmsRevisionContentText.Text ?? string.Empty));
            DmsRevisionContentText.Text = string.Empty;
            await RefreshAsync(); SelectDmsDocument(document.Document.Id); await LoadSelectedDmsDocumentAsync(created.Id);
        }
        catch (Exception ex) { StatusText.Text = "DMS REVISION FAILED — " + ex.Message; }
    }

    private async void DmsStartApprovalClicked(object? sender, RoutedEventArgs e)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow document || DmsRevisionItems.SelectedItem is not DmsRevisionRow revision) { StatusText.Text = "Select a document revision."; return; }
        var approvers = SplitCsv(DmsApprovalStepsText.Text);
        if (approvers.Length == 0) { StatusText.Text = "Enter at least one approver."; return; }
        try
        {
            var steps = approvers.Select((x, index) => new ApprovalStepDefinition(index + 1, x, true)).ToArray();
            await _features.StartDmsRevisionApprovalAsync(document.Document.Id, revision.Revision.Version, new DmsApprovalRequest(DmsApprovalNameText.Text ?? "Document Publication Approval", "Sequential", steps));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "DMS APPROVAL START FAILED — " + ex.Message; }
    }

    private async void DmsPublishRevisionClicked(object? sender, RoutedEventArgs e)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow document || DmsRevisionItems.SelectedItem is not DmsRevisionRow revision) { StatusText.Text = "Select a document revision."; return; }
        try
        {
            var published = await _features.PublishDmsRevisionAsync(document.Document.Id, revision.Revision.Version, new PublishDmsRevisionRequest(true));
            await RefreshAsync(); SelectDmsDocument(document.Document.Id); await LoadSelectedDmsDocumentAsync(published.Id);
        }
        catch (Exception ex) { StatusText.Text = "DMS PUBLISH FAILED — " + ex.Message; }
    }

    private async void DmsDistributeClicked(object? sender, RoutedEventArgs e)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow document || DmsRevisionItems.SelectedItem is not DmsRevisionRow revision) { StatusText.Text = "Select a published document revision."; return; }
        var scopeType = (DmsScopeTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Agency";
        var dueAt = int.TryParse(DmsDueHoursText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dueHours) && dueHours > 0 ? DateTimeOffset.UtcNow.AddHours(dueHours) : (DateTimeOffset?)null;
        try
        {
            await _features.CreateDmsDistributionAsync(document.Document.Id, new CreateDmsDistributionRequest(revision.Revision.Version, scopeType, DmsScopeValueText.Text ?? string.Empty, DmsRequiredReadCheck.IsChecked == true, dueAt, SplitCsv(DmsRecipientsText.Text)));
            await LoadSelectedDmsDocumentAsync(revision.Revision.Id);
        }
        catch (Exception ex) { StatusText.Text = "DMS DISTRIBUTION FAILED — " + ex.Message; }
    }

    private async void DmsAddAssetClicked(object? sender, RoutedEventArgs e)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow document || DmsRevisionItems.SelectedItem is not DmsRevisionRow revision) { StatusText.Text = "Select a draft document revision."; return; }
        try
        {
            await _features.AddDmsAssetReferenceAsync(document.Document.Id, new AddDmsAssetReferenceRequest(revision.Revision.Version, DmsAssetFileNameText.Text ?? string.Empty, DmsAssetContentTypeText.Text ?? "application/octet-stream", DmsAssetStorageText.Text ?? string.Empty, DmsAssetShaText.Text, document.Document.Classification));
            DmsAssetFileNameText.Text = string.Empty; DmsAssetStorageText.Text = string.Empty; DmsAssetShaText.Text = string.Empty;
            await LoadSelectedDmsDocumentAsync(revision.Revision.Id);
        }
        catch (Exception ex) { StatusText.Text = "DMS ASSET FAILED — " + ex.Message; }
    }

    private async void DmsLinkClicked(object? sender, RoutedEventArgs e)
    {
        if (DmsDocumentItems.SelectedItem is not DmsDocumentRow document || DmsRevisionItems.SelectedItem is not DmsRevisionRow revision) { StatusText.Text = "Select a document revision."; return; }
        var entityType = (DmsLinkTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Incident";
        try
        {
            await _features.LinkDmsDocumentAsync(document.Document.Id, new CreateDmsDocumentLinkRequest(revision.Revision.Version, entityType, DmsLinkKeyText.Text ?? string.Empty, DmsLinkRelationshipText.Text ?? "Reference"));
            await LoadSelectedDmsDocumentAsync(revision.Revision.Id);
        }
        catch (Exception ex) { StatusText.Text = "DMS LINK FAILED — " + ex.Message; }
    }

    private void SelectDmsDocument(Guid id)
    {
        if (DmsDocumentItems.ItemsSource is IEnumerable<DmsDocumentRow> rows) DmsDocumentItems.SelectedItem = rows.FirstOrDefault(x => x.Document.Id == id);
    }

    private async void FormTemplateCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) { StatusText.Text = "No agency is available."; return; }
        try
        {
            await _features.CreateFormTemplateAsync(new CreateFormTemplateRequest(_workforceAgencyId, FormTemplateNameText.Text ?? string.Empty, FormTemplateSchemaText.Text ?? "{}", string.Empty));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "FORM TEMPLATE CREATE FAILED — " + ex.Message; }
    }

    private async void FormTemplatePublishClicked(object? sender, RoutedEventArgs e)
    {
        if (FormTemplateItems.SelectedItem is not FormTemplateRow row) { StatusText.Text = "Select a form template."; return; }
        try { await _features.PublishFormTemplateAsync(row.Template.Id); await RefreshAsync(); }
        catch (Exception ex) { StatusText.Text = "FORM TEMPLATE PUBLISH FAILED — " + ex.Message; }
    }

    private async void FormCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (FormTemplateItems.SelectedItem is not FormTemplateRow row) { StatusText.Text = "Select a published form template."; return; }
        Guid? incidentId = null;
        if (!string.IsNullOrWhiteSpace(FormIncidentIdText.Text))
        {
            if (!Guid.TryParse(FormIncidentIdText.Text, out var parsed)) { StatusText.Text = "Incident ID is not a valid GUID."; return; }
            incidentId = parsed;
        }
        try
        {
            var form = await _features.CreateFormAsync(new CreateFormInstanceRequest(row.Template.AgencyId, row.Template.Id, incidentId, FormValuesText.Text ?? "{}", string.Empty));
            await RefreshAsync(); SelectForm(form.Id); await LoadSelectedFormAsync();
        }
        catch (Exception ex) { StatusText.Text = "FORM CREATE FAILED — " + ex.Message; }
    }

    private async void FormSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedFormAsync();

    private async Task LoadSelectedFormAsync()
    {
        if (FormItems.SelectedItem is not FormRow row) { FormSignatureItems.ItemsSource = null; return; }
        try
        {
            var signatures = await _features.GetFormSignaturesAsync(row.Form.Id);
            FormSignatureItems.ItemsSource = signatures.Select(x => $"{x.SignedAtUtc.LocalDateTime:G}  {x.Signer}  {x.Meaning}\ntemplate v{x.TemplateVersion}  values SHA-256 {x.ValuesSha256}  source={x.Source}");
        }
        catch (Exception ex) { StatusText.Text = "FORM SIGNATURE LOAD FAILED — " + ex.Message; }
    }

    private async void FormSignClicked(object? sender, RoutedEventArgs e)
    {
        if (FormItems.SelectedItem is not FormRow row) { StatusText.Text = "Select a form."; return; }
        try
        {
            await _features.SignFormStrongAsync(row.Form.Id, new SignFormStrongRequest(FormSignatureMeaningText.Text ?? "Approved / certified by signer"));
            await RefreshAsync(); SelectForm(row.Form.Id); await LoadSelectedFormAsync();
        }
        catch (Exception ex) { StatusText.Text = "FORM SIGN FAILED — " + ex.Message; }
    }

    private void SelectForm(Guid id)
    {
        if (FormItems.ItemsSource is IEnumerable<FormRow> rows) FormItems.SelectedItem = rows.FirstOrDefault(x => x.Form.Id == id);
    }

    private async void WorkflowSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedWorkflowAsync();

    private async Task LoadSelectedWorkflowAsync()
    {
        if (WorkflowItems.SelectedItem is not WorkflowRow row) { _approvalSteps = []; ApprovalStepItems.ItemsSource = null; return; }
        try
        {
            _approvalSteps = await _features.GetApprovalStepsAsync(row.Workflow.Id);
            ApprovalStepItems.ItemsSource = _approvalSteps.Select(x => new ApprovalStepRow(x)).ToArray();
        }
        catch (Exception ex) { StatusText.Text = "APPROVAL LOAD FAILED — " + ex.Message; }
    }

    private async void ApprovalApproveClicked(object? sender, RoutedEventArgs e) => await DecidePendingApprovalAsync("Approve");
    private async void ApprovalReturnClicked(object? sender, RoutedEventArgs e) => await DecidePendingApprovalAsync("Return");
    private async void ApprovalDenyClicked(object? sender, RoutedEventArgs e) => await DecidePendingApprovalAsync("Deny");

    private async Task DecidePendingApprovalAsync(string decision)
    {
        var step = (ApprovalStepItems.SelectedItem as ApprovalStepRow)?.Step ?? _approvalSteps.FirstOrDefault(x => x.State == "Pending");
        if (step is null || step.State != "Pending") { StatusText.Text = "Select a pending approval step."; return; }
        try
        {
            await _features.DecideApprovalStepAsync(step.Id, new DecideApprovalStepRequest(string.Empty, decision, ApprovalCommentText.Text));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "APPROVAL DECISION FAILED — " + ex.Message; }
    }

    private static string[] SplitCsv(string? value) => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private DateOnly GetWorkforceDate()
    {
        if (DateOnly.TryParseExact(WorkforceDateText.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        var today = DateOnly.FromDateTime(DateTime.Today);
        WorkforceDateText.Text = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return today;
    }

    private static string StationCode(IReadOnlyList<StationDto> stations, Guid stationId) => stations.FirstOrDefault(x => x.Id == stationId)?.Code ?? stationId.ToString("D");
    private static string UnitCallSign(IReadOnlyList<UnitDto> units, Guid? unitId) => unitId is Guid id ? units.FirstOrDefault(x => x.Id == id)?.CallSign ?? id.ToString("D") : "—";

    private async void WorkforceLoadDateClicked(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async void WorkforceAddAssignedClicked(object? sender, RoutedEventArgs e) => await CreateWorkforceAssignmentAsync("Assigned", requireEmployee: true);
    private async void WorkforceAddOpenClicked(object? sender, RoutedEventArgs e) => await CreateWorkforceAssignmentAsync("Open", requireEmployee: false);

    private async Task CreateWorkforceAssignmentAsync(string state, bool requireEmployee)
    {
        if (_workforceAgencyId == Guid.Empty || WorkforceStationItems.SelectedItem is not WorkforceStationRow station) { StatusText.Text = "Select a workforce station."; return; }
        if (string.IsNullOrWhiteSpace(WorkforcePositionText.Text)) { StatusText.Text = "A workforce position is required."; return; }
        if (requireEmployee && string.IsNullOrWhiteSpace(WorkforceEmployeeNameText.Text)) { StatusText.Text = "An employee name is required."; return; }
        try
        {
            var unitId = (WorkforceUnitItems.SelectedItem as WorkforceUnitRow)?.Id;
            var request = new UpsertWorkforceAssignmentRequest(_workforceAgencyId, GetWorkforceDate(), WorkforceShiftText.Text ?? "Day", station.Id, unitId, WorkforcePositionText.Text ?? string.Empty,
                requireEmployee ? WorkforceEmployeeIdText.Text : null, requireEmployee ? WorkforceEmployeeNameText.Text ?? string.Empty : string.Empty,
                WorkforceQualificationText.Text ?? string.Empty, WorkforceAssignmentTypeText.Text ?? "Regular", state, null, null, "OperationsCenter");
            await _features.CreateWorkforceAssignmentAsync(request);
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "WORKFORCE ASSIGNMENT FAILED — " + ex.Message; }
    }

    private async void WorkforceCallOutClicked(object? sender, RoutedEventArgs e)
    {
        if (WorkforceAssignmentItems.SelectedItem is not WorkforceAssignmentRow row) { StatusText.Text = "Select a filled workforce assignment."; return; }
        try { await _features.CallOutWorkforceAssignmentAsync(row.Id, new WorkforceActionRequest("Operations Center call-out")); await RefreshAsync(); }
        catch (Exception ex) { StatusText.Text = "WORKFORCE CALL-OUT FAILED — " + ex.Message; }
    }

    private async void WorkforceRelieveClicked(object? sender, RoutedEventArgs e)
    {
        if (WorkforceAssignmentItems.SelectedItem is not WorkforceAssignmentRow row) { StatusText.Text = "Select a filled workforce assignment."; return; }
        if (string.IsNullOrWhiteSpace(WorkforceEmployeeNameText.Text)) { StatusText.Text = "Enter the relief employee above."; return; }
        try
        {
            await _features.RelieveWorkforceAssignmentAsync(row.Id, new WorkforceReliefRequest(WorkforceEmployeeIdText.Text, WorkforceEmployeeNameText.Text!, WorkforceQualificationText.Text ?? string.Empty, WorkforceAssignmentTypeText.Text ?? "Relief", "Operations Center relief"));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "WORKFORCE RELIEF FAILED — " + ex.Message; }
    }

    private async void WorkforceAddRuleClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) { StatusText.Text = "No workforce agency is available."; return; }
        if (!int.TryParse(WorkforceRuleMinimumText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum) || minimum < 1) { StatusText.Text = "Minimum staffing must be a positive whole number."; return; }
        var stationId = WorkforceRuleAgencyWideCheck.IsChecked == true ? null : (WorkforceRuleStationItems.SelectedItem as WorkforceStationRow)?.Id;
        if (WorkforceRuleAgencyWideCheck.IsChecked != true && stationId is null) { StatusText.Text = "Select a station or choose agency-wide."; return; }
        try
        {
            await _features.CreateStaffingRuleAsync(new UpsertStaffingRuleRequest(_workforceAgencyId, stationId, WorkforceRuleShiftText.Text ?? "Day", WorkforceRulePositionText.Text ?? string.Empty, minimum, WorkforceRuleQualificationText.Text ?? string.Empty, true));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "STAFFING RULE FAILED — " + ex.Message; }
    }

    private async void WorkforcePublishClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) return;
        try
        {
            var posted = await _features.PublishWorkforceRosterAsync(_workforceAgencyId, GetWorkforceDate(), new PublishWorkforceRosterRequest("Posted from Operations Center"));
            StatusText.Text = $"Roster revision {posted.Revision} posted — {posted.State}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "ROSTER PUBLISH FAILED — " + ex.Message; }
    }

    private async void WorkforcePreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) return;
        try
        {
            var preview = await _features.GetWorkforceRosterPrintViewAsync(_workforceAgencyId, GetWorkforceDate(), WorkforceIncludeChangesCheck.IsChecked == true);
            WorkforcePrintPreviewText.Text = preview.Content;
            StatusText.Text = $"Roster preview SHA-256 {preview.ContentSha256}";
        }
        catch (Exception ex) { StatusText.Text = "ROSTER PREVIEW FAILED — " + ex.Message; }
    }

    private async void WorkforcePrintClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) return;
        try
        {
            var result = await _features.PrintWorkforceRosterAsync(_workforceAgencyId, GetWorkforceDate(), WorkforceIncludeChangesCheck.IsChecked == true, WorkforcePrinterKeyText.Text);
            StatusText.Text = $"Roster print {result.State} — ref={result.ProviderReference ?? "-"} error={result.Error ?? "-"}";
        }
        catch (Exception ex) { StatusText.Text = "ROSTER PRINT FAILED — " + ex.Message; }
    }

    private async void RmsReportSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedRmsReportAsync();

    private async Task LoadSelectedRmsReportAsync()
    {
        if (RmsReportItems.SelectedItem is not RmsReportRow row)
        {
            RmsReportDetailText.Text = string.Empty; RmsVersionItems.ItemsSource = null; RmsAttachmentItems.ItemsSource = null; RmsValidationText.Text = "Select a report."; return;
        }
        try
        {
            var report = await _core.GetRmsIncidentReportAsync(row.Id) ?? throw new InvalidOperationException("RMS report not found.");
            var validationTask = _core.GetRmsValidationAsync(row.Id);
            var versionsTask = _core.GetRmsReportVersionsAsync(row.Id);
            var attachmentsTask = _core.GetRmsAttachmentsAsync(row.Id);
            await Task.WhenAll(validationTask, versionsTask, attachmentsTask);
            var validation = validationTask.Result;
            RmsValidationText.Text = validation.Errors.Count == 0
                ? $"Canonical readiness: {(validation.CanonicalReady ? "READY" : "NOT READY")} • provider validation pending={validation.ProviderValidationPending} • warnings={validation.Warnings.Count}"
                : "VALIDATION ERRORS — " + string.Join("; ", validation.Errors);
            RmsSummaryText.Text = report.Summary;
            RmsReportDetailText.Text = FormatRmsReport(report);
            RmsVersionItems.ItemsSource = versionsTask.Result.OrderByDescending(x => x.Version).Select(x => $"v{x.Version}  {x.Status}  {x.ChangeKind}  {x.Actor}  {x.CreatedAtUtc.ToString("u", CultureInfo.InvariantCulture)}  {x.Reason}");
            RmsAttachmentItems.ItemsSource = attachmentsTask.Result.Select(x => $"{x.Classification}  {x.FileName}  {x.StorageReference}  {x.AddedBy}");
        }
        catch (Exception ex) { StatusText.Text = "RMS LOAD FAILED — " + ex.Message; }
    }

    private async void RmsCreateClicked(object? sender, RoutedEventArgs e)
    {
        if (RmsIncidentItems.SelectedItem is not RmsIncidentRow row) { StatusText.Text = "Select a CAD incident for the RMS handoff."; return; }
        try { var report = await _core.CreateRmsFromIncidentAsync(row.Id, new CreateRmsFromIncidentRequest(row.AgencyId, "FireIncident", RmsSummaryText.Text ?? string.Empty, Guid.NewGuid().ToString("N"))); await RefreshAsync(); SelectRmsReport(report.Id); }
        catch (Exception ex) { StatusText.Text = "RMS CREATE FAILED — " + ex.Message; }
    }
    private async void RmsRefreshCadClicked(object? sender, RoutedEventArgs e) => await RunRmsActionAsync(async row => await _core.RefreshRmsFromCadAsync(row.Id, new RefreshRmsCadSnapshotRequest(row.Version, Guid.NewGuid().ToString("N"))));
    private async void RmsSaveDraftClicked(object? sender, RoutedEventArgs e) => await RunRmsActionAsync(async row => await _core.UpdateRmsIncidentReportAsync(row.Id, new UpdateRmsIncidentReportRequest(row.Version, null, null, null, null, null, null, null, null, RmsSummaryText.Text, Guid.NewGuid().ToString("N"))));
    private async void RmsSubmitReviewClicked(object? sender, RoutedEventArgs e) => await RunRmsActionAsync(async row => await _core.SubmitRmsForReviewAsync(row.Id, new SubmitRmsForReviewRequest(row.Version, RmsReasonText.Text, Guid.NewGuid().ToString("N"))));
    private async void RmsReturnDraftClicked(object? sender, RoutedEventArgs e) => await RunRmsActionAsync(async row => await _core.ReviewRmsAsync(row.Id, new ReviewRmsIncidentReportRequest(row.Version, "return", RmsReasonText.Text, Guid.NewGuid().ToString("N"))));
    private async void RmsFinalizeClicked(object? sender, RoutedEventArgs e) => await RunRmsActionAsync(async row => await _core.ReviewRmsAsync(row.Id, new ReviewRmsIncidentReportRequest(row.Version, "approve", RmsReasonText.Text, Guid.NewGuid().ToString("N"))));
    private async void RmsAmendClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RmsReasonText.Text)) { StatusText.Text = "A finalized amendment requires a reason."; return; }
        await RunRmsActionAsync(async row => await _core.AmendRmsAsync(row.Id, new AmendRmsIncidentReportRequest(row.Version, RmsReasonText.Text!, null, null, null, null, null, null, null, null, RmsSummaryText.Text, Guid.NewGuid().ToString("N"))));
    }
    private async void RmsAddAttachmentClicked(object? sender, RoutedEventArgs e)
    {
        if (RmsReportItems.SelectedItem is not RmsReportRow row) { StatusText.Text = "Select an RMS report."; return; }
        try { await _core.AddRmsAttachmentAsync(row.Id, new AddRmsAttachmentReferenceRequest(RmsAttachmentName.Text ?? string.Empty, RmsAttachmentClassification.Text ?? string.Empty, RmsAttachmentStorage.Text ?? string.Empty, RmsAttachmentDescription.Text, Guid.NewGuid().ToString("N"))); await LoadSelectedRmsReportAsync(); }
        catch (Exception ex) { StatusText.Text = "RMS ATTACHMENT FAILED — " + ex.Message; }
    }
    private async Task RunRmsActionAsync(Func<RmsReportRow, Task<RmsIncidentReportDto>> action)
    {
        if (RmsReportItems.SelectedItem is not RmsReportRow row) { StatusText.Text = "Select an RMS report."; return; }
        try { var report = await action(row); await RefreshAsync(); SelectRmsReport(report.Id); await LoadSelectedRmsReportAsync(); }
        catch (Exception ex) { StatusText.Text = "RMS ACTION FAILED — " + ex.Message; }
    }
    private void SelectRmsReport(Guid id)
    {
        if (RmsReportItems.ItemsSource is IEnumerable<RmsReportRow> rows) RmsReportItems.SelectedItem = rows.FirstOrDefault(x => x.Id == id);
    }
    private static string FormatRmsReport(RmsIncidentReportDto report)
    {
        var lines = new List<string>
        {
            $"{report.Number}  {report.ReportType}  [{report.Status}]  v{report.Version}",
            $"CAD: {report.IncidentNumber} source-v{report.SourceIncidentVersion} captured {report.SourceCapturedAtUtc.ToString("u", CultureInfo.InvariantCulture)}",
            $"Nature: {report.Nature} / {report.Priority}",
            $"Location: {report.Address}  {report.CrossStreets}  {report.Municipality}",
            $"Caller: {report.CallerName}  {report.CallerPhone}",
            $"Received: {report.ReceivedAtUtc.ToString("u", CultureInfo.InvariantCulture)}  Closed: {report.ClosedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "OPEN"}",
            "", "UNITS"
        };
        lines.AddRange(report.Units.Select(x => $"  {x.CallSign}  {x.UnitType}  {x.LastIncidentStatus}"));
        lines.Add("COMMAND"); lines.AddRange(report.CommandAssignments.Select(x => $"  {x.CommandName}: {x.Person}"));
        lines.Add("MUTUAL AID"); lines.AddRange(report.MutualAid.Select(x => $"  {x.RequestedAgency}  {x.ResourceType} x{x.Quantity} [{x.State}]"));
        lines.Add(""); lines.Add("SUMMARY"); lines.Add(report.Summary); lines.Add(""); lines.Add("NARRATIVE"); lines.Add(report.Narrative);
        lines.Add(""); lines.Add("PROVENANCE"); lines.AddRange(report.Provenance.OrderBy(x => x.RecordedAtUtc).Select(x => $"  {x.Field}: {x.SourceKind}  {x.SourceReference}  {x.Actor}"));
        if (report.FinalizedAtUtc is not null) lines.Add($"Finalized: {report.FinalizedBy}  {report.FinalizedAtUtc.Value.ToString("u", CultureInfo.InvariantCulture)}");
        return string.Join(Environment.NewLine, lines);
    }


    private async void NerisSubmissionSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedNerisSubmissionAsync();

    private async Task LoadSelectedNerisSubmissionAsync()
    {
        if (NerisSubmissionItems.SelectedItem is not NerisSubmissionRow row)
        {
            NerisProviderValidationText.Text = "Select a NERIS submission.";
            NerisSubmissionDetailText.Text = string.Empty;
            NerisAttemptItems.ItemsSource = null;
            return;
        }
        try
        {
            var submission = await _core.GetNerisSubmissionAsync(row.Id) ?? throw new InvalidOperationException("NERIS submission not found.");
            var attempts = await _core.GetNerisSubmissionAttemptsAsync(row.Id);
            NerisSubmissionDetailText.Text = FormatNerisSubmission(submission);
            NerisAttemptItems.ItemsSource = attempts.OrderByDescending(x => x.AttemptNumber).Select(x => $"#{x.AttemptNumber}  {x.ProviderKey}  [{x.State}]  op={x.OperationId:D}  ref={x.ProviderReference ?? "-"}  error={x.Error ?? "-"}");
            NerisProviderValidationText.Text = submission.MappingErrors.Count == 0
                ? $"Mapping READY • warnings={submission.MappingWarnings.Count} • SHA-256={submission.PayloadSha256}"
                : "MAPPING ERRORS — " + string.Join("; ", submission.MappingErrors);
            if (!string.IsNullOrWhiteSpace(submission.ProviderKey)) NerisProviderKeyText.Text = submission.ProviderKey;
        }
        catch (Exception ex) { StatusText.Text = "NERIS LOAD FAILED — " + ex.Message; }
    }

    private async void NerisBuildClicked(object? sender, RoutedEventArgs e)
    {
        if (NerisRmsReportItems.SelectedItem is not NerisRmsReportRow row) { StatusText.Text = "Select a finalized RMS report."; return; }
        try
        {
            var built = await _core.BuildNerisSubmissionAsync(row.Id, new BuildNerisSubmissionRequest(NerisProfileText.Text, Guid.NewGuid().ToString("N")));
            await RefreshAsync(); SelectNerisSubmission(built.Id); await LoadSelectedNerisSubmissionAsync();
        }
        catch (Exception ex) { StatusText.Text = "NERIS BUILD FAILED — " + ex.Message; }
    }

    private async void NerisValidateProviderClicked(object? sender, RoutedEventArgs e)
    {
        if (NerisSubmissionItems.SelectedItem is not NerisSubmissionRow row) { StatusText.Text = "Select a NERIS submission."; return; }
        try
        {
            var validation = await _core.ValidateNerisProviderAsync(row.Id, NerisProviderKeyText.Text);
            NerisProviderValidationText.Text = validation.Valid
                ? $"Provider {validation.ProviderKey}: VALID • warnings={validation.Warnings.Count}"
                : $"Provider {validation.ProviderKey}: INVALID — {string.Join("; ", validation.Errors)}";
        }
        catch (Exception ex) { StatusText.Text = "NERIS PROVIDER VALIDATION FAILED — " + ex.Message; }
    }

    private async void NerisQueueClicked(object? sender, RoutedEventArgs e) => await RunNerisActionAsync(row => _core.QueueNerisSubmissionAsync(row.Id, new QueueNerisSubmissionRequest(NerisProviderKeyText.Text, Guid.NewGuid().ToString("N"))));

    private async void NerisRetryClicked(object? sender, RoutedEventArgs e) => await RunNerisActionAsync(row => _core.RetryNerisSubmissionAsync(row.Id, new RetryNerisSubmissionRequest(NerisProviderKeyText.Text, NerisConfirmDuplicateCheck.IsChecked == true, Guid.NewGuid().ToString("N"))));

    private async void NerisReconcileClicked(object? sender, RoutedEventArgs e) => await RunNerisActionAsync(row => _core.ReconcileNerisSubmissionAsync(row.Id, new ReconcileNerisSubmissionRequest(NerisReconcileResolutionText.Text ?? string.Empty, NerisReconcileReferenceText.Text, NerisReconcileNotesText.Text, Guid.NewGuid().ToString("N"))));

    private async Task RunNerisActionAsync(Func<NerisSubmissionRow, Task<NerisSubmissionDto>> action)
    {
        if (NerisSubmissionItems.SelectedItem is not NerisSubmissionRow row) { StatusText.Text = "Select a NERIS submission."; return; }
        try
        {
            var updated = await action(row);
            await RefreshAsync(); SelectNerisSubmission(updated.Id); await LoadSelectedNerisSubmissionAsync();
        }
        catch (Exception ex) { StatusText.Text = "NERIS ACTION FAILED — " + ex.Message; }
    }

    private void SelectNerisSubmission(Guid id)
    {
        if (NerisSubmissionItems.ItemsSource is IEnumerable<NerisSubmissionRow> rows) NerisSubmissionItems.SelectedItem = rows.FirstOrDefault(x => x.Id == id);
    }

    private static string FormatNerisSubmission(NerisSubmissionDto submission)
    {
        string payload;
        try
        {
            using var document = JsonDocument.Parse(submission.PayloadJson);
            payload = JsonSerializer.Serialize(document.RootElement, NerisPrettyJsonOptions);
        }
        catch { payload = submission.PayloadJson; }
        var lines = new List<string>
        {
            $"Submission {submission.Id:D}",
            $"RMS {submission.RmsReportId:D} v{submission.RmsReportVersion}",
            $"Profile: {submission.ProfileVersion}",
            $"State: {submission.State}",
            $"Payload SHA-256: {submission.PayloadSha256}",
            $"Provider: {submission.ProviderKey ?? "not selected"}",
            $"Provider reference: {submission.ProviderReference ?? "-"}",
            $"Attempts: {submission.AttemptCount}",
            $"Last error: {submission.LastError ?? "-"}",
            "",
            "MAPPING WARNINGS"
        };
        lines.AddRange(submission.MappingWarnings.Select(x => "  " + x));
        lines.Add(""); lines.Add("MAPPING ERRORS"); lines.AddRange(submission.MappingErrors.Select(x => "  " + x));
        lines.Add(""); lines.Add("PAYLOAD"); lines.Add(payload);
        return string.Join(Environment.NewLine, lines);
    }


    private async void IntegrationOperationSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedIntegrationOperationAsync();

    private async Task LoadSelectedIntegrationOperationAsync()
    {
        if (IntegrationOperationItems.SelectedItem is not IntegrationOperationRow row)
        {
            IntegrationAttemptItems.ItemsSource = null;
            return;
        }
        try
        {
            var attempts = await _features.GetIntegrationAttemptsAsync(row.Id);
            IntegrationAttemptItems.ItemsSource = attempts.OrderByDescending(x => x.AttemptNumber).Select(x => $"#{x.AttemptNumber}  [{x.State}]  op={x.ProviderOperationId:D}  ref={x.ProviderReference ?? "-"}  error={x.Error ?? "-"}");
        }
        catch (Exception ex) { StatusText.Text = "INTEGRATION ATTEMPT LOAD FAILED — " + ex.Message; }
    }

    private async void Integration911Clicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            _ = await _features.Ingest911GatewayEventAsync(new Ingest911GatewayEventRequest(_workforceAgencyId, "sim-911", Integration911CallIdText.Text ?? string.Empty, Integration911StateText.Text ?? "Open", "Simulation Caller", "555-0100", Integration911LocationText.Text ?? string.Empty, Integration911NatureText.Text ?? string.Empty, DateTimeOffset.UtcNow));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "911 GATEWAY FAILED — " + ex.Message; }
    }

    private async void IntegrationRadioClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            var unitId = OptionalGuid(IntegrationRadioUnitIdText.Text);
            var nativeId = IntegrationRadioNativeIdText.Text ?? string.Empty;
            _ = await _features.IngestRadioGatewayEventAsync(new IngestRadioGatewayEventRequest(_workforceAgencyId, "sim-radio", $"UI-{Guid.NewGuid():N}", IntegrationRadioEventText.Text ?? "Emergency", nativeId, "Dispatch 1", unitId, IntegrationRadioStatusText.Text, DateTimeOffset.UtcNow));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "RADIO GATEWAY FAILED — " + ex.Message; }
    }

    private async void IntegrationClearRadioClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            _ = await _features.ClearRadioEmergencyAsync(new RadioEmergencyClearRequest(_workforceAgencyId, null, OptionalGuid(IntegrationRadioUnitIdText.Text), IntegrationRadioNativeIdText.Text ?? string.Empty, "simulation-radio", Guid.NewGuid().ToString("N")));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "RADIO CLEAR FAILED — " + ex.Message; }
    }

    private async void IntegrationAisClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            var latitude = RequiredDouble(IntegrationAisLatitudeText.Text, "AIS latitude");
            var longitude = RequiredDouble(IntegrationAisLongitudeText.Text, "AIS longitude");
            _ = await _features.IngestAisGatewayEventAsync(new IngestAisGatewayEventRequest(_workforceAgencyId, "sim-ais", $"UI-{Guid.NewGuid():N}", IntegrationAisMmsiText.Text ?? string.Empty, IntegrationAisNameText.Text, latitude, longitude, 8.5, 180, DateTimeOffset.UtcNow));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "AIS GATEWAY FAILED — " + ex.Message; }
    }

    private async void IntegrationRouteClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            var from = ParseCoordinatePair(IntegrationRouteFromText.Text, "route origin");
            var to = ParseCoordinatePair(IntegrationRouteToText.Text, "route destination");
            var result = await _features.GetProviderRouteAsync(new ProviderRouteRequest(_workforceAgencyId, from.Latitude, from.Longitude, to.Latitude, to.Longitude, null, null, true, "Emergency", "simulation-routing"));
            IntegrationRouteResultText.Text = $"{result.ProviderKey}: {result.DistanceMeters:0} m / {result.DurationSeconds}s  warnings={string.Join("; ", result.Warnings)}";
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "ROUTING FAILED — " + ex.Message; }
    }

    private async void IntegrationRecordingClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            var incidentId = OptionalGuid(IntegrationRecordingIncidentText.Text);
            var now = DateTimeOffset.UtcNow;
            _ = await _features.QueryRecordingsAsync(new RecordingLookupRequest(_workforceAgencyId, incidentId, IntegrationRecordingChannelText.Text, null, now.AddHours(-1), now.AddHours(1), "simulation-recording"));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "RECORDING LOOKUP FAILED — " + ex.Message; }
    }

    private async void IntegrationDiscoverCamerasClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        try
        {
            _integrationCandidates = await _features.DiscoverCamerasAsync(new CameraDiscoveryRequest(_workforceAgencyId, OptionalGuid(IntegrationCameraStationText.Text), "simulation-onvif"));
            IntegrationDiscoveryItems.ItemsSource = _integrationCandidates.Select(x => new IntegrationCameraCandidateRow(x, $"{x.Name}  {x.Manufacturer} {x.Model}  {x.Uri}  [{x.State}]"));
        }
        catch (Exception ex) { StatusText.Text = "CAMERA DISCOVERY FAILED — " + ex.Message; }
    }

    private async void IntegrationImportCameraClicked(object? sender, RoutedEventArgs e)
    {
        if (!EnsureIntegrationAgency()) return;
        if (IntegrationDiscoveryItems.SelectedItem is not IntegrationCameraCandidateRow row) { StatusText.Text = "Select a discovered camera."; return; }
        try
        {
            _ = await _features.ImportDiscoveredCameraAsync(new ImportDiscoveredCameraRequest(_workforceAgencyId, OptionalGuid(IntegrationCameraStationText.Text), null, row.Candidate));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "CAMERA IMPORT FAILED — " + ex.Message; }
    }

    private async void IntegrationRetryClicked(object? sender, RoutedEventArgs e)
    {
        if (IntegrationOperationItems.SelectedItem is not IntegrationOperationRow row) { StatusText.Text = "Select a provider operation."; return; }
        try
        {
            _ = await _features.RetryIntegrationOperationAsync(row.Id, new RetryIntegrationOperationRequest(IntegrationConfirmDuplicateCheck.IsChecked == true, Guid.NewGuid().ToString("N")));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "INTEGRATION RETRY FAILED — " + ex.Message; }
    }

    private async void IntegrationReconcileClicked(object? sender, RoutedEventArgs e)
    {
        if (IntegrationOperationItems.SelectedItem is not IntegrationOperationRow row) { StatusText.Text = "Select a provider operation."; return; }
        try
        {
            _ = await _features.ReconcileIntegrationOperationAsync(row.Id, new ReconcileIntegrationOperationRequest(IntegrationReconcileResolutionText.Text ?? string.Empty, IntegrationReconcileReferenceText.Text, "Operations Center reconciliation"));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "INTEGRATION RECONCILIATION FAILED — " + ex.Message; }
    }

    private bool EnsureIntegrationAgency()
    {
        if (_workforceAgencyId != Guid.Empty) return true;
        StatusText.Text = "No signed-in agency context is available.";
        return false;
    }

    private static Guid? OptionalGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Guid.TryParse(value, out var parsed) ? parsed : throw new FormatException("Expected a GUID value.");
    }

    private static double RequiredDouble(string? value, string field) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new FormatException($"{field} must be a number.");

    private static (double Latitude, double Longitude) ParseCoordinatePair(string? value, string field)
    {
        var parts = (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new FormatException($"{field} must be latitude,longitude.");
        return (RequiredDouble(parts[0], field + " latitude"), RequiredDouble(parts[1], field + " longitude"));
    }

    private async void MaydayAcknowledgeClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedMaydayAsync("acknowledge", null, "Supervisor acknowledged emergency");
    private async void MaydayAssignClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedMaydayAsync("assign", "Operations Supervisor", "Supervisor assumed Mayday ownership");
    private async void MaydayEscalateClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedMaydayAsync("escalate", null, "Supervisor escalated emergency response");
    private async void MaydayResolveClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedMaydayAsync("resolve", null, "Supervisor resolved emergency");

    private async Task ActOnSelectedMaydayAsync(string action, string? assignedTo, string notes)
    {
        if (MaydayItems.SelectedItem is not MaydayRow row) { StatusText.Text = "Select an active Mayday."; return; }
        try
        {
            await _features.ActOnMaydayAsync(row.Id, action, new MaydayActionRequest(assignedTo, notes));
            await RefreshAsync();
        }
        catch (Exception ex) { StatusText.Text = "MAYDAY ACTION FAILED — " + ex.Message; }
    }


    public async ValueTask DisposeAsync()
    {
        await _chatRealtime.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

public sealed record DmsDocumentRow(DmsDocumentSummaryDto Document)
{
    public override string ToString() => $"{Document.Title}  v{Document.CurrentVersion} [{Document.State}]  published={Document.PublishedVersion}  reads={Document.CompletedRequiredReads}/{Document.RequiredReads}";
}
public sealed record DmsRevisionRow(DmsDocumentRevisionDto Revision)
{
    public override string ToString() => $"v{Revision.Version}  [{Revision.State}]  {Revision.ContentSha256[..Math.Min(12, Revision.ContentSha256.Length)]}  {Revision.Author}";
}
public sealed record FormTemplateRow(FormTemplateDto Template) { public override string ToString() => $"{Template.Name} v{Template.Version}  [{Template.Status}]"; }
public sealed record FormRow(FormInstanceDto Form) { public override string ToString() => $"{Form.Id:D}  [{Form.Status}]  {Form.CreatedBy}"; }
public sealed record WorkflowRow(ApprovalWorkflowDto Workflow) { public override string ToString() => $"{Workflow.Name}  {Workflow.EntityType}:{Workflow.EntityId:D}  [{Workflow.State}]"; }
public sealed record ApprovalStepRow(ApprovalStepDto Step) { public override string ToString() => $"#{Step.Sequence}  {Step.Approver}  [{Step.State}]  {Step.Comment ?? string.Empty}"; }
public sealed record RmsIncidentRow(Guid Id, Guid AgencyId, string Display) { public override string ToString() => Display; }
public sealed record RmsReportRow(Guid Id, Guid IncidentId, int Version, string Display) { public override string ToString() => Display; }
public sealed record NerisRmsReportRow(Guid Id, int Version, string Display) { public override string ToString() => Display; }
public sealed record NerisSubmissionRow(Guid Id, Guid RmsReportId, int RmsReportVersion, string State, string Display) { public override string ToString() => Display; }
public sealed record MaydayRow(Guid Id, string Display) { public override string ToString() => Display; }
public sealed record WorkforceStationRow(Guid Id, string Code, string Name) { public override string ToString() => $"{Code} — {Name}"; }
public sealed record WorkforceUnitRow(Guid? Id, string Display) { public override string ToString() => Display; }
public sealed record WorkforceAssignmentRow(Guid Id, string Display) { public override string ToString() => Display; }

public sealed record IntegrationOperationRow(Guid Id, string Display) { public override string ToString() => Display; }
public sealed record IntegrationCameraCandidateRow(CameraDiscoveryCandidateDto Candidate, string Display) { public override string ToString() => Display; }
