using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;
using System.Text.Json;

namespace DISRoute.Station.Display;

public sealed partial class MainWindow : Window
{
    private static readonly HttpClient SharedHttp = CreateHttpClient();
    private readonly FeatureApiClient _api;
    private readonly DispatcherTimer _timer;
    private readonly string _cachePath;
    private Guid _stationId;

    public MainWindow()
    {
        InitializeComponent();
        _stationId = Guid.TryParse(Environment.GetEnvironmentVariable("DISROUTE_STATION_ID"), out var configured) ? configured : Guid.Parse("11111111-1111-1111-1111-111111111111");
        _api = new FeatureApiClient(SharedHttp);
        var cacheRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cachePath = Path.Combine(cacheRoot, "DISRoute", "station-display-cache.json");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Opened += async (_, _) => { LoadCache(); await RefreshAsync(); _timer.Start(); };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        clock.Start();
    }

    private static HttpClient CreateHttpClient() => ClientHttpFactory.Create(TimeSpan.FromSeconds(8));

    private async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _api.GetStationSnapshotAsync(_stationId);
            Render(snapshot, false);
            SaveCache(snapshot);
        }
        catch
        {
            ConnectionText.Text = "DEGRADED — SHOWING LAST RECEIVED DATA";
            ConnectionBanner.Background = new SolidColorBrush(Color.Parse("#8B0000"));
        }
    }

    private void Render(StationSnapshotDto snapshot, bool cached)
    {
        IncidentItems.ItemsSource = snapshot.Incidents.Select(i => $"{i.Number}   {i.Nature}\n{i.Address}  {i.CrossStreets}\n{i.Priority} • {string.Join(", ", snapshot.Units.Where(u => u.IncidentId == i.Id).Select(u => u.CallSign))}\n{i.Notes}").ToArray();
        StationText.Text = $"Station {_stationId:D}";
        UpdatedText.Text = $"Updated {snapshot.AsOfUtc.LocalDateTime:G}";
        ConnectionText.Text = cached ? "DEGRADED — CACHED DATA" : "CONNECTED";
        ConnectionBanner.Background = new SolidColorBrush(Color.Parse(cached ? "#8B0000" : "#174D28"));
    }

    private void SaveCache(StationSnapshotDto snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(snapshot));
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var snapshot = JsonSerializer.Deserialize<StationSnapshotDto>(File.ReadAllText(_cachePath));
            if (snapshot is not null) Render(snapshot, true);
        }
        catch { }
    }
}
