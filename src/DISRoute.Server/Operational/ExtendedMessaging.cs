using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

public sealed partial class FeatureStore
{
    private readonly Dictionary<Guid, OperationalMessageDto> _operationalMessages = [];
    private readonly Dictionary<Guid, MessageReceiptDto> _messageReceipts = [];

    public IReadOnlyList<OperationalMessageDto> OperationalMessages { get { lock (_gate) return _operationalMessages.Values.OrderByDescending(x => x.CreatedAtUtc).ToArray(); } }
    public IReadOnlyList<MessageReceiptDto> MessageReceipts { get { lock (_gate) return _messageReceipts.Values.OrderByDescending(x => x.QueuedAtUtc).ToArray(); } }

    public OperationalMessageDto SendOperationalMessage(CreateOperationalMessageRequest request, string actor)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(request.Body)) throw new ArgumentException("Message body is required.");
            if (request.IncidentId is Guid iid) _core.GetIncident(iid);
            var priority = request.Priority.Trim().ToLowerInvariant() switch { "urgent" => "Urgent", "important" => "Important", _ => "Normal" };
            var classification = request.Classification.Trim().ToLowerInvariant() switch { "restricted" => "Restricted", "sensitive" => "Sensitive", "internal" => "Internal", _ => "Operational" };
            var recipients = (request.ReceiptRecipients ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(request.Recipient) && !recipients.Contains(request.Recipient.Trim(), StringComparer.OrdinalIgnoreCase)) recipients.Add(request.Recipient.Trim());
            var now = DateTimeOffset.UtcNow;
            var item = new OperationalMessageDto(Guid.NewGuid(), request.AgencyId, request.IncidentId, string.IsNullOrWhiteSpace(request.Channel) ? "Operations" : request.Channel.Trim(), string.IsNullOrWhiteSpace(request.Sender) ? actor : request.Sender.Trim(), string.IsNullOrWhiteSpace(request.Recipient) ? null : request.Recipient.Trim(), priority, classification, request.Body.Trim(), string.IsNullOrWhiteSpace(request.ReferenceJson) ? "{}" : request.ReferenceJson.Trim(), recipients.Count == 0 ? "Delivered" : "Queued", now);
            _operationalMessages[item.Id] = item;
            foreach (var recipient in recipients)
            {
                var receipt = new MessageReceiptDto(Guid.NewGuid(), item.Id, recipient, null, "Queued", now, null, null, null);
                _messageReceipts[receipt.Id] = receipt;
            }
            AuditMutation(item.AgencyId, actor, "OperationalMessageSent", "Message", item.Id.ToString(), $"{item.Priority} {item.Channel}: {item.Body}");
            return item;
        }
    }

    public IReadOnlyList<OperationalMessageDto> GetOperationalMessages(Guid agencyId, Guid? incidentId, string? recipient, string? channel)
    {
        lock (_gate)
        {
            IEnumerable<OperationalMessageDto> source = _operationalMessages.Values.Where(x => x.AgencyId == agencyId);
            if (incidentId is Guid iid) source = source.Where(x => x.IncidentId == iid);
            if (!string.IsNullOrWhiteSpace(channel)) source = source.Where(x => x.Channel.Equals(channel.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(recipient))
            {
                var user = recipient.Trim();
                var ids = _messageReceipts.Values.Where(x => x.Recipient.Equals(user, StringComparison.OrdinalIgnoreCase)).Select(x => x.MessageId).ToHashSet();
                source = source.Where(x => x.Recipient is null || x.Recipient.Equals(user, StringComparison.OrdinalIgnoreCase) || ids.Contains(x.Id));
            }
            return source.OrderByDescending(x => x.CreatedAtUtc).ToArray();
        }
    }

    public IReadOnlyList<MessageReceiptDto> GetQueuedReceipts(string recipient, string? deviceId)
    {
        lock (_gate)
        {
            IEnumerable<MessageReceiptDto> source = _messageReceipts.Values.Where(x => x.Recipient.Equals(recipient, StringComparison.OrdinalIgnoreCase) && x.State == "Queued");
            if (!string.IsNullOrWhiteSpace(deviceId)) source = source.Where(x => x.DeviceId is null || x.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            return source.OrderBy(x => x.QueuedAtUtc).ToArray();
        }
    }

    public MessageReceiptDto UpdateMessageReceipt(Guid receiptId, UpdateMessageReceiptRequest request, string actor)
    {
        lock (_gate)
        {
            if (!_messageReceipts.TryGetValue(receiptId, out var current)) throw new KeyNotFoundException("Message receipt not found.");
            if (!current.Recipient.Equals(request.Recipient, StringComparison.OrdinalIgnoreCase) && !actor.Equals("local-development", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Receipt may only be updated by its recipient.");
            var state = request.State.Trim().ToLowerInvariant() switch { "delivered" => "Delivered", "read" => "Read", "acknowledged" or "ack" => "Acknowledged", _ => throw new ArgumentException("Receipt state must be Delivered, Read, or Acknowledged.") };
            var now = DateTimeOffset.UtcNow;
            var updated = current with
            {
                DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? current.DeviceId : request.DeviceId.Trim(),
                State = state,
                DeliveredAtUtc = state is "Delivered" or "Read" or "Acknowledged" ? current.DeliveredAtUtc ?? now : current.DeliveredAtUtc,
                ReadAtUtc = state is "Read" or "Acknowledged" ? current.ReadAtUtc ?? now : current.ReadAtUtc,
                AcknowledgedAtUtc = state == "Acknowledged" ? current.AcknowledgedAtUtc ?? now : current.AcknowledgedAtUtc
            };
            _messageReceipts[receiptId] = updated;
            if (_operationalMessages.TryGetValue(updated.MessageId, out var message))
            {
                var receipts = _messageReceipts.Values.Where(x => x.MessageId == message.Id).ToArray();
                var aggregateState = receipts.Length == 0 ? message.State : receipts.All(x => x.State == "Acknowledged") ? "Acknowledged" : receipts.All(x => x.State is "Read" or "Acknowledged") ? "Read" : receipts.All(x => x.State is "Delivered" or "Read" or "Acknowledged") ? "Delivered" : "Queued";
                _operationalMessages[message.Id] = message with { State = aggregateState };
                AuditMutation(message.AgencyId, actor, "MessageReceiptUpdated", "Message", message.Id.ToString(), $"{updated.Recipient} -> {state}");
            }
            return updated;
        }
    }

    public IncidentActivityDto PromoteMessageToNarrative(Guid messageId, PromoteMessageToNarrativeRequest request)
    {
        lock (_gate)
        {
            if (!_operationalMessages.TryGetValue(messageId, out var message)) throw new KeyNotFoundException("Message not found.");
            if (message.IncidentId is not Guid incidentId) throw new InvalidOperationException("Only incident-scoped messages can be promoted to official narrative.");
            var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? "Promoted message" : request.Prefix.Trim();
            var provenance = $"{prefix} from {message.Sender} at {message.CreatedAtUtc:O}: {message.Body}";
            var activity = _core.AddNarrative(incidentId, new AddNarrativeRequest(provenance, request.IdempotencyKey ?? $"promote-message:{message.Id:D}"), request.Actor);
            AuditMutation(message.AgencyId, request.Actor, "MessagePromotedToNarrative", "Message", message.Id.ToString(), provenance);
            return activity;
        }
    }
}
