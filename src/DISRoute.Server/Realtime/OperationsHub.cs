using System.Collections.Concurrent;
using DISRoute.Contracts.Operational;
using DISRoute.Server.Operational;
using Microsoft.AspNetCore.SignalR;

namespace DISRoute.Server.Realtime;

public sealed class OperationsHub(FeatureStore featureStore) : Hub
{
    private static int _connected;
    private static readonly ConcurrentDictionary<string, PresenceDto> Presence = new(StringComparer.Ordinal);
    public static int ConnectedClients => Volatile.Read(ref _connected);
    public static IReadOnlyList<PresenceDto> CurrentPresence => Presence.Values.OrderBy(x => x.User).ToArray();

    public override async Task OnConnectedAsync()
    {
        Interlocked.Increment(ref _connected);
        var user = Context.User?.Identity?.Name ?? "local-development";
        _ = featureStore.ResolveActorAgency(user);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{user.ToLowerInvariant()}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _connected);
        if (Presence.TryRemove(Context.ConnectionId, out var previous))
        {
            await Clients.Group($"agency:{previous.AgencyId:D}").SendAsync("presenceLeft", previous);
            if (previous.IncidentId is Guid iid) await Clients.Group($"incident:{iid:D}").SendAsync("presenceLeft", previous);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task JoinAgency(Guid agencyId)
    {
        var user = Context.User?.Identity?.Name ?? "local-development";
        var actorAgency = featureStore.ResolveActorAgency(user);
        if (agencyId != actorAgency) throw new HubException("Agency group access denied.");
        return Groups.AddToGroupAsync(Context.ConnectionId, $"agency:{agencyId:D}");
    }
    public Task LeaveAgency(Guid agencyId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"agency:{agencyId:D}");
    public Task JoinIncident(Guid incidentId) => Groups.AddToGroupAsync(Context.ConnectionId, $"incident:{incidentId:D}");
    public Task LeaveIncident(Guid incidentId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"incident:{incidentId:D}");
    public async Task JoinChatChannel(Guid channelId)
    {
        var user = Context.User?.Identity?.Name ?? "local-development";
        if (!featureStore.CanAccessChatChannel(channelId, user)) throw new HubException("Chat channel access denied.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{channelId:D}");
    }
    public Task LeaveChatChannel(Guid channelId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{channelId:D}");

    public async Task<PresenceDto> SetPresence(Guid agencyId, Guid? incidentId, string panel, string? field)
    {
        var user = Context.User?.Identity?.Name ?? "local-development";
        var actorAgency = featureStore.ResolveActorAgency(user);
        if (agencyId != actorAgency) throw new HubException("Presence may only be published in the signed-in user's agency.");
        var current = new PresenceDto(Context.ConnectionId, actorAgency, incidentId, user, string.IsNullOrWhiteSpace(panel) ? "Workspace" : panel.Trim(), string.IsNullOrWhiteSpace(field) ? null : field.Trim(), DateTimeOffset.UtcNow);
        if (Presence.TryGetValue(Context.ConnectionId, out var previous) && previous.IncidentId is Guid previousIncident && previousIncident != incidentId)
            await Clients.Group($"incident:{previousIncident:D}").SendAsync("presenceLeft", previous);
        Presence[Context.ConnectionId] = current;
        await Groups.AddToGroupAsync(Context.ConnectionId, $"agency:{agencyId:D}");
        if (incidentId is Guid iid)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"incident:{iid:D}");
            await Clients.Group($"incident:{iid:D}").SendAsync("presenceChanged", current);
        }
        await Clients.Group($"agency:{agencyId:D}").SendAsync("presenceChanged", current);
        return current;
    }

    public async Task ClearPresence()
    {
        if (!Presence.TryRemove(Context.ConnectionId, out var previous)) return;
        await Clients.Group($"agency:{previous.AgencyId:D}").SendAsync("presenceLeft", previous);
        if (previous.IncidentId is Guid iid) await Clients.Group($"incident:{iid:D}").SendAsync("presenceLeft", previous);
    }

    public IReadOnlyList<PresenceDto> GetIncidentPresence(Guid incidentId) => Presence.Values.Where(x => x.IncidentId == incidentId).OrderBy(x => x.User).ToArray();
}
