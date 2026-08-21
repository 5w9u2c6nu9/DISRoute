using System.Data;
using System.Globalization;
using System.Text.Json;
using DISRoute.Contracts.Operational;
using Npgsql;

namespace DISRoute.Server.Operational;

public sealed record HaNodeStatusDto(
    string NodeId,
    Guid InstanceId,
    string Role,
    string State,
    bool AcceptingWrites,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatUtc,
    string? LastError,
    string Site,
    string ConfiguredRole,
    long LeaseEpoch,
    DateTimeOffset? DrainedUntilUtc,
    DateTimeOffset? LastTransitionUtc);

/// <summary>
/// Coordinates application-primary ownership. Dispatch peers sharing the same PostgreSQL primary
/// use a session advisory lock as the write fence. Geographic DR never auto-promotes by default;
/// it also requires an external witness lock and a locally writable PostgreSQL database before the
/// application can become authoritative. Losing either the lease session or database connectivity
/// fences the node immediately.
/// </summary>
public sealed class HaCoordinatorService : BackgroundService
{
    private const long PrimaryAdvisoryLockKey = 0x4452534F555445; // stable DISRoute application-primary lock key
    private readonly IConfiguration _configuration;
    private readonly ILogger<HaCoordinatorService> _logger;
    private readonly IServiceProvider _services;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly SemaphoreSlim _leaseGate = new(1, 1);
    private NpgsqlConnection? _leaseConnection;
    private string _nodeId = Environment.MachineName;
    private string _site = "Dispatch";
    private string _configuredRole = "LocalStandby";
    private string _state = "Standalone";
    private string _role = "Standalone";
    private bool _acceptingWrites = true;
    private bool _ownsLease;
    private bool _enabled;
    private bool _externalWitness;
    private bool _allowAutomaticGeographicPromotion;
    private string _databaseTopology = HaDatabaseTopologySafety.SharedPrimary;
    private string _databaseFencingMode = HaDatabaseTopologySafety.NoExternalFencing;
    private string _connectionString = string.Empty;
    private string _leaseConnectionString = string.Empty;
    private DateTimeOffset _lastHeartbeatUtc = DateTimeOffset.UtcNow;
    private string? _lastError;
    private long _leaseEpoch;
    private long _drainedUntilUtcTicks;
    private long _lastTransitionUtcTicks;
    private int _heartbeatSeconds = 2;
    private int _staleNodeSeconds = 10;
    private int _maxPromotionLagSeconds = 10;
    private long _maxPromotionLagBytes = 16 * 1024 * 1024;

    public HaCoordinatorService(IConfiguration configuration, ILogger<HaCoordinatorService> logger, IServiceProvider services)
    {
        _configuration = configuration;
        _logger = logger;
        _services = services;
    }

    public bool AcceptingWrites => Volatile.Read(ref _acceptingWrites);
    internal bool OwnsAuthoritativeLease => _ownsLease;

    public HaNodeStatusDto Status => new(
        _nodeId,
        _instanceId,
        _role,
        _state,
        AcceptingWrites,
        _startedAtUtc,
        _lastHeartbeatUtc,
        _lastError,
        _site,
        _configuredRole,
        Interlocked.Read(ref _leaseEpoch),
        DrainedUntilUtc,
        LastTransitionUtc);

    /// <summary>
    /// Serializes an authoritative PostgreSQL commit against HA lease release/election on this
    /// process. The scoped lease holds the HA gate until the caller commits or rolls back, so a
    /// planned drain or coordinator transition cannot release leadership underneath a checkpoint.
    /// The live advisory/witness session is proved with real DB I/O on entry and again immediately
    /// before commit.
    /// </summary>
    internal async ValueTask<HaAuthoritativeCommitLease> EnterAuthoritativeCommitAsync(CancellationToken cancellationToken)
    {
        await _leaseGate.WaitAsync(cancellationToken);
        try
        {
            var token = await ValidateAuthoritativeCommitLeaseCoreAsync(allowPromotingLease: false, expectedToken: null, cancellationToken);
            return new HaAuthoritativeCommitLease(this, token);
        }
        catch
        {
            _leaseGate.Release();
            throw;
        }
    }

