using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;

namespace DISRoute.Station;

public sealed partial class MainWindow : Window
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private readonly FeatureApiClient _features;
    private readonly Guid _stationId;
    private StationSnapshotDto? _snapshot;
    private IReadOnlyList<StationDeliveryPathDto> _deliveryPaths = [];

    public MainWindow()
    {
        InitializeComponent();
        _stationId = Guid.TryParse(Environment.GetEnvironmentVariable("DISROUTE_STATION_ID"), out var id) ? id : Guid.Parse("11111111-1111-1111-1111-111111111111");
        _features = new FeatureApiClient(SharedHttp);
        Opened += async (_, _) => await RefreshAsync();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += async (_, _) => await RefreshAsync(false);
        timer.Start();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        clock.Start();
    }

    private static HttpClient CreateHttpClient() => ClientHttpFactory.Create(TimeSpan.FromSeconds(12));

    private async Task RefreshAsync(bool announce = true)
    {
        var selectedIncidentId = (IncidentList.SelectedItem as IncidentRow)?.Id;
        var selectedDeliveryId = (DeliveryList.SelectedItem as DeliveryRow)?.Id;
        var selectedRunSheetId = (RunSheetList.SelectedItem as RunSheetRow)?.Id;
        var selectedPathId = (DeliveryPathList.SelectedItem as DeliveryPathRow)?.Id;

        try
        {
            var stationTask = _features.GetStationSnapshotAsync(_stationId);
            var pathTask = _features.GetStationDeliveryPathsAsync(_stationId);
            var preplanTask = _features.GetPreplansAsync();
            var crgTask = _features.GetCrgAssetsAsync();
            var cameraTask = _features.GetCamerasAsync();
            var radioTask = _features.GetRadioStreamsAsync();
            await Task.WhenAll(stationTask, pathTask, preplanTask, crgTask, cameraTask, radioTask);
            _snapshot = stationTask.Result;
            _deliveryPaths = pathTask.Result;
            StationTitle.Text = _stationId.ToString("D");

            var incidentRows = _snapshot.Incidents
                .OrderByDescending(x => x.ReceivedAtUtc)
                .Select(x => new IncidentRow(x.Id, $"{x.Number} — {x.Nature}", x.Address, $"{x.Priority} • {x.Status} • {x.Notes}"))
                .ToArray();
            IncidentList.ItemsSource = incidentRows;
            IncidentList.SelectedItem = incidentRows.FirstOrDefault(x => x.Id == selectedIncidentId) ?? incidentRows.FirstOrDefault();

            UnitList.ItemsSource = _snapshot.Units.Select(x => $"{x.CallSign,-8} {x.Status,-14} {x.UnitType,-12} {(x.IncidentId is Guid iid ? _snapshot.Incidents.FirstOrDefault(i => i.Id == iid)?.Number : "")}").ToArray();
            PreplanList.ItemsSource = preplanTask.Result.Where(x => x.Status == "Published").Select(x => $"{x.Title}  v{x.Version}  {x.OperationalContent}").ToArray();
            CrgList.ItemsSource = crgTask.Result.Select(x => $"{x.FileName}  floor={x.Floor ?? "—"}  overlay={x.OverlayType ?? "—"}  rev={x.Revision?.ToString(CultureInfo.InvariantCulture) ?? "—"}").ToArray();
            CameraList.ItemsSource = cameraTask.Result.Where(x => x.StationId is null || x.StationId == _stationId).Select(x => $"{x.Name}  {x.Provider}  [{x.State}]  {x.Uri}").ToArray();
            RadioList.ItemsSource = radioTask.Result.Select(x => $"{x.Name}  {x.Codec}  [{x.State}]  {x.IcecastUri}").ToArray();
            ConnectionText.Text = "CONNECTED";
            if (announce) ActionText.Text = $"Updated {DateTime.Now:T}.";
            RefreshIncidentDetail(selectedDeliveryId, selectedRunSheetId, selectedPathId);
        }
        catch (Exception ex)
        {
            ConnectionText.Text = "DEGRADED";
            ActionText.Text = ex.Message;
        }
    }

    private void RefreshIncidentDetail(Guid? selectedDeliveryId = null, Guid? selectedRunSheetId = null, Guid? selectedPathId = null)
    {
        if (_snapshot is null || IncidentList.SelectedItem is not IncidentRow row)
        {
            IncidentSummaryText.Text = "Select an incident.";
            DeliveryList.ItemsSource = Array.Empty<DeliveryRow>();
            DeliveryPathList.ItemsSource = Array.Empty<DeliveryPathRow>();
            RunSheetList.ItemsSource = Array.Empty<RunSheetRow>();
            RunSheetContentText.Text = string.Empty;
            return;
        }

        var incident = _snapshot.Incidents.First(x => x.Id == row.Id);
        var assigned = _snapshot.Units.Where(x => x.IncidentId == incident.Id).Select(x => x.CallSign).OrderBy(x => x).ToArray();
        IncidentSummaryText.Text = $"{incident.Number} • {incident.Nature} • {incident.Priority} • {incident.Status}\n{incident.Address}\nUnits: {(assigned.Length == 0 ? "none" : string.Join(", ", assigned))}\nNotes: {incident.Notes}";

        var deliveryRows = _snapshot.Deliveries
            .Where(x => x.IncidentId == row.Id)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new DeliveryRow(x.Id, x.DispatchActionId,
                $"Updated {x.UpdatedAtUtc.LocalDateTime:G} • attempt {x.Attempt}",
                $"Display={x.DisplayState}  Alert={x.AlertState}  Print={x.PrintState}  Ack={x.AckState}"))
            .ToArray();
        DeliveryList.ItemsSource = deliveryRows;
        DeliveryList.SelectedItem = deliveryRows.FirstOrDefault(x => x.Id == selectedDeliveryId) ?? deliveryRows.FirstOrDefault();

        var runSheetRows = _snapshot.RunSheets
            .Where(x => x.IncidentId == row.Id)
            .OrderByDescending(x => x.Revision)
            .Select(x => new RunSheetRow(x.Id, x.Revision, x.Status, x.Content,
                $"Revision {x.Revision}  [{x.Status}]  generated {x.GeneratedAtUtc.LocalDateTime:G}{(x.PrintedAtUtc is DateTimeOffset printed ? $"  printed {printed.LocalDateTime:G}" : string.Empty)}"))
            .ToArray();
        RunSheetList.ItemsSource = runSheetRows;
        RunSheetList.SelectedItem = runSheetRows.FirstOrDefault(x => x.Id == selectedRunSheetId) ?? runSheetRows.FirstOrDefault();
        RefreshDeliveryPaths(selectedPathId);
        RefreshRunSheetContent();
    }

    private void RefreshDeliveryPaths(Guid? selectedPathId = null)
    {
        if (IncidentList.SelectedItem is not IncidentRow incidentRow)
        {
            DeliveryPathList.ItemsSource = Array.Empty<DeliveryPathRow>();
            return;
        }

        var deliveryId = (DeliveryList.SelectedItem as DeliveryRow)?.Id;
        var rows = _deliveryPaths
            .Where(x => x.IncidentId == incidentRow.Id && (deliveryId is null || x.StationDeliveryId == deliveryId))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new DeliveryPathRow(x.Id, x.StationDeliveryId, x.Provider, x.State,
                $"{x.Provider} → {x.Destination}  [{x.State}]",
                $"Attempts={x.Attempts} • updated {x.UpdatedAtUtc.LocalDateTime:G}{(string.IsNullOrWhiteSpace(x.LastError) ? string.Empty : $" • ERROR: {x.LastError}")}"))
            .ToArray();
        DeliveryPathList.ItemsSource = rows;
        DeliveryPathList.SelectedItem = rows.FirstOrDefault(x => x.Id == selectedPathId) ?? rows.FirstOrDefault();
    }

    private void RefreshRunSheetContent()
    {
        if (RunSheetList.SelectedItem is RunSheetRow row)
        {
            RunSheetContentText.Text = row.Content;
            return;
        }
        RunSheetContentText.Text = string.Empty;
    }

    private async void RefreshClicked(object? sender, RoutedEventArgs e) => await RefreshAsync();
    private void IncidentSelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshIncidentDetail();
    private void DeliverySelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshDeliveryPaths();
    private void RunSheetSelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshRunSheetContent();

    private async void AcknowledgeClicked(object? sender, RoutedEventArgs e)
    {
        if (DeliveryList.SelectedItem is not DeliveryRow row)
        {
            ActionText.Text = "Select a station delivery first.";
            return;
        }
        try
        {
            var result = await _features.AcknowledgeStationDeliveryAsync(row.Id, new StationAcknowledgeRequest(Environment.MachineName, Environment.UserName));
            ActionText.Text = $"Station delivery acknowledged at {result.UpdatedAtUtc.LocalDateTime:T}.";
            await RefreshAsync(false);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void RetryDeliveryClicked(object? sender, RoutedEventArgs e)
    {
        if (DeliveryPathList.SelectedItem is not DeliveryPathRow row)
        {
            ActionText.Text = "Select a failed delivery path first.";
            return;
        }
        if (!string.Equals(row.State, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            ActionText.Text = $"{row.Provider} is {row.State}; only failed paths may be retried.";
            return;
        }
        try
        {
            var result = await _features.RetryDispatchDeliveryAsync(row.Id, new RetryDeliveryRequest(Environment.UserName));
            ActionText.Text = $"{result.Provider} retry queued. Attempt {result.Attempts}.";
            await RefreshAsync(false);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }

    private async void PrintRunSheetClicked(object? sender, RoutedEventArgs e)
    {
        if (RunSheetList.SelectedItem is not RunSheetRow row)
        {
            ActionText.Text = "Select a run sheet first.";
            return;
        }
        try
        {
            PrintResultText.Text = "Printing…";
            var result = await _features.PrintRunSheetAsync(row.Id, PrinterKeyText.Text);
            PrintResultText.Text = $"{result.State}{(string.IsNullOrWhiteSpace(result.ProviderReference) ? string.Empty : $" • {result.ProviderReference}")}{(string.IsNullOrWhiteSpace(result.Error) ? string.Empty : $" • ERROR: {result.Error}")}";
            ActionText.Text = result.State is "Printed" or "Spooled" or "Confirmed" ? "Run sheet accepted by print provider." : "Print provider did not confirm the run sheet.";
            await RefreshAsync(false);
        }
        catch (Exception ex)
        {
            PrintResultText.Text = "FAILED";
            ActionText.Text = ex.Message;
        }
    }

    private async void MarkPrintedClicked(object? sender, RoutedEventArgs e)
    {
        if (RunSheetList.SelectedItem is not RunSheetRow row)
        {
            ActionText.Text = "Select a run sheet first.";
            return;
        }
        try
        {
            var result = await _features.MarkRunSheetPrintedAsync(row.Id, new NotesActionRequest(Environment.UserName, "Manual station confirmation"));
            ActionText.Text = $"Run sheet revision {result.Revision} manually marked printed.";
            await RefreshAsync(false);
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
    }
}

public sealed record IncidentRow(Guid Id, string Header, string Address, string Detail);
public sealed record DeliveryRow(Guid Id, Guid DispatchActionId, string Display, string StateSummary);
public sealed record DeliveryPathRow(Guid Id, Guid StationDeliveryId, string Provider, string State, string Header, string Detail);
public sealed record RunSheetRow(Guid Id, int Revision, string Status, string Content, string Display);
