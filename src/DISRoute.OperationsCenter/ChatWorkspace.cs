using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DISRoute.Client.Common;
using DISRoute.Contracts.Operational;

namespace DISRoute.OperationsCenter;

public sealed partial class MainWindow
{
    private ChatMessageDto[] _chatMessages = [];
    private readonly OperationsRealtimeClient _chatRealtime = new();
    private readonly Dictionary<string, PresenceDto> _chatPresence = new(StringComparer.Ordinal);
    private bool _chatRealtimeStarted;

    private void InitializeChatRealtime()
    {
        _chatRealtime.ChatMessageReceived += item => Dispatcher.UIThread.Post(() => ChatRealtimeMessage(item));
        _chatRealtime.ChatChannelChanged += item => Dispatcher.UIThread.Post(async () => await ChatRealtimeChannelChangedAsync(item));
        _chatRealtime.ChatReadStateChanged += item => Dispatcher.UIThread.Post(async () => await ChatRealtimeReadStateAsync(item));
        _chatRealtime.PresenceChanged += item => Dispatcher.UIThread.Post(() => ChatRealtimePresenceChanged(item));
        _chatRealtime.PresenceLeft += item => Dispatcher.UIThread.Post(() => ChatRealtimePresenceLeft(item));
        _chatRealtime.ConnectionStateChanged += state => Dispatcher.UIThread.Post(() => ChatRealtimeStateText.Text = "Realtime: " + state);
        Closed += async (_, _) => await _chatRealtime.DisposeAsync();
    }

    private async Task EnsureChatRealtimeAsync(Guid agencyId)
    {
        if (_chatRealtimeStarted || agencyId == Guid.Empty) return;
        _chatRealtimeStarted = true;
        await _chatRealtime.StartAsync(agencyId);
    }

    private async Task RefreshChatAsync(Guid agencyId, Guid? selectedChannelId = null)
    {
        if (agencyId == Guid.Empty) return;
        try
        {
            await EnsureChatRealtimeAsync(agencyId);
            var channelsTask = _features.GetChatChannelsAsync(agencyId, includeArchived: true);
            var presenceTask = _features.GetPresenceAsync();
            var groupsTask = _features.GetOperationalGroupsAsync();
            await Task.WhenAll(channelsTask, presenceTask, groupsTask);
            SetPresenceSnapshot(presenceTask.Result);
            ChatOperationalGroupItems.ItemsSource = groupsTask.Result.Where(x => x.AgencyId == agencyId && x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name).Select(x => new ChatOperationalGroupRow(x.Id, $"{x.Name} [{x.Type}] — {x.IncidentId:D}")).ToArray();
            BindChatChannelRows(channelsTask.Result, selectedChannelId);
            await LoadSelectedChatChannelAsync();
        }
        catch (Exception ex)
        {
            ChatStatusText.Text = "Chat degraded — " + ex.Message;
        }
    }

    private void BindChatChannelRows(IEnumerable<ChatChannelSummaryDto> channels, Guid? selectedChannelId)
    {
        var rows = channels.Select(x => new ChatChannelRow(x.Channel.Id, x.Channel.Archived, x.Channel.Type, x.UnreadCount,
            $"{(x.Channel.Archived ? "[ARCHIVED] " : string.Empty)}{x.Channel.Name}  [{x.Channel.Type}]  unread={x.UnreadCount}")).ToArray();
        ChatChannelItems.ItemsSource = rows;
        ChatChannelItems.SelectedItem = selectedChannelId is Guid id ? rows.FirstOrDefault(x => x.Id == id) : rows.FirstOrDefault();
    }

    private void SetPresenceSnapshot(IEnumerable<PresenceDto> items)
    {
        _chatPresence.Clear();
        foreach (var item in items) _chatPresence[item.ConnectionId] = item;
        RenderPresence();
    }

    private void RenderPresence() => ChatPresenceItems.ItemsSource = _chatPresence.Values.OrderBy(x => x.User).Select(x =>
        $"{x.User}  {x.Panel}  {x.Field ?? string.Empty}  {(x.IncidentId is Guid iid ? iid.ToString("D", CultureInfo.InvariantCulture) : string.Empty)}").ToArray();

    private async void ChatChannelSelectionChanged(object? sender, SelectionChangedEventArgs e) => await LoadSelectedChatChannelAsync();

