using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Client.Common;

/// <summary>
/// Minimal ASP.NET Core SignalR JSON-protocol client used by the desktop applications.
/// It intentionally implements only the OperationsHub features DISRoute needs so the
/// desktop client does not require a second realtime stack or polling for chat updates.
/// </summary>
public sealed class OperationsRealtimeClient : IAsyncDisposable
{
    private const char RecordSeparator = '\u001e';
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri _baseUri;
    private readonly CookieContainer _cookies = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private ClientWebSocket? _socket;
    private Task? _runTask;
    private Guid _agencyId;
    private Guid? _chatChannelId;
    private Guid? _incidentId;
    private string _panel = "OperationsCenter";
    private bool _started;

    public event Action<ChatMessageDto>? ChatMessageReceived;
    public event Action<ChatChannelDto>? ChatChannelChanged;
    public event Action<ChatReadStateDto>? ChatReadStateChanged;
    public event Action<PresenceDto>? PresenceChanged;
    public event Action<PresenceDto>? PresenceLeft;
    public event Action<IncidentDto>? IncidentChanged;
    public event Action<UnitDto>? UnitChanged;
    public event Action<MaydayEventDto>? MaydayChanged;
    public event Action<MessageDto>? MessageReceived;
    public event Action<string>? ConnectionStateChanged;

