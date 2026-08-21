namespace DISRoute.Contracts.Operational;

public sealed record OperationalMessageDto(
    Guid Id, Guid AgencyId, Guid? IncidentId, string Channel, string Sender, string? Recipient,
    string Priority, string Classification, string Body, string ReferenceJson, string State, DateTimeOffset CreatedAtUtc);

public sealed record MessageReceiptDto(
    Guid Id, Guid MessageId, string Recipient, string? DeviceId, string State,
    DateTimeOffset QueuedAtUtc, DateTimeOffset? DeliveredAtUtc, DateTimeOffset? ReadAtUtc, DateTimeOffset? AcknowledgedAtUtc);

public sealed record CreateOperationalMessageRequest(
    Guid AgencyId, Guid? IncidentId, string Channel, string Sender, string? Recipient,
    string Priority, string Classification, string Body, string? ReferenceJson, IReadOnlyList<string>? ReceiptRecipients);

public sealed record UpdateMessageReceiptRequest(string Recipient, string? DeviceId, string State);
public sealed record PromoteMessageToNarrativeRequest(string Actor, string? Prefix, string? IdempotencyKey);

public sealed record ChatAttachmentReferenceDto(Guid Id, string FileName, string ContentType, string StorageReference, string? ContentSha256, string Classification);
public sealed record ChatChannelDto(
    Guid Id, Guid OwnerAgencyId, string Type, string Name, string Topic, Guid? StationId, Guid? IncidentId,
    IReadOnlyList<Guid> ParticipatingAgencyIds, IReadOnlyList<string> MemberUserIds, IReadOnlyList<string> AllowedRoles,
    bool Archived, string CreatedBy, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid? OperationalGroupId = null);
public sealed record ChatMessageDto(
    Guid Id, Guid ChannelId, Guid OwnerAgencyId, string Sender, string Body, string Priority, string Classification,
    Guid? LinkedIncidentId, IReadOnlyList<string> Mentions, IReadOnlyList<ChatAttachmentReferenceDto> Attachments, DateTimeOffset CreatedAtUtc);
public sealed record ChatReadStateDto(Guid ChannelId, string UserId, Guid? LastReadMessageId, DateTimeOffset? LastReadAtUtc, int UnreadCount);
public sealed record ChatChannelSummaryDto(ChatChannelDto Channel, int UnreadCount, ChatMessageDto? LastMessage);
public sealed record ChatSearchResultDto(Guid ChannelId, string ChannelName, ChatMessageDto Message);
public sealed record CreateChatChannelRequest(
    Guid OwnerAgencyId, string Type, string Name, string? Topic, Guid? StationId, Guid? IncidentId,
    IReadOnlyList<Guid>? ParticipatingAgencyIds, IReadOnlyList<string>? MemberUserIds, IReadOnlyList<string>? AllowedRoles, Guid? OperationalGroupId = null);
public sealed record ArchiveChatChannelRequest(bool Archived);
public sealed record SendChatMessageRequest(
    string Body, string Priority, string Classification, Guid? LinkedIncidentId,
    IReadOnlyList<string>? Mentions, IReadOnlyList<ChatAttachmentReferenceDto>? Attachments, string? IdempotencyKey = null);
public sealed record MarkChatReadRequest(Guid? ThroughMessageId);