    private async Task LoadSelectedChatChannelAsync()
    {
        if (ChatChannelItems.SelectedItem is not ChatChannelRow row)
        {
            _chatMessages = [];
            ChatMessageItems.ItemsSource = null;
            ChatStatusText.Text = "Select a channel.";
            await _chatRealtime.SwitchChatChannelAsync(null);
            return;
        }
        try
        {
            await _chatRealtime.SwitchChatChannelAsync(row.Id);
            _chatMessages = await _features.GetChatMessagesAsync(row.Id, 200);
            RenderChatMessages();
            ChatStatusText.Text = $"{row.Type} channel • unread={row.UnreadCount} • {_chatMessages.Length} loaded";
        }
        catch (Exception ex) { ChatStatusText.Text = ex.Message; }
    }

    private void RenderChatMessages()
    {
        ChatMessageItems.ItemsSource = _chatMessages.OrderBy(x => x.CreatedAtUtc).Select(x => new ChatMessageRow(x.Id,
            $"{x.CreatedAtUtc.LocalDateTime:G}  {x.Sender}  [{x.Priority}/{x.Classification}]\n{x.Body}" +
            (x.LinkedIncidentId is Guid iid ? $"\nincident={iid:D}" : string.Empty) +
            (x.Mentions.Count > 0 ? $"\nmentions={string.Join(", ", x.Mentions)}" : string.Empty) +
            (x.Attachments.Count > 0 ? $"\nattachments={string.Join(", ", x.Attachments.Select(a => a.FileName))}" : string.Empty))).ToArray();
    }

    private async void ChatRealtimeMessage(ChatMessageDto item)
    {
        if (ChatChannelItems.SelectedItem is ChatChannelRow row && row.Id == item.ChannelId)
        {
            if (_chatMessages.All(x => x.Id != item.Id))
            {
                _chatMessages = _chatMessages.Append(item).OrderBy(x => x.CreatedAtUtc).TakeLast(200).ToArray();
                RenderChatMessages();
                ChatStatusText.Text = $"Live message received from {item.Sender} at {item.CreatedAtUtc.LocalDateTime:G}.";
            }
            try { await _features.MarkChatReadAsync(row.Id, item.Id); } catch { }
        }
        try { await RefreshChatChannelListAsync(item.ChannelId); } catch { }
    }

    private async Task ChatRealtimeChannelChangedAsync(ChatChannelDto item)
    {
        var selected = (ChatChannelItems.SelectedItem as ChatChannelRow)?.Id;
        await RefreshChatChannelListAsync(selected ?? item.Id);
    }

    private async Task ChatRealtimeReadStateAsync(ChatReadStateDto item)
    {
        var selected = (ChatChannelItems.SelectedItem as ChatChannelRow)?.Id;
        await RefreshChatChannelListAsync(selected ?? item.ChannelId);
    }

    private async Task RefreshChatChannelListAsync(Guid? selectedChannelId)
    {
        if (_workforceAgencyId == Guid.Empty) return;
        var channels = await _features.GetChatChannelsAsync(_workforceAgencyId, includeArchived: true);
        BindChatChannelRows(channels, selectedChannelId);
    }

    private void ChatRealtimePresenceChanged(PresenceDto item)
    {
        _chatPresence[item.ConnectionId] = item;
        RenderPresence();
    }

    private void ChatRealtimePresenceLeft(PresenceDto item)
    {
        _chatPresence.Remove(item.ConnectionId);
        RenderPresence();
    }

