using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, ChatChannelDto> _chatChannels = [];
    private readonly Dictionary<Guid, ChatMessageDto> _chatMessages = [];
    private readonly Dictionary<string, ChatReadStateDto> _chatReadStates = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ChatChannelDto> ChatChannels { get { lock (_gate) return _chatChannels.Values.OrderBy(x => x.Name).ToArray(); } }
    public IReadOnlyList<ChatMessageDto> ChatMessages { get { lock (_gate) return _chatMessages.Values.OrderBy(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<ChatReadStateDto> ChatReadStates { get { lock (_gate) return _chatReadStates.Values.ToArray(); } }

    public Guid ResolveActorAgency(string actor)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(actor) || !_users.TryGetValue(actor.Trim(), out var user))
                throw new UnauthorizedAccessException("The signed-in user is not provisioned in DISRoute.");
            if (user.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase) || user.State.Equals("Revoked", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The signed-in DISRoute user is disabled.");
            return user.AgencyId;
        }
    }

    public ChatChannelDto CreateChatChannel(CreateChatChannelRequest request, string actor)
    {
        lock (_gate)
        {
            var actorAgency = ResolveActorAgency(actor);
            if (request.OwnerAgencyId != actorAgency)
                throw new UnauthorizedAccessException("A chat channel may only be created in the signed-in user's agency.");

            var type = NormalizeChatType(request.Type);
            var members = NormalizeStrings(request.MemberUserIds);
            if (!members.Contains(actor, StringComparer.OrdinalIgnoreCase)) members.Add(actor);
            var roles = NormalizeStrings(request.AllowedRoles);
            var agencies = (request.ParticipatingAgencyIds ?? []).Append(request.OwnerAgencyId).Distinct().ToList();
            var stationId = request.StationId;
            var incidentId = request.IncidentId;
            var groupId = request.OperationalGroupId;

            if (type == "Direct" && members.Count != 2) throw new InvalidOperationException("Direct-message channels require exactly two members.");
            if (type == "MutualAid" && agencies.Count < 2) throw new InvalidOperationException("Mutual-aid channels require at least two participating agencies.");
            if (type != "MutualAid") agencies = [request.OwnerAgencyId];

            if (type == "Station" && stationId is null) throw new InvalidOperationException("Station channels require a station.");
            if (stationId is Guid sid)
            {
                var station = _core.Stations.SingleOrDefault(x => x.Id == sid) ?? throw new KeyNotFoundException("Station not found.");
                if (station.AgencyId != request.OwnerAgencyId) throw new InvalidOperationException("Station belongs to a different agency.");
            }

            if (type == "Group")
            {
                if (groupId is not Guid gid) throw new InvalidOperationException("Group channels require an operational group.");
                if (!_operationalGroups.TryGetValue(gid, out var group)) throw new KeyNotFoundException("Operational group not found.");
                if (group.AgencyId != request.OwnerAgencyId) throw new InvalidOperationException("Operational group belongs to a different agency.");
                if (incidentId is Guid requestedIncident && requestedIncident != group.IncidentId) throw new InvalidOperationException("Group channel incident must match the operational group incident.");
                incidentId = group.IncidentId;
            }
            else if (groupId is not null)
            {
                throw new InvalidOperationException("Only Group channels may reference an operational group.");
            }

            if (type == "Supervisor" && roles.Count == 0)
            {
                roles.Add("Supervisor");
                roles.Add("Administrator");
            }

            if (incidentId is Guid iid)
            {
                var incident = _core.GetIncident(iid);
                if (!agencies.Contains(incident.AgencyId)) throw new InvalidOperationException("Linked incident is outside the channel's participating agencies.");
            }

            ValidateChatMembers(members, agencies);
            if (type == "MutualAid")
            {
                var represented = members.Select(ResolveActorAgency).Distinct().ToHashSet();
                if (agencies.Any(x => !represented.Contains(x)))
                    throw new InvalidOperationException("Each participating mutual-aid agency must have at least one explicit channel member.");
            }

            var now = DateTimeOffset.UtcNow;
            var item = new ChatChannelDto(Guid.NewGuid(), request.OwnerAgencyId, type,
                string.IsNullOrWhiteSpace(request.Name) ? DefaultChatName(type, members) : request.Name.Trim(),
                request.Topic?.Trim() ?? string.Empty, stationId, incidentId, agencies, members, roles, false, actor, now, now, groupId);
            _chatChannels[item.Id] = item;
            AuditMutation(item.OwnerAgencyId, actor, "ChatChannelCreated", "ChatChannel", item.Id.ToString(), $"{item.Type}: {item.Name}");
            return item;
        }
    }

    public ChatChannelDto SetChatChannelArchived(Guid id, bool archived, string actor)
    {
        lock (_gate)
        {
            var channel = GetAccessibleChannel(id, actor);
            if (!channel.CreatedBy.Equals(actor, StringComparison.OrdinalIgnoreCase) && !ActorHasRole(actor, "Supervisor") && !ActorHasRole(actor, "Administrator"))
                throw new InvalidOperationException("Only the channel creator or a supervisor/administrator may archive this channel.");
            var updated = channel with { Archived = archived, UpdatedAtUtc = DateTimeOffset.UtcNow };
            _chatChannels[id] = updated;
            AuditMutation(updated.OwnerAgencyId, actor, archived ? "ChatChannelArchived" : "ChatChannelRestored", "ChatChannel", id.ToString(), updated.Name);
            return updated;
        }
    }

    public IReadOnlyList<ChatChannelSummaryDto> GetChatChannels(Guid agencyId, string actor, bool includeArchived)
    {
        lock (_gate)
        {
            var actorAgency = ResolveActorAgency(actor);
            if (agencyId != actorAgency) throw new UnauthorizedAccessException("Chat channel queries are limited to the signed-in user's agency context.");
            return _chatChannels.Values
                .Where(x => includeArchived || !x.Archived)
                .Where(x => CanAccessChatChannelCore(x, actor, actorAgency))
                .Select(x => new ChatChannelSummaryDto(x, CountUnread(x.Id, actor), _chatMessages.Values.Where(m => m.ChannelId == x.Id).OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault()))
                .OrderByDescending(x => x.LastMessage?.CreatedAtUtc ?? x.Channel.UpdatedAtUtc)
                .ToArray();
        }
    }

    public IReadOnlyList<ChatMessageDto> GetChatHistory(Guid channelId, string actor, DateTimeOffset? beforeUtc, int limit)
    {
        lock (_gate)
        {
            _ = GetAccessibleChannel(channelId, actor);
            var take = Math.Clamp(limit, 1, 200);
            IEnumerable<ChatMessageDto> source = _chatMessages.Values.Where(x => x.ChannelId == channelId);
            if (beforeUtc is DateTimeOffset before) source = source.Where(x => x.CreatedAtUtc < before);
            return source.OrderByDescending(x => x.CreatedAtUtc).Take(take).OrderBy(x => x.CreatedAtUtc).ToArray();
        }
    }

    public ChatMessageDto SendChatMessage(Guid channelId, SendChatMessageRequest request, string actor)
    {
        lock (_gate)
        {
            var idempotencyRequest = new { ChannelId = channelId, Actor = actor, Request = request };
            if (_idempotency.TryGet<ChatMessageDto>(request.IdempotencyKey, "FeatureStore.SendChatMessage", idempotencyRequest, out var existing)) return existing;
            if (string.IsNullOrWhiteSpace(request.Body)) throw new ArgumentException("Message body is required.");
            var channel = GetAccessibleChannel(channelId, actor);
            if (channel.Archived) throw new InvalidOperationException("Archived channels are read-only.");
            if (request.LinkedIncidentId is Guid iid)
            {
                var incident = _core.GetIncident(iid);
                if (!channel.ParticipatingAgencyIds.Contains(incident.AgencyId)) throw new InvalidOperationException("Linked incident is outside the channel's participating agencies.");
            }
            var priority = request.Priority.Trim().ToLowerInvariant() switch { "urgent" => "Urgent", "important" => "Important", _ => "Normal" };
            var classification = request.Classification.Trim().ToLowerInvariant() switch { "restricted" => "Restricted", "sensitive" => "Sensitive", "internal" => "Internal", _ => "Operational" };
            var mentions = NormalizeStrings(request.Mentions);
            foreach (var token in request.Body.Split([' ', '\t', '\r', '\n', ',', ';', ':', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (token.Length > 1 && token[0] == '@') AddUnique(mentions, token[1..].TrimEnd('.', '!', '?'));
            var attachments = (request.Attachments ?? []).Select(a => a with
            {
                Id = a.Id == Guid.Empty ? Guid.NewGuid() : a.Id,
                FileName = a.FileName.Trim(),
                ContentType = a.ContentType.Trim(),
                StorageReference = a.StorageReference.Trim(),
                ContentSha256 = NormalizeSha256(a.ContentSha256),
                Classification = string.IsNullOrWhiteSpace(a.Classification) ? classification : a.Classification.Trim()
            }).ToArray();
            if (attachments.Any(a => string.IsNullOrWhiteSpace(a.FileName) || string.IsNullOrWhiteSpace(a.StorageReference))) throw new ArgumentException("Chat attachments require file name and storage reference.");
            var item = new ChatMessageDto(Guid.NewGuid(), channelId, channel.OwnerAgencyId, actor, request.Body.Trim(), priority, classification, request.LinkedIncidentId, mentions, attachments, DateTimeOffset.UtcNow);
            _chatMessages[item.Id] = item;
            _chatChannels[channel.Id] = channel with { UpdatedAtUtc = item.CreatedAtUtc };
            MarkChatReadCore(channelId, actor, item.Id, item.CreatedAtUtc);
            AuditMutation(channel.OwnerAgencyId, actor, "ChatMessageSent", "ChatMessage", item.Id.ToString(), $"{channel.Name}: {item.Body}");
            _idempotency.Remember(request.IdempotencyKey, "FeatureStore.SendChatMessage", idempotencyRequest, item);
            return item;
        }
    }

    public ChatReadStateDto MarkChatRead(Guid channelId, Guid? throughMessageId, string actor)
    {
        lock (_gate)
        {
            _ = GetAccessibleChannel(channelId, actor);
            ChatMessageDto? through = throughMessageId is Guid mid
                ? (_chatMessages.TryGetValue(mid, out var m) && m.ChannelId == channelId ? m : throw new InvalidOperationException("Read marker message does not belong to this channel."))
                : _chatMessages.Values.Where(x => x.ChannelId == channelId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
            return MarkChatReadCore(channelId, actor, through?.Id, through?.CreatedAtUtc ?? DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<ChatSearchResultDto> SearchChat(Guid agencyId, string actor, string query, int limit)
    {
        lock (_gate)
        {
            var actorAgency = ResolveActorAgency(actor);
            if (agencyId != actorAgency) throw new UnauthorizedAccessException("Chat search is limited to the signed-in user's agency context.");
            if (string.IsNullOrWhiteSpace(query)) return [];
            var accessible = _chatChannels.Values.Where(x => CanAccessChatChannelCore(x, actor, actorAgency)).ToDictionary(x => x.Id);
            return _chatMessages.Values.Where(x => accessible.ContainsKey(x.ChannelId) && (x.Body.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || x.Sender.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(limit, 1, 200))
                .Select(x => new ChatSearchResultDto(x.ChannelId, accessible[x.ChannelId].Name, x)).ToArray();
        }
    }

    public bool CanAccessChatChannel(Guid channelId, string actor)
    {
        lock (_gate)
        {
            if (!_chatChannels.TryGetValue(channelId, out var channel) || !TryResolveActorAgency(actor, out var actorAgency)) return false;
            return CanAccessChatChannelCore(channel, actor, actorAgency);
        }
    }

    private ChatChannelDto GetAccessibleChannel(Guid id, string actor)
    {
        if (!_chatChannels.TryGetValue(id, out var channel)) throw new KeyNotFoundException("Chat channel not found.");
        var actorAgency = ResolveActorAgency(actor);
        if (!CanAccessChatChannelCore(channel, actor, actorAgency)) throw new UnauthorizedAccessException("Chat channel access denied.");
        return channel;
    }

    private bool CanAccessChatChannelCore(ChatChannelDto channel, string actor, Guid actorAgency)
    {
        if (!channel.ParticipatingAgencyIds.Contains(actorAgency)) return false;
        if (channel.Type == "Agency") return actorAgency == channel.OwnerAgencyId;
        if (channel.MemberUserIds.Contains(actor, StringComparer.OrdinalIgnoreCase)) return true;
        if (channel.Type is "MutualAid" or "Direct") return false;
        if (channel.Type == "Station" && IsCurrentStationMember(channel, actor, actorAgency)) return true;
        if (channel.Type == "Group" && IsOperationalGroupMember(channel, actor, actorAgency)) return true;
        return channel.AllowedRoles.Any(role => ActorHasRole(actor, role));
    }

    private bool IsCurrentStationMember(ChatChannelDto channel, string actor, Guid actorAgency)
    {
        if (channel.StationId is not Guid stationId || actorAgency != channel.OwnerAgencyId) return false;
        if (!_users.TryGetValue(actor, out var user)) return false;
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(DateTime.Today);
        return _workforceAssignments.Values.Any(x => x.AgencyId == actorAgency && x.StationId == stationId && x.Date == date
            && x.EffectiveAtUtc <= now && (x.EndsAtUtc is null || x.EndsAtUtc > now) && IsFilledState(x.State)
            && ((!string.IsNullOrWhiteSpace(x.EmployeeId) && x.EmployeeId.Equals(actor, StringComparison.OrdinalIgnoreCase))
                || x.EmployeeName.Equals(user.DisplayName, StringComparison.OrdinalIgnoreCase)));
    }

    private bool IsOperationalGroupMember(ChatChannelDto channel, string actor, Guid actorAgency)
    {
        if (channel.OperationalGroupId is not Guid groupId || !_operationalGroups.TryGetValue(groupId, out var group)) return false;
        if (group.AgencyId != actorAgency || !group.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)) return false;
        if (group.Leader.Equals(actor, StringComparison.OrdinalIgnoreCase)) return true;
        return (group.MemberUserIds ?? []).Contains(actor, StringComparer.OrdinalIgnoreCase);
    }

    private bool TryResolveActorAgency(string actor, out Guid agencyId)
    {
        agencyId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(actor) || !_users.TryGetValue(actor.Trim(), out var user)) return false;
        if (user.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase) || user.State.Equals("Revoked", StringComparison.OrdinalIgnoreCase)) return false;
        agencyId = user.AgencyId;
        return true;
    }

    private void ValidateChatMembers(IEnumerable<string> members, IReadOnlyCollection<Guid> agencies)
    {
        foreach (var member in members)
        {
            if (!_users.TryGetValue(member, out var user)) throw new InvalidOperationException($"Chat member '{member}' is not a provisioned DISRoute user.");
            if (!agencies.Contains(user.AgencyId)) throw new InvalidOperationException($"Chat member '{member}' belongs to an agency outside this channel.");
        }
    }

    private bool ActorHasRole(string actor, string role) => _users.TryGetValue(actor, out var user) && user.Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    private int CountUnread(Guid channelId, string actor)
    {
        var key = ReadKey(channelId, actor);
        _chatReadStates.TryGetValue(key, out var state);
        return _chatMessages.Values.Count(x => x.ChannelId == channelId && !x.Sender.Equals(actor, StringComparison.OrdinalIgnoreCase) && (state?.LastReadAtUtc is null || x.CreatedAtUtc > state.LastReadAtUtc));
    }
    private ChatReadStateDto MarkChatReadCore(Guid channelId, string actor, Guid? messageId, DateTimeOffset readAt)
    {
        var state = new ChatReadStateDto(channelId, actor, messageId, readAt, 0);
        _chatReadStates[ReadKey(channelId, actor)] = state;
        return state;
    }
    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Chat attachment SHA-256 must be 64 hexadecimal characters.");
        return normalized;
    }
    private static string ReadKey(Guid channelId, string actor) => $"{channelId:D}|{actor.Trim().ToLowerInvariant()}";
    private static string NormalizeChatType(string value) => value.Trim().ToLowerInvariant() switch { "direct" or "dm" => "Direct", "station" => "Station", "group" => "Group", "supervisor" => "Supervisor", "agency" => "Agency", "mutualaid" or "mutual-aid" or "mutual aid" => "MutualAid", _ => throw new ArgumentException("Chat channel type must be Direct, Station, Group, Supervisor, Agency, or MutualAid.") };
    private static List<string> NormalizeStrings(IEnumerable<string>? values) => (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static void AddUnique(List<string> values, string value) { if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value); }
    private static string DefaultChatName(string type, IReadOnlyList<string> members) => type == "Direct" ? string.Join(" ↔ ", members) : type;
}