    // Promotion reload is invoked by TryAcquireLeaseCoreAsync while RunCoordinatorIterationAsync
    // already owns _leaseGate. Re-entering the semaphore would deadlock, so this narrowly scoped
    // validation path is used only for that pre-AcceptingWrites promotion checkpoint.
    internal Task<HaNodeStatusDto> ValidatePromotingCommitWhileLeaseGateHeldAsync(
        HaNodeStatusDto? expectedToken,
        CancellationToken cancellationToken) =>
        ValidateAuthoritativeCommitLeaseCoreAsync(allowPromotingLease: true, expectedToken, cancellationToken);

    private async Task<HaNodeStatusDto> ValidateAuthoritativeCommitLeaseCoreAsync(
        bool allowPromotingLease,
        HaNodeStatusDto? expectedToken,
        CancellationToken cancellationToken)
    {
        var permittedState = AcceptingWrites || (allowPromotingLease && _ownsLease && _state.Equals("Promoting", StringComparison.Ordinal));
        if (!_enabled || !_ownsLease || !permittedState || _leaseConnection is null)
            throw new InvalidOperationException("Authoritative PostgreSQL commit refused because this node no longer holds an active HA lease.");

        await VerifyLeaseSessionAsync(cancellationToken);
        var current = Status;
        if (expectedToken is not null &&
            (!current.NodeId.Equals(expectedToken.NodeId, StringComparison.OrdinalIgnoreCase) ||
             current.InstanceId != expectedToken.InstanceId ||
             current.LeaseEpoch != expectedToken.LeaseEpoch))
        {
            throw new InvalidOperationException(
                $"Authoritative PostgreSQL commit lease changed before commit. Expected={expectedToken.NodeId}/{expectedToken.InstanceId:D}/epoch-{expectedToken.LeaseEpoch}; " +
                $"current={current.NodeId}/{current.InstanceId:D}/epoch-{current.LeaseEpoch}.");
        }

        return current;
    }

    internal sealed class HaAuthoritativeCommitLease : IDisposable
    {
        private HaCoordinatorService? _owner;
        internal HaNodeStatusDto Token { get; }

        internal HaAuthoritativeCommitLease(HaCoordinatorService owner, HaNodeStatusDto token)
        {
            _owner = owner;
            Token = token;
        }

        internal async Task VerifyBeforeCommitAsync(CancellationToken cancellationToken)
        {
            var owner = _owner ?? throw new ObjectDisposedException(nameof(HaAuthoritativeCommitLease));
            _ = await owner.ValidateAuthoritativeCommitLeaseCoreAsync(allowPromotingLease: false, Token, cancellationToken);
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?._leaseGate.Release();
        }
    }