    private async void ChatCreateChannelClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty) return;
        try
        {
            var type = (ChatTypeItems.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Group";
            Guid? stationId = Guid.TryParse(ChatStationIdText.Text, out var sid) ? sid : null;
            Guid? incidentId = Guid.TryParse(ChatIncidentIdText.Text, out var iid) ? iid : null;
            Guid? operationalGroupId = (ChatOperationalGroupItems.SelectedItem as ChatOperationalGroupRow)?.Id;
            var partners = ParseGuids(ChatPartnerAgencyText.Text);
            var members = ParseStrings(ChatMembersText.Text);
            var roles = ParseStrings(ChatRolesText.Text);
            var created = await _features.CreateChatChannelAsync(new CreateChatChannelRequest(_workforceAgencyId, type, ChatChannelNameText.Text ?? string.Empty, ChatTopicText.Text, stationId, incidentId, partners, members, roles, operationalGroupId));
            ChatChannelNameText.Text = string.Empty;
            ChatStatusText.Text = $"Created {created.Type} channel {created.Name}.";
            await RefreshChatAsync(_workforceAgencyId, created.Id);
        }
        catch (Exception ex) { ChatStatusText.Text = ex.Message; }
    }

    private async void ChatSendClicked(object? sender, RoutedEventArgs e)
    {
        if (ChatChannelItems.SelectedItem is not ChatChannelRow row || string.IsNullOrWhiteSpace(ChatMessageBodyText.Text)) return;
        try
        {
            Guid? incidentId = Guid.TryParse(ChatMessageIncidentText.Text, out var iid) ? iid : null;
            var attachments = new List<ChatAttachmentReferenceDto>();
            if (!string.IsNullOrWhiteSpace(ChatAttachmentStorageText.Text))
                attachments.Add(new ChatAttachmentReferenceDto(Guid.NewGuid(), ChatAttachmentNameText.Text?.Trim() ?? "attachment", ChatAttachmentTypeText.Text?.Trim() ?? "application/octet-stream", ChatAttachmentStorageText.Text.Trim(), string.IsNullOrWhiteSpace(ChatAttachmentHashText.Text) ? null : ChatAttachmentHashText.Text.Trim(), "Internal"));
            var sent = await _features.SendChatMessageAsync(row.Id, new SendChatMessageRequest(ChatMessageBodyText.Text.Trim(), ChatPriorityText.Text?.Trim() ?? "Normal", ChatClassificationText.Text?.Trim() ?? "Operational", incidentId, ParseStrings(ChatMentionsText.Text), attachments));
            ChatMessageBodyText.Text = string.Empty;
            ChatAttachmentStorageText.Text = string.Empty;
            ChatStatusText.Text = $"Message sent at {sent.CreatedAtUtc.LocalDateTime:G}.";
            if (_chatMessages.All(x => x.Id != sent.Id))
            {
                _chatMessages = _chatMessages.Append(sent).OrderBy(x => x.CreatedAtUtc).TakeLast(200).ToArray();
                RenderChatMessages();
            }
        }
        catch (Exception ex) { ChatStatusText.Text = ex.Message; }
    }

    private async void ChatMarkReadClicked(object? sender, RoutedEventArgs e)
    {
        if (ChatChannelItems.SelectedItem is not ChatChannelRow row) return;
        try
        {
            await _features.MarkChatReadAsync(row.Id, _chatMessages.LastOrDefault()?.Id);
            await RefreshChatChannelListAsync(row.Id);
        }
        catch (Exception ex) { ChatStatusText.Text = ex.Message; }
    }

    private async void ChatArchiveClicked(object? sender, RoutedEventArgs e)
    {
        if (ChatChannelItems.SelectedItem is not ChatChannelRow row) return;
        try
        {
            await _features.ArchiveChatChannelAsync(row.Id, !row.Archived);
            await RefreshChatAsync(_workforceAgencyId, row.Id);
        }
        catch (Exception ex) { ChatStatusText.Text = ex.Message; }
    }

    private async void ChatSearchClicked(object? sender, RoutedEventArgs e)
    {
        if (_workforceAgencyId == Guid.Empty || string.IsNullOrWhiteSpace(ChatSearchText.Text)) return;
        try
        {
            var results = await _features.SearchChatAsync(_workforceAgencyId, ChatSearchText.Text.Trim(), 100);
            ChatSearchItems.ItemsSource = results.Select(x => $"{x.Message.CreatedAtUtc.LocalDateTime:G}  {x.ChannelName}  {x.Message.Sender}: {x.Message.Body}");
            ChatStatusText.Text = $"Search returned {results.Length} result(s).";
        }
        catch (Exception ex) { ChatStatusText.Text = ex.Message; }
    }

    private static string[] ParseStrings(string? text) => (text ?? string.Empty).Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static Guid[] ParseGuids(string? text) => ParseStrings(text).Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
}

public sealed record ChatChannelRow(Guid Id, bool Archived, string Type, int UnreadCount, string Display)
{
    public override string ToString() => Display;
}
public sealed record ChatMessageRow(Guid Id, string Display)
{
    public override string ToString() => Display;
}

public sealed record ChatOperationalGroupRow(Guid Id, string Display)
{
    public override string ToString() => Display;
}