    public OperationsRealtimeClient()
    {
        var configured = Environment.GetEnvironmentVariable("DISROUTE_SERVER_URL") ?? "http://127.0.0.1:5099/";
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var baseUri)) throw new InvalidOperationException("DISROUTE_SERVER_URL must be an absolute URI.");
        _baseUri = baseUri;
    }

    public Task StartAsync(Guid agencyId, string panel = "OperationsCenter")
    {
        if (_started) return Task.CompletedTask;
        _agencyId = agencyId;
        _panel = string.IsNullOrWhiteSpace(panel) ? "OperationsCenter" : panel.Trim();
        _started = true;
        _runTask = RunAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task SwitchIncidentAsync(Guid? incidentId, string? field = null)
    {
        var previous = _incidentId;
        _incidentId = incidentId;
        if (_socket?.State != WebSocketState.Open) return;
        if (previous is Guid oldId && oldId != incidentId) await InvokeAsync("LeaveIncident", [oldId], _lifetime.Token);
        if (incidentId is Guid newId && newId != previous) await InvokeAsync("JoinIncident", [newId], _lifetime.Token);
        await PublishPresenceAsync(field ?? (_chatChannelId is Guid chatId ? $"chat:{chatId:D}" : "CurrentIncident"), _lifetime.Token);
    }

    public async Task SwitchChatChannelAsync(Guid? channelId)
    {
        var previous = _chatChannelId;
        _chatChannelId = channelId;
        if (_socket?.State != WebSocketState.Open) return;
        if (previous is Guid oldId && oldId != channelId) await InvokeAsync("LeaveChatChannel", [oldId], _lifetime.Token);
        if (channelId is Guid newId && newId != previous) await InvokeAsync("JoinChatChannel", [newId], _lifetime.Token);
        await PublishPresenceAsync(channelId is Guid id ? $"chat:{id:D}" : "Chat", _lifetime.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(cancellationToken);
                ConnectionStateChanged?.Invoke("Connected");
                await InvokeAsync("JoinAgency", [_agencyId], cancellationToken);
                if (_incidentId is Guid incidentId) await InvokeAsync("JoinIncident", [incidentId], cancellationToken);
                if (_chatChannelId is Guid channelId) await InvokeAsync("JoinChatChannel", [channelId], cancellationToken);
                await PublishPresenceAsync(_chatChannelId is Guid id ? $"chat:{id:D}" : (_incidentId is Guid ? "CurrentIncident" : "Workspace"), cancellationToken);
                await ReceiveLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                ConnectionStateChanged?.Invoke("Disconnected — " + ex.Message);
            }
            finally
            {
                if (_socket is not null)
                {
                    _socket.Dispose();
                    _socket = null;
                }
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = _cookies,
            UseDefaultCredentials = ResolveUseDefaultCredentials(),
            AllowAutoRedirect = false
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        var negotiateUri = new Uri(_baseUri, "hubs/operations/negotiate?negotiateVersion=1");
        using var response = await http.PostAsync(negotiateUri, new ByteArrayContent([]), cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var negotiate = JsonSerializer.Deserialize<NegotiateResponse>(payload, JsonOptions) ?? throw new InvalidOperationException("SignalR negotiation returned no payload.");
        if (string.IsNullOrWhiteSpace(negotiate.ConnectionToken)) throw new InvalidOperationException("SignalR negotiation returned no connection token.");

        var hubUri = new Uri(_baseUri, $"hubs/operations?id={Uri.EscapeDataString(negotiate.ConnectionToken)}");
        var wsBuilder = new UriBuilder(hubUri) { Scheme = hubUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws" };
        var socket = new ClientWebSocket();
        socket.Options.Cookies = _cookies;
        if (ResolveUseDefaultCredentials()) socket.Options.Credentials = CredentialCache.DefaultCredentials;
        await socket.ConnectAsync(wsBuilder.Uri, cancellationToken);
        _socket = socket;
        await SendTextAsync("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator, cancellationToken);
        var handshake = await ReceiveTextMessageAsync(socket, cancellationToken);
        if (!handshake.Contains(RecordSeparator, StringComparison.Ordinal)) throw new InvalidOperationException("SignalR handshake did not complete.");
        var handshakePayload = handshake.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(handshakePayload))
        {
            using var document = JsonDocument.Parse(handshakePayload);
            if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException("SignalR handshake failed: " + error.GetString());
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("SignalR connection is not open.");
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var text = await ReceiveTextMessageAsync(socket, cancellationToken);
            foreach (var frame in text.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) HandleFrame(frame);
        }
    }

    private void HandleFrame(string frame)
    {
        using var document = JsonDocument.Parse(frame);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement)) return;
        var type = typeElement.GetInt32();
        if (type == 6) return; // keep-alive ping
        if (type == 7)
        {
            var error = root.TryGetProperty("error", out var errorElement) ? errorElement.GetString() : "server closed the connection";
            throw new InvalidOperationException(error);
        }
        if (type != 1 || !root.TryGetProperty("target", out var targetElement) || !root.TryGetProperty("arguments", out var arguments) || arguments.GetArrayLength() == 0) return;
        var target = targetElement.GetString();
        var argument = arguments[0];
        switch (target)
        {
            case "chatMessage": ChatMessageReceived?.Invoke(argument.Deserialize<ChatMessageDto>(JsonOptions)!); break;
            case "chatChannelChanged": ChatChannelChanged?.Invoke(argument.Deserialize<ChatChannelDto>(JsonOptions)!); break;
            case "chatReadState": ChatReadStateChanged?.Invoke(argument.Deserialize<ChatReadStateDto>(JsonOptions)!); break;
            case "presenceChanged": PresenceChanged?.Invoke(argument.Deserialize<PresenceDto>(JsonOptions)!); break;
            case "presenceLeft": PresenceLeft?.Invoke(argument.Deserialize<PresenceDto>(JsonOptions)!); break;
            case "incidentChanged": IncidentChanged?.Invoke(argument.Deserialize<IncidentDto>(JsonOptions)!); break;
            case "unitChanged": UnitChanged?.Invoke(argument.Deserialize<UnitDto>(JsonOptions)!); break;
            case "maydayChanged": MaydayChanged?.Invoke(argument.Deserialize<MaydayEventDto>(JsonOptions)!); break;
            case "messageReceived": MessageReceived?.Invoke(argument.Deserialize<MessageDto>(JsonOptions)!); break;
        }
    }

    private Task PublishPresenceAsync(string field, CancellationToken cancellationToken) => InvokeAsync("SetPresence", [_agencyId, _incidentId, _panel, field], cancellationToken);

    private async Task InvokeAsync(string target, object?[] arguments, CancellationToken cancellationToken)
    {
        if (_socket?.State != WebSocketState.Open) return;
        var payload = JsonSerializer.Serialize(new { type = 1, target, arguments }, JsonOptions) + RecordSeparator;
        await SendTextAsync(payload, cancellationToken);
    }

    private async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var socket = _socket ?? throw new InvalidOperationException("SignalR connection is not open.");
        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendGate.WaitAsync(cancellationToken);
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken); }
        finally { _sendGate.Release(); }
    }

    private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("SignalR websocket closed.");
            if (result.MessageType != WebSocketMessageType.Text) continue;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool ResolveUseDefaultCredentials()
    {
        var configured = Environment.GetEnvironmentVariable("DISROUTE_USE_DEFAULT_CREDENTIALS");
        if (bool.TryParse(configured, out var parsed)) return parsed;
        return OperatingSystem.IsWindows();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_socket?.State == WebSocketState.Open)
        {
            try { await InvokeAsync("ClearPresence", [], CancellationToken.None); } catch { }
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "DISRoute closing", CancellationToken.None); } catch { }
        }
        if (_runTask is not null)
        {
            try { await _runTask; } catch { }
        }
        _lifetime.Dispose();
        _sendGate.Dispose();
        _socket?.Dispose();
    }

    private sealed record NegotiateResponse(string? ConnectionId, string? ConnectionToken);
}