    private DateTimeOffset? DrainedUntilUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _drainedUntilUtcTicks);
            return ticks <= 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    private DateTimeOffset? LastTransitionUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastTransitionUtcTicks);
            return ticks <= 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _nodeId = _configuration["HighAvailability:NodeId"]?.Trim() ?? Environment.MachineName;
        if (string.IsNullOrWhiteSpace(_nodeId)) _nodeId = Environment.MachineName;
        _site = NormalizeSite(_configuration["HighAvailability:Site"]);
        _configuredRole = NormalizeConfiguredRole(_configuration["HighAvailability:ConfiguredRole"]);
        _heartbeatSeconds = Math.Clamp(_configuration.GetValue("HighAvailability:HeartbeatSeconds", 2), 1, 30);
        _staleNodeSeconds = Math.Clamp(_configuration.GetValue("HighAvailability:StaleNodeSeconds", 10), _heartbeatSeconds * 2, 300);
        _maxPromotionLagSeconds = Math.Clamp(_configuration.GetValue("HighAvailability:MaxPromotionLagSeconds", 10), 0, 300);
        _maxPromotionLagBytes = Math.Max(0, _configuration.GetValue<long>("HighAvailability:MaxPromotionLagBytes", 16 * 1024 * 1024));
        _allowAutomaticGeographicPromotion = _configuration.GetValue("HighAvailability:AllowAutomaticGeographicPromotion", false);
        _enabled = _configuration.GetValue("HighAvailability:Enabled", false);
        var provider = _configuration["Persistence:Provider"]?.Trim() ?? "FileCheckpoint";
        if (!_enabled)
        {
            _role = "Standalone";
            _state = "Healthy";
            _acceptingWrites = true;
        }
        else if (!provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("HighAvailability:Enabled requires PostgreSQL persistence so primary fencing is durable.");
        }
        else
        {
            _connectionString = SecretReferenceResolver.ResolveConnectionString(_configuration);
            var witnessConnectionString = SecretReferenceResolver.ResolveOptional(_configuration, "HighAvailability:WitnessConnectionString");
            _externalWitness = !string.IsNullOrWhiteSpace(witnessConnectionString);
            _leaseConnectionString = _externalWitness ? witnessConnectionString! : _connectionString;
            var topologyPolicy = HaDatabaseTopologySafety.Resolve(_configuration, _externalWitness);
            _databaseTopology = topologyPolicy.DatabaseTopology;
            _databaseFencingMode = topologyPolicy.DatabaseFencingMode;
            if (!_site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) && _allowAutomaticGeographicPromotion && !_externalWitness)
            {
                throw new InvalidOperationException("Automatic geographic promotion requires HighAvailability:WitnessConnectionString so Dispatch and HQ cannot become authoritative independently.");
            }
            _acceptingWrites = false;
            _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
            _state = "Starting";
        }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCoordinatorIterationAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_heartbeatSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await FenceAfterFailureAsync(ex);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_heartbeatSeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task RunCoordinatorIterationAsync(CancellationToken cancellationToken)
    {
        await _leaseGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLeaseConnectionAsync(cancellationToken);
            if (_ownsLease) await VerifyLeaseSessionAsync(cancellationToken);
            var drainedUntil = DrainedUntilUtc;
            if (drainedUntil is DateTimeOffset until && until > DateTimeOffset.UtcNow)
            {
                if (_ownsLease) await ReleaseLeaseCoreAsync("PlannedDrain", cancellationToken);
                _acceptingWrites = false;
                _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
                _state = "Drained";
            }
            else
            {
                if (drainedUntil is not null) Interlocked.Exchange(ref _drainedUntilUtcTicks, 0);
                var eligible = await IsPromotionEligibleAsync(cancellationToken);
                if (_ownsLease && !eligible)
                {
                    // A witness/application lease is not sufficient if this node's local database
                    // has become read-only or entered recovery. Release the lease before exposing
                    // another write window.
                    await ReleaseLeaseCoreAsync("LocalDatabaseNoLongerWritable", cancellationToken);
                    _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
                    _state = "DatabaseNotWritable";
                }
                else if (!_ownsLease && eligible)
                {
                    await TryAcquireLeaseCoreAsync(cancellationToken);
                }
                else if (!_ownsLease)
                {
                    _acceptingWrites = false;
                    _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
                    _state = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "ReadOnlyStandby" : "GeographicStandby";
                }
                else
                {
                    _acceptingWrites = true;
                    _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchPrimary" : "HqDrPrimary";
                    _state = "PrimaryLeaseHeld";
                }
            }

            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            await UpsertHeartbeatAsync(cancellationToken);
            _lastError = null;
        }
        finally
        {
            _leaseGate.Release();
        }
    }

    private async Task<bool> IsPromotionEligibleAsync(CancellationToken cancellationToken)
    {
        if (!_site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase))
        {
            if (!_allowAutomaticGeographicPromotion) return false;
            if (!_externalWitness) return false;
        }

        return await IsLocalDatabaseWritablePrimaryAsync(cancellationToken);
    }

    private async Task<bool> IsLocalDatabaseWritablePrimaryAsync(CancellationToken cancellationToken)
    {
        await using var localConnection = new NpgsqlConnection(_connectionString);
        await localConnection.OpenAsync(cancellationToken);
        return await PostgreSqlDatabaseSafety.IsWritablePrimaryAsync(localConnection, proveWrite: true, cancellationToken);
    }

    private async Task EnsureLeaseConnectionAsync(CancellationToken cancellationToken)
    {
        if (_leaseConnection is { State: ConnectionState.Open }) return;
        if (_leaseConnection is not null) await _leaseConnection.DisposeAsync();
        _leaseConnection = new NpgsqlConnection(_leaseConnectionString);
        await _leaseConnection.OpenAsync(cancellationToken);
        _ownsLease = false;
        _acceptingWrites = false;
    }

    private async Task VerifyLeaseSessionAsync(CancellationToken cancellationToken)
    {
        if (_leaseConnection is null) throw new InvalidOperationException("HA lease connection is unavailable.");
        await using var ping = _leaseConnection.CreateCommand();
        ping.CommandText = "SELECT 1";
        _ = await ping.ExecuteScalarAsync(cancellationToken);
    }

    private async Task TryAcquireLeaseCoreAsync(CancellationToken cancellationToken)
    {
        if (_leaseConnection is null) throw new InvalidOperationException("HA lease connection is unavailable.");
        await using var lease = _leaseConnection.CreateCommand();
        lease.CommandText = "SELECT pg_try_advisory_lock(@key)";
        lease.Parameters.AddWithValue("key", PrimaryAdvisoryLockKey);
        var owns = (bool)(await lease.ExecuteScalarAsync(cancellationToken) ?? false);
        if (!owns)
        {
            _ownsLease = false;
            _acceptingWrites = false;
            _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
            _state = "ReadOnlyStandby";
            return;
        }

        _ownsLease = true;
        _acceptingWrites = false;
        _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
        _state = "Promoting";

        // Re-probe after acquiring the lease to close the eligibility/lease TOCTOU window. A node
        // never advertises itself writable until both the local DB probe and the durable lease-epoch
        // transition have succeeded.
        if (!await IsLocalDatabaseWritablePrimaryAsync(cancellationToken))
        {
            await ReleaseLeaseCoreAsync("DatabaseWriteProbeFailedAfterLease", cancellationToken);
            _state = "DatabaseNotWritable";
            return;
        }

        var epoch = await RecordLeadershipTransitionAsync("LeaseAcquired", null, cancellationToken);
        Interlocked.Exchange(ref _leaseEpoch, epoch);
        Interlocked.Exchange(ref _lastTransitionUtcTicks, DateTimeOffset.UtcNow.UtcTicks);

        // The standby process may have been running for hours with an old in-memory checkpoint.
        // Reload the authoritative PostgreSQL checkpoint after the new fencing epoch is committed
        // and before any client can observe this node as writable.
        var persistence = _services.GetRequiredService<StatePersistenceService>();
        var promotionToken = Status;
        await persistence.ReloadAfterPromotionAsync(cancellationToken);

        // Reload itself may take non-trivial time on a large operational checkpoint. Re-prove both
        // halves of promotion after the reload: the advisory/witness session must still be alive and
        // the local PostgreSQL endpoint must still be a writable, non-recovery primary. Only then may
        // AcceptingWrites transition to true.
        _ = await ValidateAuthoritativeCommitLeaseCoreAsync(allowPromotingLease: true, promotionToken, cancellationToken);
        if (!await IsLocalDatabaseWritablePrimaryAsync(cancellationToken))
        {
            await ReleaseLeaseCoreAsync("DatabaseWriteProbeFailedAfterPromotionReload", cancellationToken);
            _state = "DatabaseNotWritable";
            return;
        }

        _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchPrimary" : "HqDrPrimary";
        _state = "PrimaryLeaseHeld";
        _acceptingWrites = true;
        OperationalLog.HaLeaseAcquired(_logger, _nodeId, epoch);
    }

    private async Task ReleaseLeaseCoreAsync(string reason, CancellationToken cancellationToken)
    {
        if (!_ownsLease || _leaseConnection is null) return;
        try
        {
            await using var unlock = _leaseConnection.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
            unlock.Parameters.AddWithValue("key", PrimaryAdvisoryLockKey);
            _ = await unlock.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            _ownsLease = false;
            _acceptingWrites = false;
        }
        await RecordClusterEventAsync("LeaseReleased", new { reason, epoch = Interlocked.Read(ref _leaseEpoch) }, cancellationToken);
        Interlocked.Exchange(ref _lastTransitionUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        OperationalLog.HaLeaseReleased(_logger, _nodeId, reason);
    }

    private async Task<long> RecordLeadershipTransitionAsync(string eventType, string? reason, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE ha_cluster_state
               SET lease_epoch = lease_epoch + 1,
                   active_node_id = @node,
                   active_instance_id = @instance,
                   active_site = @site,
                   acquired_at_utc = now(),
                   last_transition_utc = now(),
                   transition_reason = @reason
             WHERE singleton = true
             RETURNING lease_epoch;
            """;
        update.Parameters.AddWithValue("node", _nodeId);
        update.Parameters.AddWithValue("instance", _instanceId);
        update.Parameters.AddWithValue("site", _site);
        update.Parameters.AddWithValue("reason", reason ?? eventType);
        var epoch = Convert.ToInt64(await update.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        await using var clusterEvent = connection.CreateCommand();
        clusterEvent.Transaction = transaction;
        clusterEvent.CommandText = "INSERT INTO cluster_events(id, node_id, event_type, details_json, occurred_at_utc) VALUES (@id, @node, @type, @details::jsonb, now())";
        clusterEvent.Parameters.AddWithValue("id", Guid.NewGuid());
        clusterEvent.Parameters.AddWithValue("node", _nodeId);
        clusterEvent.Parameters.AddWithValue("type", eventType);
        clusterEvent.Parameters.AddWithValue("details", JsonSerializer.Serialize(new { site = _site, configuredRole = _configuredRole, reason, epoch }));
        await clusterEvent.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return epoch;
    }

    private async Task RecordClusterEventAsync(string eventType, object details, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString)) return;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO cluster_events(id, node_id, event_type, details_json, occurred_at_utc) VALUES (@id, @node, @type, @details::jsonb, now())";
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("node", _nodeId);
        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("details", JsonSerializer.Serialize(details));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertHeartbeatAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // Physical hot standbys must remain passive. Heartbeats are durable HA metadata and are
        // intentionally resumed only after PostgreSQL promotion makes the local endpoint writable.
        if (!await PostgreSqlDatabaseSafety.IsWritablePrimaryAsync(connection, proveWrite: false, cancellationToken)) return;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO server_nodes(node_id, instance_id, role, state, base_address, started_at_utc, last_heartbeat_utc, accepting_writes, version, site, configured_role, lease_epoch, drained_until_utc, last_error)
            VALUES (@node, @instance, @role, @state, @address, @started, @heartbeat, @writes, @version, @site, @configuredRole, @epoch, @drainedUntil, @error)
            ON CONFLICT(node_id) DO UPDATE SET instance_id=EXCLUDED.instance_id, role=EXCLUDED.role, state=EXCLUDED.state,
                base_address=EXCLUDED.base_address, started_at_utc=EXCLUDED.started_at_utc, last_heartbeat_utc=EXCLUDED.last_heartbeat_utc,
                accepting_writes=EXCLUDED.accepting_writes, version=EXCLUDED.version, site=EXCLUDED.site, configured_role=EXCLUDED.configured_role,
                lease_epoch=EXCLUDED.lease_epoch, drained_until_utc=EXCLUDED.drained_until_utc, last_error=EXCLUDED.last_error;
            """;
        command.Parameters.AddWithValue("node", _nodeId);
        command.Parameters.AddWithValue("instance", _instanceId);
        command.Parameters.AddWithValue("role", _role);
        command.Parameters.AddWithValue("state", _state);
        command.Parameters.AddWithValue("address", _configuration["HighAvailability:AdvertisedAddress"] ?? string.Empty);
        command.Parameters.AddWithValue("started", _startedAtUtc);
        command.Parameters.AddWithValue("heartbeat", _lastHeartbeatUtc);
        command.Parameters.AddWithValue("writes", AcceptingWrites);
        command.Parameters.AddWithValue("version", typeof(HaCoordinatorService).Assembly.GetName().Version?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("site", _site);
        command.Parameters.AddWithValue("configuredRole", _configuredRole);
        command.Parameters.AddWithValue("epoch", Interlocked.Read(ref _leaseEpoch));
        command.Parameters.AddWithValue("drainedUntil", (object?)DrainedUntilUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("error", (object?)_lastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task FenceAfterFailureAsync(Exception exception)
    {
        _acceptingWrites = false;
        _ownsLease = false;
        _role = _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase) ? "DispatchStandby" : "GeographicDR";
        _state = "Fenced";
        _lastError = exception.Message;
        OperationalLog.HaLeaseLost(_logger, exception);
        await _leaseGate.WaitAsync();
        try
        {
            if (_leaseConnection is not null)
            {
                await _leaseConnection.DisposeAsync();
                _leaseConnection = null;
            }
        }
        finally
        {
            _leaseGate.Release();
        }
    }

    public async Task<HaControlResultDto> DrainAsync(HaDrainRequest request, string actor, CancellationToken cancellationToken)
    {
        if (!_enabled) throw new InvalidOperationException("HA is not enabled on this server.");
        var seconds = Math.Clamp(request.Seconds, 15, 86400);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Planned maintenance" : request.Reason.Trim();
        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        Interlocked.Exchange(ref _drainedUntilUtcTicks, until.UtcTicks);
        await _leaseGate.WaitAsync(cancellationToken);
        try
        {
            if (_ownsLease) await ReleaseLeaseCoreAsync("DrainRequested", cancellationToken);
            _state = "Drained";
            _acceptingWrites = false;
            await RecordClusterEventAsync("NodeDrained", new { actor, reason, drainedUntilUtc = until }, cancellationToken);
            _lastHeartbeatUtc = DateTimeOffset.UtcNow;
            await UpsertHeartbeatAsync(cancellationToken);
        }
        finally
        {
            _leaseGate.Release();
        }
        return new HaControlResultDto(_nodeId, _state, AcceptingWrites, until, $"Node drained for planned maintenance: {reason}");
    }

    public async Task<HaControlResultDto> ResumeAsync(HaResumeRequest request, string actor, CancellationToken cancellationToken)
    {
        if (!_enabled) throw new InvalidOperationException("HA is not enabled on this server.");
        Interlocked.Exchange(ref _drainedUntilUtcTicks, 0);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Maintenance complete" : request.Reason.Trim();
        await RecordClusterEventAsync("NodeResumed", new { actor, reason }, cancellationToken);
        _state = "Resuming";
        return new HaControlResultDto(_nodeId, _state, AcceptingWrites, null, $"Node returned to HA election: {reason}");
    }

    public async Task<HaStatusDto> GetClusterStatusAsync(FeatureStore features, CancellationToken cancellationToken)
    {
        var backups = features.HaStatus().Backups;
        if (!_enabled)
        {
            return new HaStatusDto(_site, _nodeId, "Dispatch Primary -> Dispatch Local Standby -> HQ DR", [], backups, false, DateTimeOffset.UtcNow)
            {
                TopologyState = "Standalone",
                CurrentNodeId = _nodeId,
                CurrentNodeRole = _role,
                LeaseEpoch = 0,
                Nodes = [new HaNodeInfoDto(_nodeId, _instanceId, _site, _configuredRole, _role, _state, true, false, _configuration["HighAvailability:AdvertisedAddress"] ?? string.Empty, typeof(HaCoordinatorService).Assembly.GetName().Version?.ToString() ?? string.Empty, 0, _startedAtUtc, _lastHeartbeatUtc, 0, null, _lastError)],
                Detail = "High availability is disabled; this node is operating standalone."
            };
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var nodes = await ReadNodesAsync(connection, cancellationToken);
        var clusterState = await ReadClusterStateAsync(connection, cancellationToken);
        var replicas = await ReadReplicationStatusAsync(connection, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        // Physical promotion replicates the old writer heartbeat to the promoted database. Only a
        // writer heartbeat from the current durable lease epoch can represent current authority;
        // previous-epoch rows are historical state, not split brain.
        var currentEpochWriters = nodes.Where(x => x.AcceptingWrites && !x.Stale && x.LeaseEpoch == clusterState.LeaseEpoch).ToArray();
        var active = currentEpochWriters.FirstOrDefault(x => x.NodeId.Equals(clusterState.ActiveNodeId, StringComparison.OrdinalIgnoreCase));
        var topologyState = active is null ? "NoAuthoritativeWriter" : currentEpochWriters.Length > 1 ? "SplitBrainDetected" : "Healthy";
        var physicalFailover = _databaseTopology.Equals(HaDatabaseTopologySafety.PhysicalFailover, StringComparison.Ordinal);
        var splitBrainProtected = !topologyState.Equals("SplitBrainDetected", StringComparison.OrdinalIgnoreCase) &&
            (physicalFailover ? _externalWitness : (_externalWitness || _site.Equals("Dispatch", StringComparison.OrdinalIgnoreCase)));
        var detail = physicalFailover
            ? "Physical PostgreSQL failover mode: application leadership is fenced by an independent witness; database-primary promotion additionally requires external database fencing/STONITH supplied by the PostgreSQL HA layer."
            : _externalWitness
                ? "Application leadership is fenced by an external witness advisory lock."
                : "Dispatch-local peers are fenced by the shared PostgreSQL advisory lock. Geographic DR requires manual database promotion and external witness configuration before automatic application promotion is safe.";
        return new HaStatusDto(active?.Site ?? "None", active?.NodeId ?? "None", "Dispatch Primary -> Dispatch Local Standby -> HQ DR", replicas, backups, splitBrainProtected, now)
        {
            TopologyState = topologyState,
            CurrentNodeId = _nodeId,
            CurrentNodeRole = _role,
            LeaseEpoch = clusterState.LeaseEpoch,
            LastTransitionUtc = clusterState.LastTransitionUtc,
            AutomaticGeographicPromotionEnabled = _allowAutomaticGeographicPromotion && _externalWitness,
            DatabaseTopology = _databaseTopology,
            DatabaseFencingMode = _databaseFencingMode,
            ExternalWitnessConfigured = _externalWitness,
            Nodes = nodes,
            Detail = detail
        };
    }

    public async Task<IReadOnlyList<HaClusterEventDto>> GetClusterEventsAsync(int limit, CancellationToken cancellationToken)
    {
        if (!_enabled) return [];
        limit = Math.Clamp(limit, 1, 500);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, node_id, event_type, details_json::text, occurred_at_utc FROM cluster_events ORDER BY occurred_at_utc DESC LIMIT @limit";
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<HaClusterEventDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new HaClusterEventDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return events;
    }

    private async Task<IReadOnlyList<HaNodeInfoDto>> ReadNodesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT node_id, instance_id, site, configured_role, role, state, accepting_writes, base_address, version, lease_epoch, started_at_utc, last_heartbeat_utc, drained_until_utc, last_error FROM server_nodes ORDER BY site, node_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<HaNodeInfoDto>();
        var now = DateTimeOffset.UtcNow;
        while (await reader.ReadAsync(cancellationToken))
        {
            var heartbeat = reader.GetFieldValue<DateTimeOffset>(11);
            var age = Math.Max(0, (now - heartbeat).TotalSeconds);
            var stale = age > _staleNodeSeconds;
            var state = stale ? "Stale" : reader.GetString(5);
            result.Add(new HaNodeInfoDto(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                state,
                !stale && reader.GetBoolean(6),
                stale,
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt64(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                heartbeat,
                age,
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        }
        return result;
    }

    private static async Task<(long LeaseEpoch, string ActiveNodeId, DateTimeOffset? LastTransitionUtc)> ReadClusterStateAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT lease_epoch, COALESCE(active_node_id,''), last_transition_utc FROM ha_cluster_state WHERE singleton = true";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (0, string.Empty, null);
        return (reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
    }

    private async Task<IReadOnlyList<ReplicationStatusDto>> ReadReplicationStatusAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var recoveryCommand = connection.CreateCommand();
        recoveryCommand.CommandText = "SELECT pg_is_in_recovery()";
        var inRecovery = (bool)(await recoveryCommand.ExecuteScalarAsync(cancellationToken) ?? false);
        var now = DateTimeOffset.UtcNow;
        var replicas = new List<ReplicationStatusDto>();
        if (inRecovery)
        {
            await using var standby = connection.CreateCommand();
            standby.CommandText = """
                SELECT COALESCE(pg_wal_lsn_diff(pg_last_wal_receive_lsn(), pg_last_wal_replay_lsn()), 0)::bigint,
                       COALESCE(EXTRACT(EPOCH FROM (now() - pg_last_xact_replay_timestamp())), 0)::double precision
                """;
            await using var reader = await standby.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var lagBytes = reader.GetInt64(0);
                var lagSeconds = Math.Max(0, reader.GetDouble(1));
                replicas.Add(new ReplicationStatusDto(_site, "DatabaseStandby", "Streaming", lagBytes, lagSeconds, lagBytes <= _maxPromotionLagBytes && lagSeconds <= _maxPromotionLagSeconds, now));
            }
            return replicas;
        }

        await using var primary = connection.CreateCommand();
        primary.CommandText = """
            SELECT COALESCE(NULLIF(application_name,''), COALESCE(client_addr::text, 'replica')),
                   state,
                   sync_state,
                   COALESCE(pg_wal_lsn_diff(pg_current_wal_lsn(), replay_lsn), 0)::bigint,
                   COALESCE(EXTRACT(EPOCH FROM replay_lag), 0)::double precision
              FROM pg_stat_replication
             ORDER BY application_name, client_addr::text
            """;
        await using var primaryReader = await primary.ExecuteReaderAsync(cancellationToken);
        while (await primaryReader.ReadAsync(cancellationToken))
        {
            var name = primaryReader.GetString(0);
            var state = primaryReader.GetString(1);
            var syncState = primaryReader.GetString(2);
            var lagBytes = primaryReader.GetInt64(3);
            var lagSeconds = Math.Max(0, primaryReader.GetDouble(4));
            var promotable = state.Equals("streaming", StringComparison.OrdinalIgnoreCase) && lagBytes <= _maxPromotionLagBytes && lagSeconds <= _maxPromotionLagSeconds;
            replicas.Add(new ReplicationStatusDto(name, syncState, state, lagBytes, lagSeconds, promotable, now));
        }
        return replicas;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_ownsLease && AcceptingWrites)
        {
            var persistence = _services.GetService<StatePersistenceService>();
            if (persistence is not null) await persistence.FlushAsync(cancellationToken);
        }
        _acceptingWrites = false;
        await _leaseGate.WaitAsync(cancellationToken);
        try
        {
            if (_ownsLease) await ReleaseLeaseCoreAsync("ServerStopping", cancellationToken);
            if (_leaseConnection is not null)
            {
                await _leaseConnection.DisposeAsync();
                _leaseConnection = null;
            }
        }
        finally
        {
            _leaseGate.Release();
        }
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _leaseGate.Dispose();
        base.Dispose();
    }

    private static string NormalizeSite(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "hq" or "headquarters" or "dr" => "HQ",
        _ => "Dispatch"
    };

    private static string NormalizeConfiguredRole(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "primary" or "primarycandidate" => "PrimaryCandidate",
        "geographicdr" or "dr" => "GeographicDR",
        _ => "LocalStandby"
    };
}
