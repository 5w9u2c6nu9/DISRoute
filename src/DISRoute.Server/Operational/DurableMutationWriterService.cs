using System.Text.Json;
using Npgsql;

namespace DISRoute.Server.Operational;

public sealed record MutationJournalStatusDto(string Provider, long Queued, long Persisted, long Failed, int PendingAuthoritativeBatches, DateTimeOffset? LastPersistedAtUtc, string? LastError);

internal sealed record LocalMutationJournalRecord(
    Guid EventId, Guid AgencyId, string AggregateType, string AggregateId, string EventType,
    string Actor, string Summary, string PayloadJson, DateTimeOffset OccurredAtUtc, DateTimeOffset PersistedAtUtc);

/// <summary>
/// Persists every authoritative mutation to an append-only journal. In PostgreSQL mode the journal
/// row and transactional outbox row are committed together so downstream realtime/integration work
/// can be retried without losing the authoritative audit trail.
/// </summary>
public sealed class DurableMutationWriterService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly OperationalMutationBuffer _queue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DurableMutationWriterService> _logger;
    private readonly HaCoordinatorService _ha;
    private string _provider = "FileJournal";
    private string _connectionString = string.Empty;
    private string _journalPath = string.Empty;
    private DateTimeOffset? _lastPersistedAtUtc;
    private string? _lastError;

    public DurableMutationWriterService(OperationalMutationBuffer queue, IConfiguration configuration, ILogger<DurableMutationWriterService> logger, HaCoordinatorService ha)
    {
        _queue = queue;
        _configuration = configuration;
        _logger = logger;
        _ha = ha;
    }

    public MutationJournalStatusDto Status => new(_provider, _queue.Queued, _queue.Persisted, _queue.Failed, _queue.PendingAuthoritativeBatchCount, _lastPersistedAtUtc, _lastError);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _provider = _configuration["Persistence:Provider"]?.Trim() ?? "FileCheckpoint";
        if (_provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            _provider = "PostgreSQL";
            _connectionString = SecretReferenceResolver.ResolveConnectionString(_configuration);
        }
        else
        {
            _provider = "FileJournal";
            var configured = _configuration["Persistence:JournalPath"];
            var checkpoint = _configuration["Persistence:CheckpointPath"];
            var baseDirectory = !string.IsNullOrWhiteSpace(checkpoint)
                ? Path.GetDirectoryName(Path.GetFullPath(checkpoint)) ?? AppContext.BaseDirectory
                : Path.Combine(AppContext.BaseDirectory, "data");
            _journalPath = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(baseDirectory, "disroute-command-journal.ndjson")
                : Path.GetFullPath(configured);
            Directory.CreateDirectory(Path.GetDirectoryName(_journalPath) ?? AppContext.BaseDirectory);
            await EnsureFileJournalAsync(cancellationToken);
        }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        if (item.DurabilityBatchId is Guid abandonedBatch && _queue.IsBatchAbandoned(abandonedBatch))
                        {
                            _queue.MarkPersisted();
                            break;
                        }
                        if (_provider == "PostgreSQL" && !_ha.AcceptingWrites)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                            continue;
                        }
                        if (_provider == "PostgreSQL") await PersistPostgreSqlAsync(item, stoppingToken);
                        else await PersistFileAsync(item, stoppingToken);
                        _queue.MarkPersisted();
                        _lastPersistedAtUtc = DateTimeOffset.UtcNow;
                        _lastError = null;
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _queue.MarkFailed();
                        _lastError = ex.Message;
                        OperationalLog.MutationPersistFailed(_logger, item.EventId, item.EventType, ex);
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task PersistPostgreSqlAsync(OperationalMutationEnvelope item, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        await using (var journal = connection.CreateCommand())
        {
            journal.Transaction = tx;
            journal.CommandText = """
                INSERT INTO command_journal(event_id, agency_id, aggregate_type, aggregate_id, event_type, actor, summary, payload_json, occurred_at_utc, durability_batch_id, authoritative_committed_at_utc)
                VALUES (@id, @agency, @aggregateType, @aggregateId, @eventType, @actor, @summary, CAST(@payload AS jsonb), @occurred, @batch, NULL)
                ON CONFLICT(event_id) DO NOTHING;
                """;
            journal.Parameters.AddWithValue("id", item.EventId);
            journal.Parameters.AddWithValue("agency", item.AgencyId);
            journal.Parameters.AddWithValue("aggregateType", item.AggregateType);
            journal.Parameters.AddWithValue("aggregateId", item.AggregateId);
            journal.Parameters.AddWithValue("eventType", item.EventType);
            journal.Parameters.AddWithValue("actor", item.Actor);
            journal.Parameters.AddWithValue("summary", item.Summary);
            journal.Parameters.AddWithValue("payload", item.PayloadJson);
            journal.Parameters.AddWithValue("occurred", item.OccurredAtUtc);
            journal.Parameters.AddWithValue("batch", (object?)item.DurabilityBatchId ?? DBNull.Value);
            await journal.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var outbox = connection.CreateCommand())
        {
            outbox.Transaction = tx;
            outbox.CommandText = """
                INSERT INTO outbox_events(id, agency_id, aggregate_type, aggregate_id, event_type, payload, occurred_at_utc, durability_batch_id, authoritative_committed_at_utc)
                VALUES (@id, @agency, @aggregateType, @aggregateId, @eventType, CAST(@payload AS jsonb), @occurred, @batch, NULL)
                ON CONFLICT(id) DO NOTHING;
                """;
            outbox.Parameters.AddWithValue("id", item.EventId);
            outbox.Parameters.AddWithValue("agency", item.AgencyId);
            outbox.Parameters.AddWithValue("aggregateType", item.AggregateType);
            outbox.Parameters.AddWithValue("aggregateId", item.AggregateId);
            outbox.Parameters.AddWithValue("eventType", item.EventType);
            outbox.Parameters.AddWithValue("payload", item.PayloadJson);
            outbox.Parameters.AddWithValue("occurred", item.OccurredAtUtc);
            outbox.Parameters.AddWithValue("batch", (object?)item.DurabilityBatchId ?? DBNull.Value);
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private async Task EnsureFileJournalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_journalPath)) return;
        await using var stream = new FileStream(_journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task PersistFileAsync(OperationalMutationEnvelope item, CancellationToken cancellationToken)
    {
        var record = new LocalMutationJournalRecord(
            item.EventId, item.AgencyId, item.AggregateType, item.AggregateId, item.EventType,
            item.Actor, item.Summary, item.PayloadJson, item.OccurredAtUtc, DateTimeOffset.UtcNow);
        var line = JsonSerializer.Serialize(record, SerializerOptions) + Environment.NewLine;
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        await using var stream = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

}
