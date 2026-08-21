using Microsoft.AspNetCore.SignalR;
using Npgsql;
using DISRoute.Server.Realtime;

namespace DISRoute.Server.Operational;

/// <summary>
/// Relays durable PostgreSQL events to clients connected to standby server nodes. PostgreSQL
/// physical hot standbys cannot execute LISTEN, so recovery-mode nodes use read-only outbox polling
/// until promotion. Once writable, LISTEN/NOTIFY is used normally.
/// </summary>
public sealed class PostgreSqlRealtimeRelayService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly HaCoordinatorService _ha;
    private readonly IHubContext<OperationsHub> _hub;
    private readonly ILogger<PostgreSqlRealtimeRelayService> _logger;
    private string _connectionString = string.Empty;
    private bool _enabled;
    private DateTimeOffset _replicaCursorUtc = DateTimeOffset.UtcNow;
    private Guid _replicaCursorId = Guid.Empty;

    public PostgreSqlRealtimeRelayService(IConfiguration configuration, HaCoordinatorService ha, IHubContext<OperationsHub> hub, ILogger<PostgreSqlRealtimeRelayService> logger)
    { _configuration = configuration; _ha = ha; _hub = hub; _logger = logger; }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _enabled = string.Equals(_configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);
        if (_enabled) _connectionString = SecretReferenceResolver.ResolveConnectionString(_configuration);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(stoppingToken);
                await using var recovery = connection.CreateCommand();
                recovery.CommandText = "SELECT pg_is_in_recovery()";
                var inRecovery = (bool)(await recovery.ExecuteScalarAsync(stoppingToken) ?? false);
                if (inRecovery)
                {
                    await PollReplicaAsync(connection, stoppingToken);
                    continue;
                }

                connection.Notification += (_, args) => _ = RelayNotificationAsync(args.Payload, stoppingToken);
                await using (var listen = connection.CreateCommand())
                {
                    listen.CommandText = "LISTEN disroute_events";
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }
                while (!stoppingToken.IsCancellationRequested) await connection.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                OperationalLog.RealtimeRelayDisconnected(_logger, ex);
                try { await Task.Delay(1000, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task PollReplicaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_ha.AcceptingWrites) return;
            await using (var recovery = connection.CreateCommand())
            {
                recovery.CommandText = "SELECT pg_is_in_recovery()";
                if (!(bool)(await recovery.ExecuteScalarAsync(cancellationToken) ?? false)) return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, agency_id, aggregate_type, aggregate_id, event_type, payload::text, occurred_at_utc
                  FROM outbox_events
                 WHERE occurred_at_utc > @cursorUtc OR (occurred_at_utc = @cursorUtc AND id > @cursorId)
                 ORDER BY occurred_at_utc, id
                 LIMIT 200
                """;
            command.Parameters.AddWithValue("cursorUtc", _replicaCursorUtc);
            command.Parameters.AddWithValue("cursorId", _replicaCursorId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<DurableRealtimeEventDto>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new DurableRealtimeEventDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
            }
            await reader.CloseAsync();
            foreach (var item in rows)
            {
                await BroadcastAsync(item, cancellationToken);
                _replicaCursorUtc = item.OccurredAtUtc;
                _replicaCursorId = item.Id;
            }
            await Task.Delay(500, cancellationToken);
        }
    }

    private async Task RelayNotificationAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            // Primary already broadcasts locally; avoid duplicate local messages there.
            if (_ha.AcceptingWrites) return;
            if (!Guid.TryParse(payload, out var eventId)) return;
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, agency_id, aggregate_type, aggregate_id, event_type, payload::text, occurred_at_utc FROM outbox_events WHERE id=@id";
            command.Parameters.AddWithValue("id", eventId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            var item = new DurableRealtimeEventDto(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6));
            await BroadcastAsync(item, cancellationToken);
        }
        catch (Exception ex) { OperationalLog.RealtimeRelayNotificationFailed(_logger, ex); }
    }

    private async Task BroadcastAsync(DurableRealtimeEventDto item, CancellationToken cancellationToken)
    {
        await _hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("durableEvent", item, cancellationToken);
        if (Guid.TryParse(item.AggregateId, out var aggregateId) && item.AggregateType.Contains("Incident", StringComparison.OrdinalIgnoreCase))
            await _hub.Clients.Group($"incident:{aggregateId:D}").SendAsync("durableEvent", item, cancellationToken);
    }
}
