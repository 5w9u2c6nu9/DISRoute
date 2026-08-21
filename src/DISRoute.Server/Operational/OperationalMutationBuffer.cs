using System.Text.Json;
using System.Threading.Channels;
using System.Collections.Concurrent;

namespace DISRoute.Server.Operational;

public sealed record OperationalMutationEnvelope(
    Guid EventId,
    Guid AgencyId,
    string AggregateType,
    string AggregateId,
    string EventType,
    string Actor,
    string Summary,
    DateTimeOffset OccurredAtUtc,
    string PayloadJson,
    Guid? DurabilityBatchId = null);

public sealed class OperationalMutationBuffer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Channel<OperationalMutationEnvelope> _channel = Channel.CreateUnbounded<OperationalMutationEnvelope>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly ConcurrentDictionary<Guid, byte> _pendingAuthoritativeBatches = new();
    private readonly ConcurrentDictionary<Guid, byte> _abandonedBatches = new();
    private long _queued;
    private long _persisted;
    private long _failed;

    public ChannelReader<OperationalMutationEnvelope> Reader => _channel.Reader;
    public long Queued => Interlocked.Read(ref _queued);
    public long Persisted => Interlocked.Read(ref _persisted);
    public long Failed => Interlocked.Read(ref _failed);
    public int PendingAuthoritativeBatchCount => _pendingAuthoritativeBatches.Count;
    public IReadOnlyList<Guid> PendingAuthoritativeBatchIds => _pendingAuthoritativeBatches.Keys.OrderBy(x => x).ToArray();

    public IDisposable BeginAuthoritativeBatch(Guid batchId) => DurabilityCommandContext.Begin(batchId);

    public void Enqueue(Guid agencyId, string aggregateType, string aggregateId, string eventType, string actor, string summary, object? payload = null)
    {
        var envelope = new OperationalMutationEnvelope(
            Guid.NewGuid(),
            agencyId,
            string.IsNullOrWhiteSpace(aggregateType) ? "Unknown" : aggregateType.Trim(),
            string.IsNullOrWhiteSpace(aggregateId) ? "none" : aggregateId.Trim(),
            string.IsNullOrWhiteSpace(eventType) ? "Mutation" : eventType.Trim(),
            string.IsNullOrWhiteSpace(actor) ? "unknown" : actor.Trim(),
            summary ?? string.Empty,
            DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(payload ?? new { summary }, SerializerOptions),
            DurabilityCommandContext.CurrentBatchId);
        if (envelope.DurabilityBatchId is Guid batchId) _pendingAuthoritativeBatches.TryAdd(batchId, 0);
        if (!_channel.Writer.TryWrite(envelope)) throw new InvalidOperationException("Operational mutation queue is unavailable.");
        Interlocked.Increment(ref _queued);
    }

    public async Task<bool> WaitUntilPersistedAsync(long targetPersisted, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (targetPersisted <= Persisted) return true;
        if (timeout <= TimeSpan.Zero) return false;

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (Persisted < targetPersisted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return false;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(10) ? remaining : TimeSpan.FromMilliseconds(10), cancellationToken);
        }
        return true;
    }

    public void MarkAuthoritativeBatchesCommitted(IEnumerable<Guid> batchIds)
    {
        foreach (var batchId in batchIds) _pendingAuthoritativeBatches.TryRemove(batchId, out _);
    }

    public void AbandonBatch(Guid batchId)
    {
        _pendingAuthoritativeBatches.TryRemove(batchId, out _);
        _abandonedBatches.TryAdd(batchId, 0);
    }

    internal bool IsBatchAbandoned(Guid batchId) => _abandonedBatches.ContainsKey(batchId);
    internal void MarkPersisted() => Interlocked.Increment(ref _persisted);
    internal void MarkFailed() => Interlocked.Increment(ref _failed);
}
