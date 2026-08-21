using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;

namespace DISRoute.CAD;

public sealed partial class ReplayWindow : Window
{
    private readonly OperationalApiClient? _core;
    private readonly IncidentDto? _incident;

    public ReplayWindow()
    {
        InitializeComponent();
        ReplayAtBox.Text = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    public ReplayWindow(OperationalApiClient core, IncidentDto incident) : this()
    {
        _core = core;
        _incident = incident;
        IncidentIdentityText.Text = $"{incident.Number} — {incident.Nature}";
        Opened += async (_, _) => await LoadSnapshotAsync();
    }

    private async void LoadClicked(object? sender, RoutedEventArgs e) => await LoadSnapshotAsync();

    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();

    private async Task LoadSnapshotAsync()
    {
        if (_core is null || _incident is null) return;
        if (!DateTimeOffset.TryParse(ReplayAtBox.Text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            StatusText.Text = "Enter a valid UTC timestamp, for example 2026-08-20T18:30:00Z.";
            return;
        }

        try
        {
            StatusText.Text = "Reconstructing historical state…";
            var replay = await _core.GetReplaySnapshotAsync(_incident.Id, at);
            IncidentStateText.Text = $"{replay.Incident.Number} • {replay.Incident.Priority} • {replay.Incident.Status}\n{replay.Incident.Nature}\n{replay.Incident.Address} {replay.Incident.CrossStreets}\nVersion {replay.Incident.Version} at {replay.AtUtc:O}";
            UnitSummaryText.Text = replay.Units.Count == 0 ? "None" : string.Join("\n", replay.Units.Select(x => $"{x.CallSign} — {x.Status}"));
            DispatchSummaryText.Text = $"Actions: {replay.DispatchActions.Count}\nDeliveries: {replay.DispatchDeliveries.Count}\nStation: {replay.StationDeliveries.Count}\nRun sheets: {replay.RunSheets.Count}";
            ObservationSummaryText.Text = $"AVL: {replay.Avl.Count}\nAIS: {replay.Ais.Count}\nRoad restrictions: {replay.RoadRestrictions.Count}\nMap objects: {replay.MapObjects.Count}";
            TimelineList.ItemsSource = replay.Timeline
                .OrderBy(x => x.EffectiveAtUtc)
                .Select(x => new ReplayTimelineRow(x.EffectiveAtUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), x.Classification, x.Stream, x.Summary))
                .ToArray();
            CorrectionsList.ItemsSource = replay.CorrectedLater
                .Select(x => new ReplayCorrectionRow(x.Field, $"{x.OriginalValue} → {x.CorrectedValue}", x.CorrectedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))
                .ToArray();
            LimitationsList.ItemsSource = replay.Limitations.Count == 0 ? ["No known replay limitations for this snapshot."] : replay.Limitations;
            StatusText.Text = $"Historical snapshot loaded at {replay.AtUtc:O}. KNOWN={replay.Timeline.Count(x => x.Classification == "KNOWN")}, OBSERVED={replay.Timeline.Count(x => x.Classification == "OBSERVED")}, CORRECTED LATER={replay.Timeline.Count(x => x.Classification == "CORRECTED LATER")}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}

public sealed record ReplayTimelineRow(string Time, string Classification, string Stream, string Summary);
public sealed record ReplayCorrectionRow(string Field, string Change, string Time);
