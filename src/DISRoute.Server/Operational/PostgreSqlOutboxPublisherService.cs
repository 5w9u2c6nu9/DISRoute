using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using DISRoute.Server.Realtime;

namespace DISRoute.Server.Operational;

public sealed record DurableRealtimeEventDto(Guid Id, Guid AgencyId, string AggregateType, string AggregateId, string EventType, string PayloadJson, DateTimeOffset OccurredAtUtc);

/// <summary>
/// Publishes committed PostgreSQL outbox events exactly once per claimed row and uses PostgreSQL
/// NOTIFY as the no-extra-infrastructure scale-out signal. Each server relays the same durable event
/// to its locally connected SignalR clients.
/// </summary>
public sealed class PostgreSqlOutboxPublisherService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly HaCoordinatorService _ha;
    private readonly IHubContext<OperationsHub> _hub;
    private readonly ILogger<PostgreSqlOutboxPublisherService> _logger;
    private string _connectionString = string.Empty;
    private bool _enabled;

    public PostgreSqlOutboxPublisherService(IConfiguration configuration, HaCoordinatorService ha, IHubContext<OperationsHub> hub, ILogger<PostgreSqlOutboxPublisherService> logger)
    {
        _configuration = configuration; _ha = ha; _hub = hub; _logger = logger;
    }

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
                if (!_ha.AcceptingWrites) { await Task.Delay(500, stoppingToken); continue; }
                var published = await PublishBatchAsync(stoppingToken);
                if (published == 0) await Task.Delay(250, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                OperationalLog.OutboxPublishFailed(_logger, ex);
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var items = new List<DurableRealtimeEventDto>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = """
                SELECT id, agency_id, aggregate_type, aggregate_id, event_type, payload::text, occurred_at_utc
                FROM outbox_events
                WHERE published_at_utc IS NULL
                  AND authoritative_committed_at_utc IS NOT NULL
                ORDER BY occurred_at_utc
                FOR UPDATE SKIP LOCKED
                LIMIT 50;
                """;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        foreach (var item in items)
        {
            await using var notify = connection.CreateCommand();
            notify.Transaction = tx;
            notify.CommandText = "SELECT pg_notify('disroute_events', @payload)";
            notify.Parameters.AddWithValue("payload", item.Id.ToString("D"));
            await notify.ExecuteScalarAsync(cancellationToken);

            await using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE outbox_events SET published_at_utc=now(), attempts=attempts+1, last_error=NULL WHERE id=@id";
            update.Parameters.AddWithValue("id", item.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);

        // The publishing node also has local clients; deliver directly without waiting for its listener.
        foreach (var item in items) await BroadcastAsync(item, cancellationToken);
        return items.Count;
    }

    private async Task BroadcastAsync(DurableRealtimeEventDto item, CancellationToken cancellationToken)
    {
        await _hub.Clients.Group($"agency:{item.AgencyId:D}").SendAsync("durableEvent", item, cancellationToken);
        if (Guid.TryParse(item.AggregateId, out var id) && item.AggregateType.Contains("Incident", StringComparison.OrdinalIgnoreCase))
            await _hub.Clients.Group($"incident:{id:D}").SendAsync("durableEvent", item, cancellationToken);
    }
}
