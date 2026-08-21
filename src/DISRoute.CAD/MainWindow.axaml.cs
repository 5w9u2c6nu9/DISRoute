using System.Globalization;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;

namespace DISRoute.CAD;

public sealed partial class MainWindow : Window
{
    private const string CadActor = "CAD operator";
    private static readonly string[] AllCrgFloorsPrefix = ["ALL"];
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private readonly OperationalApiClient _core;
    private readonly FeatureApiClient _features;
    private IReadOnlyList<IncidentDto> _incidents = [];
    private IReadOnlyList<UnitDto> _units = [];
    private IReadOnlyList<StationDto> _stations = [];
    private IReadOnlyList<AlertDto> _alerts = [];
    private IReadOnlyList<DispatchActionDto> _dispatchActions = [];
    private IReadOnlyList<DispatchDeliveryDto> _dispatchDeliveries = [];
    private IReadOnlyList<FireWatchDto> _fireWatches = [];
    private IReadOnlyList<RelocationDto> _relocations = [];
    private IncidentSiteIntelligenceDto? _siteIntelligence;
    private long _sharedNarrativeVersion;
    private string _sharedNarrativeBaseText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _core = new OperationalApiClient(SharedHttp);
        _features = new FeatureApiClient(SharedHttp);
        Opened += async (_, _) => await RefreshAllAsync();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        clock.Start();
    }

    private static HttpClient CreateHttpClient() => ClientHttpFactory.Create(TimeSpan.FromSeconds(15));

    private IncidentDto? SelectedIncident => IncidentList.SelectedItem as IncidentDto;
    private UnitRow? SelectedUnit => UnitList.SelectedItem as UnitRow;
    private UnitRow[] SelectedUnits => UnitList.SelectedItems?.OfType<UnitRow>().ToArray() ?? (SelectedUnit is UnitRow one ? [one] : []);
    private DispatchActionRow? SelectedDispatchAction => DispatchActionList.SelectedItem as DispatchActionRow;
    private FireWatchRow? SelectedFireWatch => FireWatchList.SelectedItem as FireWatchRow;
    private RelocationRow? SelectedRelocation => RelocationList.SelectedItem as RelocationRow;
    private MutualAidRow? SelectedMutualAid => MutualAidList.SelectedItem as MutualAidRow;
    private OperationalMessageRow? SelectedOperationalMessage => OperationalMessageList.SelectedItem as OperationalMessageRow;
    private AlertRow? SelectedAlert => AlertList.SelectedItem as AlertRow;
    private RoadRestrictionRow? SelectedRoadRestriction => RoadRestrictionList.SelectedItem as RoadRestrictionRow;
    private IncidentTargetRow? SelectedRelatedIncident => RelatedIncidentCombo.SelectedItem as IncidentTargetRow;
    private IncidentTargetRow? SelectedMergeTarget => MergeTargetCombo.SelectedItem as IncidentTargetRow;

    private async Task RefreshAllAsync(Guid? keepIncident = null)
    {
        try
        {
            ConnectionText.Text = "Refreshing…";
            var stationTask = _core.GetStationsAsync();
            var unitTask = _core.GetUnitsAsync();
            var incidentTask = _core.GetIncidentsAsync(openOnly: false, string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text);
            var alertTask = _features.GetAlertsAsync();
            var dispatchTask = _features.GetDispatchActionsAsync();
            var deliveryTask = _features.GetDispatchDeliveriesAsync();
            var fireWatchTask = _features.GetFireWatchesAsync();
            var relocationTask = _features.GetRelocationsAsync();
            await Task.WhenAll(stationTask, unitTask, incidentTask, alertTask, dispatchTask, deliveryTask, fireWatchTask, relocationTask);

            _stations = stationTask.Result;
            _units = unitTask.Result;
            _incidents = incidentTask.Result;
            _alerts = alertTask.Result;
            _dispatchActions = dispatchTask.Result;
            _dispatchDeliveries = deliveryTask.Result;
            _fireWatches = fireWatchTask.Result;
            _relocations = relocationTask.Result;

            var stationRows = _stations.Select(x => new StationRow(x.Id, $"{x.Code} — {x.Name}")).ToArray();
            StationCombo.ItemsSource = stationRows;
            DestinationStationCombo.ItemsSource = stationRows;
            if (StationCombo.SelectedIndex < 0 && stationRows.Length > 0) StationCombo.SelectedIndex = 0;
            if (DestinationStationCombo.SelectedIndex < 0 && stationRows.Length > 0) DestinationStationCombo.SelectedIndex = 0;

            IncidentList.ItemsSource = _incidents;
            UnitList.ItemsSource = BuildUnitRows();
            UnitCountText.Text = $"{_units.Count} units";
            AlertList.ItemsSource = _alerts.Select(x => new AlertRow(x.Id, $"{x.Severity} • {x.State}", x.Summary, $"Claimed: {x.ClaimedBy ?? "—"}")).ToArray();
            RelocationList.ItemsSource = BuildRelocationRows();

            if (keepIncident is Guid iid) IncidentList.SelectedItem = _incidents.FirstOrDefault(x => x.Id == iid);
            ConnectionText.Text = "CONNECTED";
            await RefreshIncidentAsync();
        }
        catch (Exception ex)
        {
            ConnectionText.Text = "DEGRADED";
            ActionText.Text = ex.Message;
        }
    }

    private UnitRow[] BuildUnitRows() => _units.Select(u => new UnitRow(
        u.Id,
        u.CallSign,
        u.Status,
        u.UnitType,
        u.IncidentId,
        u.IncidentId is Guid iid ? _incidents.FirstOrDefault(i => i.Id == iid)?.Number ?? "Assigned" : "—")).ToArray();

    private RelocationRow[] BuildRelocationRows() => _relocations
        .OrderBy(x => x.Status == "Active" ? 0 : 1)
        .ThenByDescending(x => x.StartedAtUtc)
        .Select(x => new RelocationRow(
            x.Id,
            x.UnitId,
            _units.FirstOrDefault(u => u.Id == x.UnitId)?.CallSign ?? x.UnitId.ToString("N")[..6],
            _stations.FirstOrDefault(s => s.Id == x.DestinationStationId)?.Code ?? "Unknown",
            x.Status,
            x.Reason))
        .ToArray();

    private async Task RefreshIncidentAsync()
    {
        var incident = SelectedIncident;
        if (incident is null)
        {
            IncidentHeader.Text = "Select an incident";
            IncidentStatus.Text = string.Empty;
            IncidentDetail.Text = string.Empty;
            ActivityList.ItemsSource = Array.Empty<ActivityRow>();
            SharedNarrativeBox.Text = string.Empty;
            SharedNarrativeVersionText.Text = string.Empty;
            SharedNarrativeConflictText.Text = string.Empty;
            RecommendationList.ItemsSource = Array.Empty<RecommendationRow>();
            RecommendationWarningsText.Text = string.Empty;
            DispatchActionList.ItemsSource = Array.Empty<DispatchActionRow>();
            FireWatchList.ItemsSource = Array.Empty<FireWatchRow>();
            CommandList.ItemsSource = Array.Empty<CommandRow>();
            GroupList.ItemsSource = Array.Empty<GroupRow>();
            MutualAidList.ItemsSource = Array.Empty<MutualAidRow>();
            OperationalMessageList.ItemsSource = Array.Empty<OperationalMessageRow>();
            PremiseList.ItemsSource = Array.Empty<PremiseRow>();
            PreplanList.ItemsSource = Array.Empty<PreplanRow>();
            CrgList.ItemsSource = Array.Empty<CrgRow>();
            HydrantList.ItemsSource = Array.Empty<HydrantRow>();
            RoadRestrictionList.ItemsSource = Array.Empty<RoadRestrictionRow>();
            MapObjectList.ItemsSource = Array.Empty<MapObjectRow>();
            CrgFloorCombo.ItemsSource = Array.Empty<string>();
            SiteIntelligenceSummaryText.Text = string.Empty;
            CorrectionList.ItemsSource = Array.Empty<CorrectionRow>();
            RelationshipList.ItemsSource = Array.Empty<RelationshipRow>();
            RelatedIncidentCombo.ItemsSource = Array.Empty<IncidentTargetRow>();
            MergeTargetCombo.ItemsSource = Array.Empty<IncidentTargetRow>();
            _siteIntelligence = null;
            DispatchInfoText.Text = string.Empty;
            _sharedNarrativeVersion = 0;
            _sharedNarrativeBaseText = string.Empty;
            return;
        }

        IncidentHeader.Text = $"{incident.Number} — {incident.Nature}";
        IncidentStatus.Text = $"{incident.Priority} / {incident.Status} / v{incident.Version}";
        IncidentDetail.Text = $"{incident.Address}  {incident.CrossStreets}  {incident.Municipality}\nCaller: {incident.CallerName} {incident.CallerPhone}\n{incident.Notes}";

        try
        {
            var activityTask = _core.GetActivityAsync(incident.Id);
            var sharedTask = _features.GetCollaborativeNarrativeAsync(incident.Id);
            var recommendationTask = _features.GetResponseRecommendationAsync(new ResponseRecommendationRequest(incident.AgencyId, incident.Id, incident.Nature, incident.Priority, incident.StationId, []));
            var alarmTask = _features.GetAdditionalAlarmsAsync(incident.Id);
            var commandTask = _features.GetCommandAssignmentsAsync(incident.Id);
            var groupTask = _features.GetOperationalGroupsAsync(incident.Id);
            var mutualAidTask = _features.GetMutualAidAsync(incident.Id);
            var messageTask = _features.GetOperationalMessagesAsync(incident.AgencyId, incident.Id);
            var siteTask = _features.GetSiteIntelligenceAsync(incident.Id);
            var correctionTask = _features.GetIncidentCorrectionsAsync(incident.Id);
            var relationshipTask = _features.GetIncidentRelationshipsAsync(incident.Id);
            await Task.WhenAll(activityTask, sharedTask, recommendationTask, alarmTask, commandTask, groupTask, mutualAidTask, messageTask, siteTask, correctionTask, relationshipTask);

            var activity = activityTask.Result;
            var shared = sharedTask.Result;
            ActivityList.ItemsSource = activity.Select(x => new ActivityRow(
                x.OccurredAtUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), x.Category, x.Message)).ToArray();

            _sharedNarrativeVersion = shared.Version;
            _sharedNarrativeBaseText = shared.Text;
            SharedNarrativeBox.Text = shared.Text;
            SharedNarrativeVersionText.Text = $"v{shared.Version} • {shared.LastEditor}";
            SharedNarrativeConflictText.Text = string.Empty;

            RenderRecommendation(recommendationTask.Result);
            RenderDispatchState(incident, alarmTask.Result);
            RenderFireWatches(incident.Id);
            RenderIncidentOperations(commandTask.Result, groupTask.Result, mutualAidTask.Result);
            RenderOperationalMessages(messageTask.Result);
            RenderSiteIntelligence(siteTask.Result);
            RenderIncidentManagement(incident, correctionTask.Result, relationshipTask.Result);
        }
        catch (Exception ex)
        {
            ActionText.Text = ex.Message;
        }
    }

    private void RenderRecommendation(ResponseRecommendationDto recommendation)
    {
        RecommendationList.ItemsSource = recommendation.Units.Select(x => new RecommendationRow(
            x.UnitId,
            x.CallSign,
            x.UnitType,
            x.Score.ToString("0", CultureInfo.InvariantCulture),
            x.Reasons.Count == 0 ? "—" : string.Join(", ", x.Reasons))).ToArray();
        RecommendationWarningsText.Text = recommendation.Warnings.Count == 0 ? "No response-plan warnings." : string.Join("  •  ", recommendation.Warnings);
    }

    private void RenderDispatchState(IncidentDto incident, AdditionalAlarmDto[] alarms)
    {
        var alarmByAction = alarms.ToDictionary(x => x.DispatchActionId, x => x.AlarmNumber);
        var incidentActions = _dispatchActions.Where(x => x.IncidentId == incident.Id).OrderByDescending(x => x.CommittedAtUtc).ToArray();
        DispatchActionList.ItemsSource = incidentActions.Select(x => new DispatchActionRow(
            x.Id,
            x.State,
            x.CommittedAtUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            string.Join(", ", x.UnitIds.Select(id => _units.FirstOrDefault(u => u.Id == id)?.CallSign ?? id.ToString("N")[..6])),
            alarmByAction.TryGetValue(x.Id, out var alarm) ? $"Alarm {alarm}" : "Initial")).ToArray();

        var actionIds = incidentActions.Select(x => x.Id).ToHashSet();
        var deliveries = _dispatchDeliveries.Where(x => actionIds.Contains(x.DispatchActionId)).ToArray();
        var pending = deliveries.Count(x => x.State == "Pending");
        var failed = deliveries.Count(x => x.State == "Failed");
        DispatchInfoText.Text = $"{incidentActions.Length} dispatch action(s); {deliveries.Length} delivery path(s); pending={pending}; failed={failed}; additional alarms={alarms.Length}.";
    }

    private void RenderFireWatches(Guid incidentId)
    {
        FireWatchList.ItemsSource = _fireWatches
            .Where(x => x.IncidentId == incidentId)
            .OrderBy(x => x.Status == "Active" ? 0 : 1)
            .ThenByDescending(x => x.StartedAtUtc)
            .Select(x => new FireWatchRow(
                x.Id,
                x.UnitId,
                _units.FirstOrDefault(u => u.Id == x.UnitId)?.CallSign ?? x.UnitId.ToString("N")[..6],
                x.Status,
                $"Every {x.CheckIntervalMinutes}m • {x.Supervisor} • {x.Notes}"))
            .ToArray();
    }

    private void RenderIncidentOperations(CommandAssignmentDto[] commands, OperationalGroupDto[] groups, MutualAidRequestDto[] mutualAid)
    {
        CommandList.ItemsSource = commands
            .OrderByDescending(x => x.EffectiveAtUtc)
            .Select(x => new CommandRow(
                x.Id,
                x.CommandName,
                x.UnitId is Guid uid ? _units.FirstOrDefault(u => u.Id == uid)?.CallSign ?? uid.ToString("N")[..6] : "—",
                x.Person))
            .ToArray();
        GroupList.ItemsSource = groups
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new GroupRow(x.Id, x.Name, x.Type, x.Leader, x.Status))
            .ToArray();
        MutualAidList.ItemsSource = mutualAid
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => new MutualAidRow(x.Id, x.RequestedAgency, x.ResourceType, x.Quantity.ToString(CultureInfo.InvariantCulture), x.State, x.RequestedBy))
            .ToArray();
    }

    private void RenderOperationalMessages(OperationalMessageDto[] messages)
    {
        OperationalMessageList.ItemsSource = messages
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new OperationalMessageRow(
                x.Id,
                x.CreatedAtUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                x.Sender,
                x.Body,
                $"{x.Priority} • {x.Channel} • {(string.IsNullOrWhiteSpace(x.Recipient) ? "broadcast" : x.Recipient)} • {x.State}"))
            .ToArray();
    }

    private void RenderSiteIntelligence(IncidentSiteIntelligenceDto site)
    {
        _siteIntelligence = site;
        var coordinateState = site.SceneLatitude is double latitude && site.SceneLongitude is double longitude
            ? $"Scene point {latitude.ToString("0.00000", CultureInfo.InvariantCulture)}, {longitude.ToString("0.00000", CultureInfo.InvariantCulture)}"
            : "Scene coordinates not established";
        var warningText = site.Warnings.Count == 0 ? "No site-intelligence warnings." : string.Join("  •  ", site.Warnings);
        SiteIntelligenceSummaryText.Text = $"{coordinateState} • premises={site.Premises.Count} • published preplans={site.PublishedPreplans.Count} • CRG assets={site.CrgAssets.Count} • hydrants={site.Hydrants.Count} • active road restrictions={site.RoadRestrictions.Count}.  {warningText}";

        PremiseList.ItemsSource = site.Premises.Select(x => new PremiseRow(
            x.Id,
            $"{(string.IsNullOrWhiteSpace(x.Name) ? "Premise" : x.Name)} — {x.Address}",
            $"{x.Occupancy} • access: {ValueOrDash(x.AccessInformation)} • fire protection: {ValueOrDash(x.FireProtection)} • utilities: {ValueOrDash(x.UtilityShutoffs)} • hazards: {ValueOrDash(x.Hazards)}"))
            .ToArray();

        PreplanList.ItemsSource = site.PublishedPreplans.Select(x => new PreplanRow(
            x.Id,
            $"{x.Title} • v{x.Version} • {x.Status}",
            x.OperationalContent))
            .ToArray();

        var previousFloor = CrgFloorCombo.SelectedItem as string;
        var floors = AllCrgFloorsPrefix
            .Concat(site.CrgAssets.Select(x => string.IsNullOrWhiteSpace(x.Floor) ? "UNSPECIFIED" : x.Floor).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        CrgFloorCombo.ItemsSource = floors;
        CrgFloorCombo.SelectedItem = previousFloor is not null && floors.Contains(previousFloor, StringComparer.OrdinalIgnoreCase) ? floors.First(x => string.Equals(x, previousFloor, StringComparison.OrdinalIgnoreCase)) : floors.FirstOrDefault();
        RenderCrgFloor();

        HydrantList.ItemsSource = site.Hydrants.Select(x => new HydrantRow(
            x.Hydrant.Id,
            x.Hydrant.AssetId,
            FormatDistance(x.DistanceMeters),
            $"{x.Hydrant.Status} • {x.Hydrant.Address} • main {FormatNullable(x.Hydrant.MainSizeInches, "in")} • static {FormatNullable(x.Hydrant.StaticPsi, "psi")} • residual {FormatNullable(x.Hydrant.ResidualPsi, "psi")} • flow {FormatNullable(x.Hydrant.FlowGpm, "gpm")}"))
            .ToArray();

        RoadRestrictionList.ItemsSource = site.RoadRestrictions.Select(x => new RoadRestrictionRow(
            x.Restriction.Id,
            x.Restriction.Name,
            FormatDistance(x.DistanceMeters),
            $"{x.Restriction.Description} • radius {x.Restriction.RadiusMeters.ToString("0", CultureInfo.InvariantCulture)}m • until {(x.Restriction.EndsAtUtc?.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "manual end")}"))
            .ToArray();

        MapObjectList.ItemsSource = site.MapObjects.Select(x => new MapObjectRow(
            x.Id,
            $"{x.Kind} — {x.Name}",
            $"{x.VisibilityScope} • {x.GeometryGeoJson}"))
            .ToArray();
    }

    private void RenderCrgFloor()
    {
        if (_siteIntelligence is null)
        {
            CrgList.ItemsSource = Array.Empty<CrgRow>();
            return;
        }

        var floor = CrgFloorCombo.SelectedItem as string ?? "ALL";
        var assets = _siteIntelligence.CrgAssets
            .Where(x => string.Equals(floor, "ALL", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(floor, "UNSPECIFIED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(x.Floor))
                || string.Equals(x.Floor, floor, StringComparison.OrdinalIgnoreCase))
            .Select(x => new CrgRow(
                x.Id,
                $"Floor {ValueOrDash(x.Floor)} • {x.FileName}",
                $"{ValueOrDash(x.OverlayType)} • rev {x.Revision?.ToString(CultureInfo.InvariantCulture) ?? "—"} • rotation {x.Rotation?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"} • scale {x.Scale?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"} • opacity {x.Opacity?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—"} • source {ValueOrDash(x.Source)}"))
            .ToArray();
        CrgList.ItemsSource = assets;
    }

    private async Task RefreshSiteIntelligenceAsync()
    {
        var incident = SelectedIncident;
        if (incident is null) return;
        RenderSiteIntelligence(await _features.GetSiteIntelligenceAsync(incident.Id));
    }

    private void RenderIncidentManagement(IncidentDto incident, IncidentCorrectionDto[] corrections, IncidentRelationshipDto[] relationships)
    {
        CorrectionList.ItemsSource = corrections
            .OrderByDescending(x => x.CorrectedAtUtc)
            .Select(x => new CorrectionRow(
                x.Id,
                x.Field,
                $"'{x.OriginalValue}' → '{x.CorrectedValue}'",
                $"{x.Reason} • {x.Actor}",
                x.CorrectedAtUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
            .ToArray();

        RelationshipList.ItemsSource = relationships
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x =>
            {
                var sourceIsSelected = x.SourceIncidentId == incident.Id;
                var otherId = sourceIsSelected ? x.TargetIncidentId : x.SourceIncidentId;
                var other = _incidents.FirstOrDefault(i => i.Id == otherId);
                var display = other is null ? otherId.ToString("D") : $"{other.Number} — {other.Nature} — {other.Status}";
                return new RelationshipRow(x.Id, x.RelationshipType, display, sourceIsSelected ? "outbound →" : "← inbound");
            })
            .ToArray();

        var selectedRelationshipTargetId = SelectedRelatedIncident?.Id;
        var relationshipTargets = _incidents
            .Where(x => x.Id != incident.Id && x.AgencyId == incident.AgencyId)
            .OrderByDescending(x => x.Status != "Closed")
            .ThenByDescending(x => x.ReceivedAtUtc)
            .Select(ToIncidentTargetRow)
            .ToArray();
        RelatedIncidentCombo.ItemsSource = relationshipTargets;
        RelatedIncidentCombo.SelectedItem = relationshipTargets.FirstOrDefault(x => x.Id == selectedRelationshipTargetId) ?? relationshipTargets.FirstOrDefault();

        var selectedMergeTargetId = SelectedMergeTarget?.Id;
        var mergeTargets = _incidents
            .Where(x => x.Id != incident.Id && x.AgencyId == incident.AgencyId && x.Status != "Closed")
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Select(ToIncidentTargetRow)
            .ToArray();
        MergeTargetCombo.ItemsSource = mergeTargets;
        MergeTargetCombo.SelectedItem = mergeTargets.FirstOrDefault(x => x.Id == selectedMergeTargetId) ?? mergeTargets.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(SplitNatureBox.Text)) SplitNatureBox.Text = incident.Nature;
        if (string.IsNullOrWhiteSpace(SplitAddressBox.Text)) SplitAddressBox.Text = incident.Address;
    }

    private async Task RefreshIncidentManagementAsync()
    {
        var incident = SelectedIncident;
        if (incident is null) return;
        var correctionTask = _features.GetIncidentCorrectionsAsync(incident.Id);
        var relationshipTask = _features.GetIncidentRelationshipsAsync(incident.Id);
        await Task.WhenAll(correctionTask, relationshipTask);
        RenderIncidentManagement(incident, correctionTask.Result, relationshipTask.Result);
    }

    private static IncidentTargetRow ToIncidentTargetRow(IncidentDto incident)
        => new(incident.Id, incident.Version, incident.Status, $"{incident.Number} — {incident.Nature} — {incident.Address} — v{incident.Version} — {incident.Status}");

    private static string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    private static string FormatDistance(double? meters) => meters is null ? "distance unknown" : meters < 1_000 ? $"{meters.Value.ToString("0", CultureInfo.InvariantCulture)}m" : $"{(meters.Value / 1_000d).ToString("0.0", CultureInfo.InvariantCulture)}km";
    private static string FormatNullable(int? value, string unit) => value is int actual ? $"{actual.ToString(CultureInfo.InvariantCulture)} {unit}" : "—";

    private async Task RefreshIncidentOperationsAsync()
    {
        var incident = SelectedIncident;
        if (incident is null) return;
        var commandTask = _features.GetCommandAssignmentsAsync(incident.Id);
        var groupTask = _features.GetOperationalGroupsAsync(incident.Id);
        var mutualAidTask = _features.GetMutualAidAsync(incident.Id);
        var messageTask = _features.GetOperationalMessagesAsync(incident.AgencyId, incident.Id);
        await Task.WhenAll(commandTask, groupTask, mutualAidTask, messageTask);
        RenderIncidentOperations(commandTask.Result, groupTask.Result, mutualAidTask.Result);
        RenderOperationalMessages(messageTask.Result);
    }

    private async Task RefreshRecommendationAsync()
    {
        var incident = SelectedIncident;
        if (incident is null) return;
        var recommendation = await _features.GetResponseRecommendationAsync(new ResponseRecommendationRequest(incident.AgencyId, incident.Id, incident.Nature, incident.Priority, incident.StationId, []));
        RenderRecommendation(recommendation);
    }

    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await RefreshAllAsync(SelectedIncident?.Id);
    private async void IncidentSelectionChanged(object? sender, SelectionChangedEventArgs e) => await RefreshIncidentAsync();
    private async void SearchKeyUp(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) await RefreshAllAsync(); }
    private void CrgFloorSelectionChanged(object? sender, SelectionChangedEventArgs e) => RenderCrgFloor();

    private async void CreateMapPointClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        if (!TryCoordinates(MapLatitudeBox.Text, MapLongitudeBox.Text, out var latitude, out var longitude)) { ActionText.Text = "Enter valid latitude (-90..90) and longitude (-180..180)."; return; }
        if (!TryPositiveMinutes(MapDurationBox.Text, out var durationMinutes)) { ActionText.Text = "Map point duration must be a positive number of minutes."; return; }

        var kind = string.IsNullOrWhiteSpace(MapKindBox.Text) ? "IncidentLocation" : MapKindBox.Text.Trim();
        var name = string.IsNullOrWhiteSpace(MapNameBox.Text) ? "Incident scene" : MapNameBox.Text.Trim();
        var geoJson = FormattableString.Invariant($"{{\"type\":\"Point\",\"coordinates\":[{longitude:R},{latitude:R}]}}");
        try
        {
            var item = await _features.CreateMapObjectAsync(new CreateMapObjectRequest(incident.AgencyId, incident.Id, kind, name, geoJson, "Incident", DateTimeOffset.UtcNow.AddMinutes(durationMinutes)));
            ActionText.Text = $"Map point {item.Kind} — {item.Name} added. Site proximity recalculated.";
            await RefreshSiteIntelligenceAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CreateRoadRestrictionClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        if (string.IsNullOrWhiteSpace(RoadNameBox.Text)) { ActionText.Text = "Road restriction name is required."; return; }
        if (!TryCoordinates(RoadLatitudeBox.Text, RoadLongitudeBox.Text, out var latitude, out var longitude)) { ActionText.Text = "Enter valid road-restriction latitude and longitude."; return; }
        if (!double.TryParse(RoadRadiusBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) || radius <= 0) { ActionText.Text = "Road restriction radius must be a positive number of meters."; return; }
        if (!TryPositiveMinutes(RoadDurationBox.Text, out var durationMinutes)) { ActionText.Text = "Road restriction duration must be a positive number of minutes."; return; }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var item = await _features.CreateRoadRestrictionAsync(new CreateRoadRestrictionRequest(
                incident.AgencyId,
                RoadNameBox.Text.Trim(),
                RoadDescriptionBox.Text?.Trim() ?? string.Empty,
                latitude,
                longitude,
                radius,
                now,
                now.AddMinutes(durationMinutes)));
            ActionText.Text = $"Road restriction {item.Name} created for {durationMinutes.ToString(CultureInfo.InvariantCulture)} minutes.";
            await RefreshSiteIntelligenceAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void EndRoadRestrictionClicked(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedRoadRestriction;
        if (selected is null) { ActionText.Text = "Select an active road restriction."; return; }
        try
        {
            var item = await _features.EndRoadRestrictionAsync(selected.Id, new NotesActionRequest(CadActor, "Ended from CAD site intelligence"));
            ActionText.Text = $"Road restriction {item.Name} ended.";
            await RefreshSiteIntelligenceAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private static bool TryCoordinates(string? latitudeText, string? longitudeText, out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        if (!double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)) return false;
        if (!double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)) return false;
        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static bool TryPositiveMinutes(string? value, out int minutes)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) && minutes is > 0 and <= 10_080;

    private async void CorrectIncidentClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        var field = (CorrectionFieldCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Nature";
        var correctedValue = CorrectionValueBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(CorrectionReasonBox.Text)) { ActionText.Text = "A correction reason is required for the audit trail."; return; }
        if (field is "Nature" or "Priority" or "Address" && string.IsNullOrWhiteSpace(correctedValue)) { ActionText.Text = $"{field} cannot be blank."; return; }

        try
        {
            var correction = await _features.CorrectIncidentAsync(incident.Id, new CorrectIncidentFieldRequest(
                incident.Version,
                field,
                correctedValue,
                CorrectionReasonBox.Text.Trim(),
                CadActor,
                Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Audited correction applied: {correction.Field} → '{correction.CorrectedValue}'.";
            CorrectionValueBox.Text = string.Empty;
            CorrectionReasonBox.Text = string.Empty;
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void AddRelationshipClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        var target = SelectedRelatedIncident;
        if (incident is null || target is null) { ActionText.Text = "Select a source incident and related incident."; return; }
        var relationshipType = (RelationshipTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Related";
        try
        {
            var relationship = await _features.CreateIncidentRelationshipAsync(incident.Id, new CreateIncidentRelationshipRequest(incident.AgencyId, target.Id, relationshipType, CadActor));
            ActionText.Text = $"Relationship recorded: {incident.Number} {relationship.RelationshipType} {target.Display}.";
            await RefreshIncidentManagementAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void MergeIncidentClicked(object? sender, RoutedEventArgs e)
    {
        var source = SelectedIncident;
        var target = SelectedMergeTarget;
        if (source is null || target is null) { ActionText.Text = "Select a source incident and open merge target."; return; }
        if (source.Status == "Closed") { ActionText.Text = "A closed source incident cannot be merged through the active merge workflow."; return; }
        if (string.IsNullOrWhiteSpace(MergeReasonBox.Text)) { ActionText.Text = "Merge reason is required."; return; }
        if (!string.Equals(MergeConfirmationBox.Text?.Trim(), source.Number, StringComparison.OrdinalIgnoreCase))
        {
            ActionText.Text = $"To confirm the merge, type the source incident number exactly: {source.Number}";
            return;
        }

        try
        {
            var closed = await _features.MergeIncidentsAsync(source.Id, new MergeIncidentsRequest(
                target.Id,
                source.Version,
                target.Version,
                MergeReasonBox.Text.Trim(),
                CadActor,
                Guid.NewGuid().ToString("N")));
            ActionText.Text = $"{closed.Number} merged into {target.Display}; source closed and assigned units transferred.";
            MergeConfirmationBox.Text = string.Empty;
            MergeReasonBox.Text = string.Empty;
            await RefreshAllAsync(target.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void SplitIncidentClicked(object? sender, RoutedEventArgs e)
    {
        var source = SelectedIncident;
        if (source is null) { ActionText.Text = "Select a source incident."; return; }
        if (string.IsNullOrWhiteSpace(SplitNatureBox.Text) || string.IsNullOrWhiteSpace(SplitAddressBox.Text)) { ActionText.Text = "Split incident nature and address are required."; return; }
        var priority = (SplitPriorityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? source.Priority;
        try
        {
            var created = await _features.SplitIncidentAsync(source.Id, new SplitIncidentRequest(
                source.Version,
                SplitNatureBox.Text.Trim(),
                priority,
                SplitAddressBox.Text.Trim(),
                SplitNotesBox.Text?.Trim() ?? string.Empty,
                CadActor,
                Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Split incident created: {created.Number}. Source {source.Number} remains preserved.";
            SplitNotesBox.Text = string.Empty;
            await RefreshAllAsync(created.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CheckDuplicatesClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AddressBox.Text) && string.IsNullOrWhiteSpace(CallerPhoneBox.Text))
        {
            DuplicateText.Text = "Enter an address or callback number to check for likely duplicates.";
            return;
        }

        try
        {
            var candidates = await _features.CheckDuplicatesAsync(new DuplicateCheckRequest(
                OperationalStoreConstants.DefaultAgencyId,
                NatureBox.Text?.Trim() ?? string.Empty,
                AddressBox.Text?.Trim() ?? string.Empty,
                CallerPhoneBox.Text?.Trim() ?? string.Empty,
                null,
                null));
            DuplicateText.Text = candidates.Length == 0
                ? "Duplicate check: no likely active matches."
                : "Possible duplicate(s): " + string.Join(" | ", candidates.Take(3).Select(x => $"{x.IncidentNumber} {x.Score:P0} — {x.Reason}"));
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CreateIncidentClicked(object? sender, RoutedEventArgs e)
    {
        if (StationCombo.SelectedItem is not StationRow station) { ActionText.Text = "Select a station."; return; }
        if (string.IsNullOrWhiteSpace(NatureBox.Text) || string.IsNullOrWhiteSpace(AddressBox.Text)) { ActionText.Text = "Nature and address are required."; return; }
        var priority = (PriorityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Routine";
        try
        {
            var nature = NatureBox.Text.Trim();
            var address = AddressBox.Text.Trim();
            var caller = CallerBox.Text?.Trim() ?? string.Empty;
            var phone = CallerPhoneBox.Text?.Trim() ?? string.Empty;
            var request = new CreateIncidentRequest(OperationalStoreConstants.DefaultAgencyId, station.Id, nature, priority, address, "", "", caller, phone, "", Guid.NewGuid().ToString("N"));
            var created = await _core.CreateIncidentAsync(request);
            var callRecorded = true;
            try
            {
                await _features.CreateCallAsync(new CreateCallRecordRequest(
                    created.AgencyId,
                    "CADManual",
                    $"CAD-{Guid.NewGuid():N}",
                    caller,
                    phone,
                    address,
                    nature,
                    created.Id));
            }
            catch (Exception ex)
            {
                callRecorded = false;
                ActionText.Text = $"Created {created.Number}, but the manual call record failed: {ex.Message}";
            }

            if (callRecorded) ActionText.Text = $"Created {created.Number} and linked the manual call-intake record.";
            NatureBox.Text = AddressBox.Text = CallerBox.Text = CallerPhoneBox.Text = string.Empty;
            DuplicateText.Text = string.Empty;
            await RefreshAllAsync(created.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void AssignClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        var selected = SelectedUnits;
        if (incident is null || selected.Length == 0) { ActionText.Text = "Select an incident and at least one unit."; return; }
        try
        {
            var updated = incident;
            foreach (var unit in selected)
                updated = await _core.AssignUnitAsync(incident.Id, new AssignUnitRequest(unit.Id, updated.Version, true, Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Assigned {string.Join(", ", selected.Select(x => x.CallSign))} to {updated.Number}.";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void SetStatusClicked(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedUnits;
        if (selected.Length == 0) { ActionText.Text = "Select at least one unit."; return; }
        var status = (UnitStatusCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Available";
        try
        {
            foreach (var unit in selected)
                await _core.SetUnitStatusAsync(unit.Id, new SetUnitStatusRequest(status, Guid.NewGuid().ToString("N")));
            ActionText.Text = $"{string.Join(", ", selected.Select(x => x.CallSign))} → {status}.";
            await RefreshAllAsync(SelectedIncident?.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void DispatchClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        var assigned = _units.Where(x => x.IncidentId == incident.Id).Select(x => x.Id).ToArray();
        if (assigned.Length == 0) { ActionText.Text = "Assign at least one unit before Dispatch Commit."; return; }
        try
        {
            var result = await _features.CommitDispatchAsync(incident.Id, new CommitDispatchRequest(incident.Version, assigned, ["StationDisplay", "RunSheet", "StationAlert", "Mobile"], Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Dispatch committed: {result.Action.Id:D}; {result.Deliveries.Count} delivery paths.";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void AdditionalAlarmClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        var selected = SelectedUnits;
        if (incident is null || selected.Length == 0) { ActionText.Text = "Select the incident and the units for the additional alarm."; return; }
        try
        {
            var alarm = await _features.CommitAdditionalAlarmAsync(incident.Id, new CommitAdditionalAlarmRequest(
                incident.Version,
                selected.Select(x => x.Id).ToArray(),
                ["StationDisplay", "RunSheet", "StationAlert", "Mobile"],
                CadActor,
                Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Alarm {alarm.AlarmNumber} committed with {alarm.UnitIds.Count} unit(s).";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CancelDispatchClicked(object? sender, RoutedEventArgs e)
    {
        var action = SelectedDispatchAction;
        var incident = SelectedIncident;
        if (action is null || incident is null) { ActionText.Text = "Select a dispatch action to cancel."; return; }
        try
        {
            var updated = await _features.CancelDispatchAsync(action.Id, new CancelDispatchRequest("Cancelled from CAD dispatcher workspace", CadActor, Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Dispatch {updated.Id:D} cancellation queued.";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void RecommendClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (SelectedIncident is null) { ActionText.Text = "Select an incident."; return; }
            await RefreshRecommendationAsync();
            ActionText.Text = "Response recommendation refreshed from the published response-plan rules.";
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void SaveSharedNarrativeClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) return;
        var localText = SharedNarrativeBox.Text ?? string.Empty;
        var patch = NarrativePatchBuilder.Build(_sharedNarrativeBaseText, localText);
        if (!patch.HasChanges)
        {
            ActionText.Text = "Shared narrative has no unsaved changes.";
            return;
        }

        try
        {
            var updated = await _features.PatchCollaborativeNarrativeAsync(incident.Id, new CollaborativeNarrativePatchRequest(
                _sharedNarrativeVersion,
                patch.Start,
                patch.DeleteCount,
                patch.InsertText,
                CadActor,
                Guid.NewGuid().ToString("N")));
            _sharedNarrativeVersion = updated.Version;
            _sharedNarrativeBaseText = updated.Text;
            SharedNarrativeBox.Text = updated.Text;
            SharedNarrativeVersionText.Text = $"v{updated.Version} • {updated.LastEditor}";
            SharedNarrativeConflictText.Text = string.Empty;
            ActionText.Text = updated.Text == localText ? "Shared narrative saved." : "Shared narrative saved and merged with a non-overlapping dispatcher edit.";
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("409", StringComparison.Ordinal))
        {
            SharedNarrativeConflictText.Text = "Overlapping edit conflict. Your unsaved text is preserved. Reload Latest only if you intend to discard it and edit against the newest server version.";
            ActionText.Text = "Shared narrative conflict — local text preserved; nothing was overwritten.";
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void ReloadSharedNarrativeClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) return;
        try
        {
            var shared = await _features.GetCollaborativeNarrativeAsync(incident.Id);
            _sharedNarrativeVersion = shared.Version;
            _sharedNarrativeBaseText = shared.Text;
            SharedNarrativeBox.Text = shared.Text;
            SharedNarrativeVersionText.Text = $"v{shared.Version} • {shared.LastEditor}";
            SharedNarrativeConflictText.Text = string.Empty;
            ActionText.Text = "Latest shared narrative loaded; previous local edits were discarded by explicit operator action.";
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void AddNarrativeClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null || string.IsNullOrWhiteSpace(NarrativeBox.Text)) return;
        try
        {
            using var response = await SharedHttp.PostAsJsonAsync($"api/incidents/{incident.Id:D}/narrative", new AddNarrativeRequest(NarrativeBox.Text.Trim(), Guid.NewGuid().ToString("N")));
            response.EnsureSuccessStatusCode();
            NarrativeBox.Text = string.Empty;
            await RefreshIncidentAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void StartFireWatchClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        var unit = SelectedUnit;
        if (incident is null || unit is null) { ActionText.Text = "Select an incident and unit for Fire Watch."; return; }
        try
        {
            var notes = FireWatchNotesBox.Text?.Trim() ?? string.Empty;
            var item = await _features.CreateFireWatchAsync(new CreateFireWatchRequest(incident.AgencyId, incident.Id, unit.Id, CadActor, null, 15, notes));
            ActionText.Text = $"Fire Watch started for {unit.CallSign}; checks every {item.CheckIntervalMinutes} minutes.";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void FireWatchCheckClicked(object? sender, RoutedEventArgs e)
    {
        var fireWatch = SelectedFireWatch;
        if (fireWatch is null) { ActionText.Text = "Select a Fire Watch record."; return; }
        try
        {
            var notes = FireWatchNotesBox.Text?.Trim() ?? string.Empty;
            var check = await _features.AddFireWatchCheckAsync(fireWatch.Id, new NotesActionRequest(CadActor, notes));
            ActionText.Text = $"Fire Watch check logged at {check.CheckedAtUtc.LocalDateTime:HH:mm:ss}.";
            FireWatchNotesBox.Text = string.Empty;
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void EndFireWatchClicked(object? sender, RoutedEventArgs e)
    {
        var fireWatch = SelectedFireWatch;
        var incident = SelectedIncident;
        if (fireWatch is null || incident is null) { ActionText.Text = "Select a Fire Watch record."; return; }
        try
        {
            await _features.EndFireWatchAsync(fireWatch.Id, new NotesActionRequest(CadActor, FireWatchNotesBox.Text));
            ActionText.Text = $"Fire Watch for {fireWatch.Unit} ended.";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void StartRelocationClicked(object? sender, RoutedEventArgs e)
    {
        var unit = SelectedUnit;
        if (unit is null || DestinationStationCombo.SelectedItem is not StationRow station) { ActionText.Text = "Select a unit and destination station."; return; }
        if (string.IsNullOrWhiteSpace(RelocationReasonBox.Text)) { ActionText.Text = "Relocation reason is required."; return; }
        try
        {
            var item = await _features.CreateRelocationAsync(new CreateRelocationRequest(OperationalStoreConstants.DefaultAgencyId, unit.Id, station.Id, RelocationReasonBox.Text.Trim()));
            ActionText.Text = $"Relocation started for {unit.CallSign} to {station.Display}.";
            RelocationReasonBox.Text = string.Empty;
            await RefreshAllAsync(SelectedIncident?.Id);
            RelocationList.SelectedItem = (RelocationList.ItemsSource as IEnumerable<RelocationRow>)?.FirstOrDefault(x => x.Id == item.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void EndRelocationClicked(object? sender, RoutedEventArgs e)
    {
        var relocation = SelectedRelocation;
        if (relocation is null) { ActionText.Text = "Select a relocation record."; return; }
        try
        {
            await _features.EndRelocationAsync(relocation.Id, new NotesActionRequest(CadActor, null));
            ActionText.Text = $"Relocation ended for {relocation.Unit}.";
            await RefreshAllAsync(SelectedIncident?.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async Task ActOnSelectedAlertAsync(string action)
    {
        var alert = SelectedAlert;
        if (alert is null) { ActionText.Text = "Select an alert."; return; }
        try
        {
            var updated = await _features.ActOnAlertAsync(alert.Id, action, new AlertActionRequest(CadActor, $"{action} from CAD dispatcher workspace"));
            ActionText.Text = $"Alert {updated.State}: {updated.Summary}";
            await RefreshAllAsync(SelectedIncident?.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void AcknowledgeAlertClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedAlertAsync("acknowledge");
    private async void ClaimAlertClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedAlertAsync("claim");
    private async void EscalateAlertClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedAlertAsync("escalate");
    private async void ResolveAlertClicked(object? sender, RoutedEventArgs e) => await ActOnSelectedAlertAsync("resolve");

    private async void SetCommandClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        if (string.IsNullOrWhiteSpace(CommandNameBox.Text) || string.IsNullOrWhiteSpace(CommandPersonBox.Text)) { ActionText.Text = "Command role and person are required."; return; }
        try
        {
            var item = await _features.SetCommandAsync(new SetCommandRequest(incident.AgencyId, incident.Id, CommandNameBox.Text.Trim(), SelectedUnit?.Id, CommandPersonBox.Text.Trim(), CadActor));
            ActionText.Text = $"{item.CommandName} assigned to {item.Person}.";
            await RefreshIncidentOperationsAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CreateGroupClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        if (string.IsNullOrWhiteSpace(GroupNameBox.Text)) { ActionText.Text = "Group/division name is required."; return; }
        try
        {
            var type = string.IsNullOrWhiteSpace(GroupTypeBox.Text) ? "Group" : GroupTypeBox.Text.Trim();
            var leader = string.IsNullOrWhiteSpace(GroupLeaderBox.Text) ? CadActor : GroupLeaderBox.Text.Trim();
            var members = (GroupMembersBox.Text ?? string.Empty).Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var item = await _features.CreateOperationalGroupAsync(new CreateOperationalGroupRequest(incident.AgencyId, incident.Id, GroupNameBox.Text.Trim(), type, leader, "", members));
            ActionText.Text = $"{item.Type} {item.Name} created.";
            GroupNameBox.Text = string.Empty;
            GroupMembersBox.Text = string.Empty;
            await RefreshIncidentOperationsAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CreateMutualAidClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        if (string.IsNullOrWhiteSpace(MutualAidAgencyBox.Text) || string.IsNullOrWhiteSpace(MutualAidResourceBox.Text)) { ActionText.Text = "Mutual-aid agency and resource are required."; return; }
        if (!int.TryParse(MutualAidQuantityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity < 1) { ActionText.Text = "Mutual-aid quantity must be at least 1."; return; }
        try
        {
            var item = await _features.CreateMutualAidAsync(new CreateMutualAidRequest(incident.AgencyId, incident.Id, MutualAidAgencyBox.Text.Trim(), MutualAidResourceBox.Text.Trim(), quantity, CadActor));
            ActionText.Text = $"Mutual aid requested from {item.RequestedAgency}: {item.Quantity} {item.ResourceType}.";
            await RefreshIncidentOperationsAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void UpdateMutualAidClicked(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedMutualAid;
        if (selected is null) { ActionText.Text = "Select a mutual-aid request."; return; }
        var state = (MutualAidStateCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Accepted";
        try
        {
            var item = await _features.UpdateMutualAidAsync(selected.Id, state);
            ActionText.Text = $"Mutual aid {item.RequestedAgency} → {item.State}.";
            await RefreshIncidentOperationsAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void SendOperationalMessageClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        if (string.IsNullOrWhiteSpace(MessageBodyBox.Text)) { ActionText.Text = "Message body is required."; return; }
        var channel = string.IsNullOrWhiteSpace(MessageChannelBox.Text) ? "Command" : MessageChannelBox.Text.Trim();
        var recipient = string.IsNullOrWhiteSpace(MessageRecipientBox.Text) ? null : MessageRecipientBox.Text.Trim();
        var priority = (MessagePriorityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Routine";
        try
        {
            var receipts = recipient is null ? Array.Empty<string>() : new[] { recipient };
            var item = await _features.SendOperationalMessageAsync(new CreateOperationalMessageRequest(incident.AgencyId, incident.Id, channel, CadActor, recipient, priority, "Operational", MessageBodyBox.Text.Trim(), "{}", receipts));
            ActionText.Text = $"Operational message sent on {item.Channel}.";
            MessageBodyBox.Text = string.Empty;
            await RefreshIncidentOperationsAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void PromoteMessageClicked(object? sender, RoutedEventArgs e)
    {
        var message = SelectedOperationalMessage;
        if (message is null) { ActionText.Text = "Select an operational message."; return; }
        try
        {
            var activity = await _features.PromoteOperationalMessageToNarrativeAsync(message.Id, new PromoteMessageToNarrativeRequest(CadActor, "Operational message", Guid.NewGuid().ToString("N")));
            ActionText.Text = $"Message promoted to immutable incident narrative at {activity.OccurredAtUtc.LocalDateTime:HH:mm:ss}.";
            await RefreshIncidentAsync();
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void CloseClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident; if (incident is null || incident.Status == "Closed") return;
        try { await _core.CloseIncidentAsync(incident.Id, new CloseIncidentRequest(incident.Version, "Closed from CAD", Guid.NewGuid().ToString("N"))); await RefreshAllAsync(incident.Id); }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void ReopenClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident; if (incident is null || incident.Status != "Closed") return;
        try { await _core.ReopenIncidentAsync(incident.Id, new ReopenIncidentRequest("Reopened from CAD", Guid.NewGuid().ToString("N"))); await RefreshAllAsync(incident.Id); }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void ReplayClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident;
        if (incident is null) { ActionText.Text = "Select an incident."; return; }
        var replay = new ReplayWindow(_core, incident);
        await replay.ShowDialog(this);
    }

    private async void MaydayClicked(object? sender, RoutedEventArgs e)
    {
        var incident = SelectedIncident; if (incident is null) return;
        try
        {
            using var response = await SharedHttp.PostAsJsonAsync("api/mayday", new DeclareMaydayRequest(incident.AgencyId, incident.Id, null, "Unknown member", incident.Address, "", CadActor));
            response.EnsureSuccessStatusCode();
            ActionText.Text = "MAYDAY declared and critical alert raised.";
            await RefreshAllAsync(incident.Id);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }
}

public sealed record StationRow(Guid Id, string Display);
public sealed record UnitRow(Guid Id, string CallSign, string Status, string UnitType, Guid? IncidentId, string Assignment);
public sealed record ActivityRow(string Time, string Category, string Message);
public sealed record RecommendationRow(Guid UnitId, string CallSign, string UnitType, string Score, string Reasons);
public sealed record DispatchActionRow(Guid Id, string State, string Time, string Units, string Alarm);
public sealed record FireWatchRow(Guid Id, Guid UnitId, string Unit, string Status, string Detail);
public sealed record RelocationRow(Guid Id, Guid UnitId, string Unit, string Destination, string Status, string Reason);
public sealed record AlertRow(Guid Id, string SeverityState, string Summary, string Claimed);
public sealed record CommandRow(Guid Id, string Name, string Unit, string Person);
public sealed record GroupRow(Guid Id, string Name, string Type, string Leader, string Status);
public sealed record MutualAidRow(Guid Id, string Agency, string Resource, string Quantity, string State, string RequestedBy);
public sealed record OperationalMessageRow(Guid Id, string Time, string Sender, string Body, string Meta);
public sealed record PremiseRow(Guid Id, string NameAddress, string Detail);
public sealed record PreplanRow(Guid Id, string Header, string Content);
public sealed record CrgRow(Guid Id, string Header, string Meta);
public sealed record HydrantRow(Guid Id, string Asset, string Distance, string Detail);
public sealed record RoadRestrictionRow(Guid Id, string Name, string Distance, string Detail);
public sealed record MapObjectRow(Guid Id, string Header, string Geometry);
public sealed record IncidentTargetRow(Guid Id, long Version, string Status, string Display);
public sealed record CorrectionRow(Guid Id, string Field, string Change, string Reason, string Time);
public sealed record RelationshipRow(Guid Id, string Type, string OtherIncident, string Direction);

internal static class OperationalStoreConstants
{
    public static readonly Guid DefaultAgencyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
}
