using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;
using DISRoute.Mobile.Core;

namespace DISRoute.Mobile;

public partial class MainWindow : Window, IDisposable
{
    private readonly HttpClient _http;
    private readonly FeatureApiClient _features;
    private readonly OperationalApiClient _operations;
    private readonly MobileSession _session;
    private readonly OperationsRealtimeClient _realtime = new();
    private readonly AesGcmOfflineDataProtector _protector;
    private bool _disposed;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Guid _agencyId;
    private readonly string _deviceId;
    private bool _realtimeStarted;
    private bool _updatingUnitSelection;

    public MainWindow()
    {
        InitializeComponent();
        _agencyId = ResolveAgencyId();
        _deviceId = ResolveDeviceId();
        _http = ClientHttpFactory.Create(TimeSpan.FromSeconds(12));
        _features = new FeatureApiClient(_http);
        _operations = new OperationalApiClient(_http);
        _protector = CreateProtector();
        var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DISRoute", "Mobile");
        var queue = new OfflineOperationStore(Path.Combine(stateDir, "pending.bin"), _protector);
        var cache = new MobileCacheStore(Path.Combine(stateDir, "cache.bin"), _protector);
        _session = new MobileSession(_features, queue, _agencyId, _deviceId, cache, _operations)
        {
            SelectedUnitId = ResolveConfiguredUnitId()
        };

        _realtime.ConnectionStateChanged += state => Dispatcher.UIThread.Post(() =>
        {
            if (state.StartsWith("Disconnected", StringComparison.OrdinalIgnoreCase)) ShowConnectionState();
        });
        _realtime.IncidentChanged += _ => ScheduleReadOnlyRefresh();
        _realtime.UnitChanged += _ => ScheduleReadOnlyRefresh();
        _realtime.MaydayChanged += _ => ScheduleReadOnlyRefresh();
        _realtime.MessageReceived += _ => ScheduleReadOnlyRefresh();
        _realtime.ChatMessageReceived += _ => ScheduleReadOnlyRefresh();
        Opened += async (_, _) => await InitializeAsync();
        Closed += (_, _) => Dispose();
        Render();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _session.RefreshReadOnlyAsync();
            await EnsureRealtimeAsync();
        }
        catch (Exception ex) { ActionText.Text = "Startup sync unavailable — " + ex.Message; }
        Render();
    }

    private async Task EnsureRealtimeAsync()
    {
        if (!_realtimeStarted)
        {
            _realtimeStarted = true;
            await _realtime.StartAsync(_agencyId, "Mobile");
        }
        await _realtime.SwitchIncidentAsync(_session.OperationalPackage?.CurrentIncident?.Id, "CurrentIncident");
    }

    private async void ScheduleReadOnlyRefresh()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            await Task.Delay(120);
            try
            {
                await _session.RefreshReadOnlyAsync();
                await EnsureRealtimeAsync();
                await Dispatcher.UIThread.InvokeAsync(Render);
            }
            catch { await Dispatcher.UIThread.InvokeAsync(ShowConnectionState); }
        }
        finally { _refreshGate.Release(); }
    }

    private async void SyncClicked(object? sender, RoutedEventArgs e)
    {
        ActionText.Text = "Synchronizing authoritative state and queued operations…";
        try
        {
            var result = await _session.SyncAsync();
            var conflicts = result.OperationResults.Count(x => x.State is "Conflict" or "Rejected" or "RequiresOnline");
            ActionText.Text = conflicts == 0 ? "Sync complete." : $"Sync complete — {conflicts} queued operation(s) need review.";
            await EnsureRealtimeAsync();
        }
        catch (Exception ex) { ActionText.Text = "Sync failed — cached data retained: " + ex.Message; }
        Render();
    }

    private async void LeaseClicked(object? sender, RoutedEventArgs e)
    {
        var userId = _session.OperationalPackage?.UserId;
        if (string.IsNullOrWhiteSpace(userId)) { ActionText.Text = "Sync successfully before requesting an offline authorization lease."; return; }
        try
        {
            var lease = await _session.AcquireOfflineUnitStatusLeaseAsync(userId, 30);
            ActionText.Text = $"Offline unit-status lease active until {lease.ExpiresAtUtc.ToLocalTime():t}.";
        }
        catch (Exception ex) { ActionText.Text = "Offline lease unavailable — " + ex.Message; }
    }

    private async Task ChangeUnitStatusAsync(string status)
    {
        var unit = _session.OperationalPackage?.SelectedUnit;
        if (unit is null) { ActionText.Text = "No authorized unit is selected."; return; }
        if (_session.ConnectionState == "Online")
        {
            try
            {
                await _session.SetUnitStatusOnlineAsync(unit, status);
                ActionText.Text = $"{unit.CallSign} → {status}.";
                await _session.RefreshReadOnlyAsync();
                await EnsureRealtimeAsync();
                Render();
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                ActionText.Text = "Connection dropped; attempting explicit offline queue.";
            }
            catch (Exception ex) { ActionText.Text = "Status change rejected — " + ex.Message; return; }
        }
        try
        {
            _session.QueueUnitStatus(unit, status);
            ActionText.Text = $"QUEUED OFFLINE: {unit.CallSign} {unit.Status} → {status}. Press SYNC after reconnect to reconcile.";
        }
        catch (Exception ex) { ActionText.Text = "Cannot queue status offline — " + ex.Message; }
        Render();
    }

    private async void EnRouteClicked(object? s, RoutedEventArgs e) => await ChangeUnitStatusAsync("EnRoute");
    private async void OnSceneClicked(object? s, RoutedEventArgs e) => await ChangeUnitStatusAsync("OnScene");
    private async void ClearingClicked(object? s, RoutedEventArgs e) => await ChangeUnitStatusAsync("Clearing");
    private async void AvailableClicked(object? s, RoutedEventArgs e) => await ChangeUnitStatusAsync("Available");
    private async void CommandClicked(object? s, RoutedEventArgs e) => await ChangeUnitStatusAsync("Command");
    private async void Code55Clicked(object? s, RoutedEventArgs e) => await ChangeUnitStatusAsync("Code55");

    private void QueueNarrativeClicked(object? sender, RoutedEventArgs e)
    {
        var incident = _session.OperationalPackage?.CurrentIncident;
        if (incident is null || string.IsNullOrWhiteSpace(NarrativeText.Text)) { ActionText.Text = "A current incident and note text are required."; return; }
        try
        {
            _session.QueueNarrative(incident.Id, NarrativeText.Text);
            NarrativeText.Text = string.Empty;
            ActionText.Text = "Incident note queued. Press SYNC to commit it.";
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
        Render();
    }


    private void DiscardQueuedClicked(object? sender, RoutedEventArgs e)
    {
        if (QueueReviewList.SelectedItem is not QueueRow row)
        {
            ActionText.Text = "Select a queued item to discard.";
            return;
        }
        if (_session.DiscardQueuedOperation(row.Id))
            ActionText.Text = $"Discarded queued {row.Kind} operation. No authoritative write was performed.";
        Render();
    }

    private async void SendChatClicked(object? sender, RoutedEventArgs e)
    {
        if (ChatChannelBox.SelectedItem is not ChatChannelRow row || string.IsNullOrWhiteSpace(ChatText.Text)) return;
        var text = ChatText.Text.Trim();
        var incidentId = _session.OperationalPackage?.CurrentIncident?.Id;
        if (_session.ConnectionState == "Online")
        {
            try
            {
                await _features.SendChatMessageAsync(row.Id, new SendChatMessageRequest(text, "Normal", "Operational", incidentId, null, null));
                ChatText.Text = string.Empty;
                await _session.RefreshReadOnlyAsync();
                RenderChatHistory(row.Id);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { ActionText.Text = "Chat connection dropped; queuing message."; }
            catch (Exception ex) { ActionText.Text = "Chat send rejected — " + ex.Message; return; }
        }
        try
        {
            _session.QueueChatMessage(row.Id, incidentId, text);
            ChatText.Text = string.Empty;
            ActionText.Text = "Chat message queued. Press SYNC after reconnect to commit it.";
        }
        catch (Exception ex) { ActionText.Text = ex.Message; }
        Render();
    }

    private async void ChatChannelChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ChatChannelBox.SelectedItem is not ChatChannelRow row) return;
        await _realtime.SwitchChatChannelAsync(row.Id);
        RenderChatHistory(row.Id);
    }

    private async void UnitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingUnitSelection || UnitBox.SelectedItem is not UnitRow row || row.Id == _session.SelectedUnitId) return;
        _session.SelectedUnitId = row.Id;
        try { await _session.RefreshReadOnlyAsync(); await EnsureRealtimeAsync(); }
        catch (Exception ex) { ActionText.Text = "Unit selection refresh failed — " + ex.Message; }
        Render();
    }

    private void DocumentChanged(object? sender, SelectionChangedEventArgs e)
    {
        DocumentText.Text = DocumentList.SelectedItem is DocumentRow row ? row.Content : string.Empty;
    }

    private void RadioChanged(object? sender, SelectionChangedEventArgs e)
    {
        RadioDetailText.Text = RadioList.SelectedItem is RadioRow row ? $"{row.Codec} · {row.State}\n{row.Uri}" : "Select an authorized feed.";
    }

    private void OpenRadioClicked(object? sender, RoutedEventArgs e)
    {
        if (RadioList.SelectedItem is not RadioRow row) return;
        try
        {
            Process.Start(new ProcessStartInfo(row.Uri) { UseShellExecute = true });
            ActionText.Text = "Opened authorized listen-only stream. DISRoute exposes no transmit/PTT control.";
        }
        catch (Exception ex) { ActionText.Text = "Unable to open radio stream — " + ex.Message; }
    }

    private void Render()
    {
        var package = _session.OperationalPackage;
        ShowConnectionState();
        QueueText.Text = $"{_session.PendingOperationCount} queued";
        var resultStates = _session.LastOperationResults.ToDictionary(x => x.OperationId);
        QueueReviewList.ItemsSource = _session.PendingOperations.Select(x =>
        {
            var state = resultStates.TryGetValue(x.OperationId, out var result) ? $" · {result.State}: {result.Detail}" : " · Pending";
            return new QueueRow(x.OperationId, x.Kind, $"{x.Kind} · {x.OccurredAtUtc.ToLocalTime():t}{state}");
        }).ToArray();
        AssignmentText.Text = package?.SelectedUnit is UnitDto unit ? $"{unit.CallSign} · {unit.Status}" : "No unit assignment";

        _updatingUnitSelection = true;
        var unitRows = _session.Units.Select(x => new UnitRow(x.Id, $"{x.CallSign} — {x.Status}")).ToArray();
        UnitBox.ItemsSource = unitRows;
        UnitBox.SelectedItem = unitRows.FirstOrDefault(x => x.Id == package?.SelectedUnitId);
        _updatingUnitSelection = false;

        var incident = package?.CurrentIncident;
        IncidentHeader.Text = incident is null ? "NO CURRENT INCIDENT" : $"{incident.Number} · {incident.Nature}";
        IncidentAddress.Text = incident?.Address ?? string.Empty;
        IncidentDetail.Text = incident is null ? "No incident is assigned to the selected unit." : $"{incident.Priority} · {incident.Status}\n{incident.CrossStreets}\n{incident.Notes}";
        CrewList.ItemsSource = package?.Crew.Select(x => $"{x.Position}: {x.EmployeeName} [{x.AssignmentType}]").ToArray() ?? [];

        var maydays = package?.Maydays ?? [];
        MaydayBorder.IsVisible = maydays.Count > 0;
        MaydayText.Text = maydays.Count == 0 ? string.Empty : string.Join("\n", maydays.Select(x => $"MAYDAY · {x.Person} · {x.Location} · {x.State}"));
        MessageList.ItemsSource = package?.OperationalMessages.Select(x => $"{x.CreatedAtUtc.ToLocalTime():t} {x.Sender}: {x.Body}").ToArray() ?? [];

        var site = package?.SiteIntelligence;
        SceneText.Text = site is null ? "No scene package." : $"Scene: {(site.SceneLatitude is double lat ? lat.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) : "?")}, {(site.SceneLongitude is double lon ? lon.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) : "?")}\n{string.Join(" · ", site.Warnings)}";
        PreplanList.ItemsSource = site?.PublishedPreplans.Select(x => $"{x.Title} · v{x.Version} · {x.Status}").ToArray() ?? [];
        CrgList.ItemsSource = site?.CrgAssets.Select(x => $"Floor {x.Floor} · {x.FileName}").ToArray() ?? [];
        HydrantList.ItemsSource = site?.Hydrants.Select(x => $"{x.Hydrant.AssetId} · {x.Hydrant.Address} · {x.DistanceMeters:F0} m · {x.Hydrant.FlowGpm:F0} gpm").ToArray() ?? [];
        RoadList.ItemsSource = site?.RoadRestrictions.Select(x => $"{x.Restriction.Name} · {x.DistanceMeters:F0} m · {x.Restriction.Description}").ToArray() ?? [];

        var selectedChatId = (ChatChannelBox.SelectedItem as ChatChannelRow)?.Id;
        var channelRows = package?.ChatChannels.Select(x => new ChatChannelRow(x.Channel.Id, $"{x.Channel.Name} ({x.UnreadCount})")).ToArray() ?? [];
        ChatChannelBox.ItemsSource = channelRows;
        ChatChannelBox.SelectedItem = channelRows.FirstOrDefault(x => x.Id == selectedChatId);
        if (selectedChatId is Guid chatId) RenderChatHistory(chatId);

        DocumentList.ItemsSource = package?.Documents.Select(x => new DocumentRow(x.DocumentId, $"{x.Title} · v{x.Version}{(x.RequiredRead && !x.Acknowledged ? " · REQUIRED" : string.Empty)}", x.Content)).ToArray() ?? [];
        RadioList.ItemsSource = package?.RadioStreams.Select(x => new RadioRow(x.Id, x.Name, x.Codec, x.State, x.IcecastUri)).ToArray() ?? [];
    }

    private void RenderChatHistory(Guid channelId)
    {
        ChatHistoryList.ItemsSource = _session.OperationalPackage?.ChatMessages.Where(x => x.ChannelId == channelId).Select(x => $"{x.CreatedAtUtc.ToLocalTime():t} {x.Sender}: {x.Body}").ToArray() ?? [];
    }

    private void ShowConnectionState()
    {
        ConnectionText.Text = _session.ConnectionState.ToUpperInvariant();
        DegradedBanner.IsVisible = _session.ConnectionState != "Online";
        if (_session.ConnectionState == "Online")
        {
            DegradedText.Text = "Online — authoritative state current.";
        }
        else
        {
            var age = _session.CachedAtUtc is DateTimeOffset cached ? $" Cached {DateTimeOffset.UtcNow - cached:g} ago." : string.Empty;
            DegradedText.Text = $"{_session.ConnectionState.ToUpperInvariant()} — {_session.ConnectionDetail ?? "Server unavailable."}{age}";
        }
    }



    private static Guid ResolveAgencyId()
    {
        var configured = Environment.GetEnvironmentVariable("DISROUTE_AGENCY_ID");
        return Guid.TryParse(configured, out var id) ? id : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    }

    private static Guid? ResolveConfiguredUnitId() => Guid.TryParse(Environment.GetEnvironmentVariable("DISROUTE_MOBILE_UNIT_ID"), out var id) ? id : null;
    private static string ResolveDeviceId() => (Environment.GetEnvironmentVariable("DISROUTE_MOBILE_DEVICE_ID") ?? (Environment.MachineName + "-mobile")).Trim();

    private static AesGcmOfflineDataProtector CreateProtector()
    {
        var configured = Environment.GetEnvironmentVariable("DISROUTE_MOBILE_CACHE_KEY_BASE64");
        if (!string.IsNullOrWhiteSpace(configured)) return AesGcmOfflineDataProtector.FromBase64Key(configured.Trim());
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DISRoute", "Mobile");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "cache.key");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return AesGcmOfflineDataProtector.FromBase64Key(File.ReadAllText(path).Trim());
    }

    private sealed record QueueRow(Guid Id, string Kind, string Display) { public override string ToString() => Display; }
    private sealed record UnitRow(Guid Id, string Display) { public override string ToString() => Display; }
    private sealed record ChatChannelRow(Guid Id, string Display) { public override string ToString() => Display; }
    private sealed record DocumentRow(Guid Id, string Display, string Content) { public override string ToString() => Display; }
    private sealed record RadioRow(Guid Id, string Display, string Codec, string State, string Uri) { public override string ToString() => Display; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _realtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _http.Dispose();
        _refreshGate.Dispose();
        _protector.Dispose();
        GC.SuppressFinalize(this);
    }
}
