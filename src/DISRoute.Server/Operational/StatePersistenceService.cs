using System.Text.Json;
using Npgsql;

namespace DISRoute.Server.Operational;

public sealed record PersistenceStatusDto(
    string Provider,
    string Location,
    bool LoadedExistingState,
    DateTimeOffset? LastSavedAtUtc,
    string State,
    string? LastError,
    int SupportedSchemaVersion = 0,
    string DatabaseRole = "NotApplicable",
    bool? MigrationIntegrityVerified = null);

internal sealed record FileCheckpointEnvelope(int SchemaVersion, DateTimeOffset SavedAtUtc, string PayloadJson, string PayloadSha256);

/// <summary>
/// Durable runtime checkpointing for the direct-build server. An atomic JSON checkpoint is used
/// for zero-setup development/training installations so restart survival does not depend on native
/// SQLite binaries. Production deployments use PostgreSQL as their authoritative normalized store;
/// the same checkpoint envelope remains a portable recovery/export artifact.
/// </summary>
public sealed class StatePersistenceService : IHostedService, IDisposable
{
    private const int CurrentSchemaVersion = 20;
    private readonly OperationalStore _operational;
    private readonly FeatureStore _features;
    private readonly DurableIdempotencyStore _idempotency;
    private readonly OperationalMutationBuffer _mutationBuffer;
    private readonly AuthoritativeCommandGate _commandGate;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StatePersistenceService> _logger;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Timer? _timer;
    private string _connectionString = string.Empty;
    private string _location = string.Empty;
    private string _provider = "FileCheckpoint";
    private DateTimeOffset? _lastSavedAtUtc;
    private bool _loadedExisting;
    private string? _lastError;
    private string _databaseRole = "NotApplicable";
    private bool? _migrationIntegrityVerified;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public StatePersistenceService(OperationalStore operational, FeatureStore features, DurableIdempotencyStore idempotency, OperationalMutationBuffer mutationBuffer, AuthoritativeCommandGate commandGate, IConfiguration configuration, ILogger<StatePersistenceService> logger, IServiceProvider services)
    {
        _operational = operational;
        _features = features;
        _idempotency = idempotency;
        _mutationBuffer = mutationBuffer;
        _commandGate = commandGate;
        _configuration = configuration;
        _logger = logger;
        _services = services;
    }

    public PersistenceStatusDto Status => new(
        _provider,
        _location,
        _loadedExisting,
        _lastSavedAtUtc,
        _lastError is null ? "Healthy" : "Degraded",
        _lastError,
        CurrentSchemaVersion,
        _databaseRole,
        _migrationIntegrityVerified);
    public string Provider => _provider;
    public string Location => _location;
    public int SupportedSchemaVersion => CurrentSchemaVersion;
    public string? PendingRestorePath => _provider.Equals("FileCheckpoint", StringComparison.OrdinalIgnoreCase) ? _location + ".restore-pending" : null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _provider = _configuration["Persistence:Provider"]?.Trim() ?? "FileCheckpoint";
        if (_provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            _provider = "PostgreSQL";
            _connectionString = SecretReferenceResolver.ResolveConnectionString(_configuration);
            var csb = new NpgsqlConnectionStringBuilder(_connectionString);
            _location = $"{csb.Host}:{csb.Port}/{csb.Database}";
        }
        else
        {
            // Preserve compatibility with older configuration values while removing the native
            // SQLite dependency from the zero-setup development path.
            _provider = "FileCheckpoint";
            var configured = _configuration["Persistence:CheckpointPath"];
            var path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "data", "disroute-state.json")
                : Path.GetFullPath(configured);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
            _location = path;
            _connectionString = string.Empty;
        }

        await EnsureSchemaAsync(cancellationToken);
        await ApplyPendingFileRestoreAsync(cancellationToken);
        await LoadAsync(cancellationToken);

        var seconds = Math.Clamp(_configuration.GetValue("Persistence:CheckpointSeconds", 2), 1, 300);
        _timer = new Timer(_ => _ = SaveSafeAsync(), null, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(seconds));
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        await SaveAsync(commandGateHeld: false, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default) => await SaveAsync(commandGateHeld: false, cancellationToken);
    internal async Task FlushWithinCommandGateAsync(CancellationToken cancellationToken = default) => await SaveAsync(commandGateHeld: true, cancellationToken);
    internal DISRoutePersistedState CaptureInMemoryState() => Capture();
    internal void RestoreInMemoryState(DISRoutePersistedState state)
    {
        _operational.ImportState(state.Operational);
        _features.ImportState(state.Features);
        _idempotency.ImportState(state.IdempotencyReceipts);
    }

    internal async Task ReloadAfterPromotionAsync(CancellationToken cancellationToken)
    {
        if (!_provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HA promotion reload is only valid for PostgreSQL persistence.");

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var loaded = await ReadPostgreSqlCheckpointAsync(cancellationToken);
            if (loaded is not null)
            {
                RestoreInMemoryState(loaded.Value.State);
                _loadedExisting = true;
                _lastSavedAtUtc = loaded.Value.SavedAtUtc;
                _lastError = null;
                OperationalLog.StateLoaded(_logger, _location, loaded.Value.SavedAtUtc);
                return;
            }

            // First bootstrap of an empty PostgreSQL database: establish the initial seed state under
            // the newly committed fencing epoch before advertising this node as writable.
            var state = Capture();
            await PersistPostgreSqlStateAsync(state, allowPromotingLease: true, cancellationToken);
            _lastSavedAtUtc = state.SavedAtUtc;
            _lastError = null;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<string> ExportJsonAsync(CancellationToken cancellationToken = default)
    {
        var state = Capture();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    private DISRoutePersistedState Capture() => new(CurrentSchemaVersion, DateTimeOffset.UtcNow, _operational.ExportState(), _features.ExportState(), _idempotency.ExportState());

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_provider == "PostgreSQL")
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await EnsurePostgreSqlSchemaAsync(connection, cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(_location) ?? AppContext.BaseDirectory);
    }


    private async Task EnsurePostgreSqlSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var migrationDirectory = Path.Combine(AppContext.BaseDirectory, "db", "migrations");
        var migrations = PostgreSqlDatabaseSafety.LoadPublishedMigrations(migrationDirectory);
        var writable = await PostgreSqlDatabaseSafety.IsWritablePrimaryAsync(connection, proveWrite: false, cancellationToken);
        if (writable)
        {
            // Multiple application nodes can start together against the same PostgreSQL primary.
            // Serialize schema migration/ledger establishment with a dedicated session advisory lock.
            await using var migrationLock = connection.CreateCommand();
            migrationLock.CommandText = "SELECT pg_advisory_lock(@key)";
            migrationLock.Parameters.AddWithValue("key", PostgreSqlDatabaseSafety.MigrationAdvisoryLockKey);
            _ = await migrationLock.ExecuteScalarAsync(cancellationToken);
            try
            {
                await PostgreSqlDatabaseSafety.EnsureMigrationLedgerAndApplyAsync(connection, migrations, cancellationToken);
                _databaseRole = "WritablePrimary";
                _migrationIntegrityVerified = true;
            }
            finally
            {
                await using var unlock = connection.CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
                unlock.Parameters.AddWithValue("key", PostgreSqlDatabaseSafety.MigrationAdvisoryLockKey);
                _ = await unlock.ExecuteScalarAsync(CancellationToken.None);
            }
            return;
        }

        var haEnabled = _configuration.GetValue("HighAvailability:Enabled", false);
        if (!haEnabled)
            throw new InvalidOperationException("PostgreSQL persistence is connected to a read-only/recovery database while high availability is disabled.");

        // Geographic DR may run against a physical read replica before promotion. It must be fully
        // migrated and integrity-verified, but startup must not attempt DDL on the replica.
        await PostgreSqlDatabaseSafety.VerifyMigrationLedgerAsync(connection, migrations, cancellationToken);
        _databaseRole = "ReadOnlyReplica";
        _migrationIntegrityVerified = true;
    }

    private async Task ApplyPendingFileRestoreAsync(CancellationToken cancellationToken)
    {
        if (!_provider.Equals("FileCheckpoint", StringComparison.OrdinalIgnoreCase)) return;
        var pendingPath = PendingRestorePath;
        if (string.IsNullOrWhiteSpace(pendingPath) || !File.Exists(pendingPath)) return;

        var envelopeJson = await File.ReadAllTextAsync(pendingPath, cancellationToken);
        var envelope = JsonSerializer.Deserialize<FileCheckpointEnvelope>(envelopeJson, JsonOptions)
            ?? throw new InvalidDataException("Pending DISRoute restore envelope was empty or invalid.");
        if (envelope.SchemaVersion > CurrentSchemaVersion)
            throw new InvalidOperationException($"Pending restore schema {envelope.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
        var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(envelope.PayloadJson))).ToLowerInvariant();
        if (!string.Equals(envelope.PayloadSha256, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pending DISRoute restore failed SHA-256 integrity validation.");
        _ = JsonSerializer.Deserialize<DISRoutePersistedState>(envelope.PayloadJson, JsonOptions)
            ?? throw new InvalidDataException("Pending DISRoute restore state was empty.");

        string? rollbackPath = null;
        if (File.Exists(_location))
        {
            rollbackPath = _location + $".pre-restore-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}.bak";
            File.Copy(_location, rollbackPath, overwrite: false);
        }
        File.Move(pendingPath, _location, overwrite: true);
        OperationalLog.PendingRestoreApplied(_logger, _location, rollbackPath ?? "none");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            int schema;
            string json;
            string expectedHash;
            DateTimeOffset savedAt;

            if (_provider == "PostgreSQL")
            {
                var loaded = await ReadPostgreSqlCheckpointAsync(cancellationToken);
                if (loaded is null) return;
                RestoreInMemoryState(loaded.Value.State);
                _loadedExisting = true;
                _lastSavedAtUtc = loaded.Value.SavedAtUtc;
                _lastError = null;
                OperationalLog.StateLoaded(_logger, _location, loaded.Value.SavedAtUtc);
                return;
            }
            else
            {
                if (!File.Exists(_location)) return;
                var envelopeJson = await File.ReadAllTextAsync(_location, cancellationToken);
                var envelope = JsonSerializer.Deserialize<FileCheckpointEnvelope>(envelopeJson, JsonOptions)
                    ?? throw new InvalidDataException("DISRoute checkpoint envelope was empty or invalid.");
                schema = envelope.SchemaVersion;
                savedAt = envelope.SavedAtUtc;
                json = envelope.PayloadJson;
                expectedHash = envelope.PayloadSha256;
            }

            if (schema > CurrentSchemaVersion) throw new InvalidOperationException($"Runtime state schema {schema} is newer than supported schema {CurrentSchemaVersion}.");
            var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Persisted DISRoute state failed SHA-256 integrity validation.");

            var state = JsonSerializer.Deserialize<DISRoutePersistedState>(json, JsonOptions) ?? throw new InvalidDataException("Persisted DISRoute state was empty.");
            _operational.ImportState(state.Operational);
            _features.ImportState(state.Features);
            _idempotency.ImportState(state.IdempotencyReceipts);
            _loadedExisting = true;
            _lastSavedAtUtc = savedAt;
            _lastError = null;
            OperationalLog.StateLoaded(_logger, _location, savedAt);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            OperationalLog.StateLoadFailed(_logger, ex);
            if (_provider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)) throw;
        }
    }

    private async Task<(DISRoutePersistedState State, DateTimeOffset SavedAtUtc)?> ReadPostgreSqlCheckpointAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version, saved_at_utc, payload_json::text, payload_sha256 FROM runtime_state_checkpoint WHERE id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var schema = reader.GetInt32(0);
        var savedAt = reader.GetFieldValue<DateTimeOffset>(1);
        var json = reader.GetString(2);
        var expectedHash = reader.GetString(3);
        if (schema > CurrentSchemaVersion)
            throw new InvalidOperationException($"Runtime state schema {schema} is newer than supported schema {CurrentSchemaVersion}.");
        var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Persisted DISRoute state failed SHA-256 integrity validation.");
        var state = JsonSerializer.Deserialize<DISRoutePersistedState>(json, JsonOptions)
            ?? throw new InvalidDataException("Persisted DISRoute state was empty.");
        return (state, savedAt);
    }

    private async Task SaveSafeAsync()
    {
        try { await SaveAsync(commandGateHeld: false, CancellationToken.None); }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            OperationalLog.StateCheckpointFailed(_logger, ex);
        }
    }

    private async Task SaveAsync(bool commandGateHeld, CancellationToken cancellationToken)
    {
        IDisposable? commandLease = null;
        if (_provider == "PostgreSQL" && !commandGateHeld)
            commandLease = await _commandGate.EnterAsync(cancellationToken);
        try
        {
        if (_provider == "PostgreSQL" && _configuration.GetValue("HighAvailability:Enabled", false))
        {
            var ha = _services.GetService<HaCoordinatorService>();
            if (ha is null || !ha.AcceptingWrites)
            {
                if (commandGateHeld)
                    throw new InvalidOperationException("Authoritative PostgreSQL flush refused because HA leadership was lost before commit.");
                return;
            }
        }
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var state = Capture();
            var json = JsonSerializer.Serialize(state, JsonOptions);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

            if (_provider == "PostgreSQL")
            {
                await PersistPostgreSqlStateAsync(state, allowPromotingLease: false, cancellationToken);
            }
            else
            {
                var envelope = new FileCheckpointEnvelope(state.SchemaVersion, state.SavedAtUtc, json, hash);
                var envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
                var tempPath = _location + ".tmp";
                await File.WriteAllTextAsync(tempPath, envelopeJson, cancellationToken);
                File.Move(tempPath, _location, true);
            }

            _lastSavedAtUtc = state.SavedAtUtc;
            _lastError = null;
        }
        finally { _writeGate.Release(); }
        }
        finally
        {
            commandLease?.Dispose();
        }
    }



    private async Task PersistPostgreSqlStateAsync(DISRoutePersistedState state, bool allowPromotingLease, CancellationToken cancellationToken)
    {
        var haEnabled = _configuration.GetValue("HighAvailability:Enabled", false);
        var ha = haEnabled ? _services.GetRequiredService<HaCoordinatorService>() : null;
        HaCoordinatorService.HaAuthoritativeCommitLease? commitLease = null;
        HaNodeStatusDto? fencingToken = null;

        if (ha is not null)
        {
            if (allowPromotingLease)
            {
                // TryAcquireLeaseCoreAsync owns the HA lease gate while promotion reload runs.
                // Validate the live lease session without recursively entering the same semaphore.
                fencingToken = await ha.ValidatePromotingCommitWhileLeaseGateHeldAsync(expectedToken: null, cancellationToken);
            }
            else
            {
                // Hold the HA lease gate through the database transaction. Planned drain/release
                // must wait for this scope, and a commit that observes leadership loss is rejected.
                commitLease = await ha.EnterAuthoritativeCommitAsync(cancellationToken);
                fencingToken = commitLease.Token;
            }
        }

        using (commitLease)
        {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (fencingToken is not null) await AssertHaFencingTokenAsync(connection, transaction, fencingToken, cancellationToken);

        // PostgreSQL jsonb has its own canonical text representation (notably whitespace and
        // object-key formatting). Hash the exact representation PostgreSQL will persist so a
        // restart/promotion can verify payload_json::text byte-for-byte instead of comparing a
        // pre-insert System.Text.Json string against PostgreSQL's canonicalized jsonb text.
        string canonicalJson;
        await using (var canonicalize = connection.CreateCommand())
        {
            canonicalize.Transaction = transaction;
            canonicalize.CommandText = "SELECT CAST(@payload AS jsonb)::text";
            canonicalize.Parameters.AddWithValue("payload", json);
            canonicalJson = (string?)await canonicalize.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidDataException("PostgreSQL returned an empty canonical runtime checkpoint payload.");
        }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO runtime_state_checkpoint (id, schema_version, saved_at_utc, payload_json, payload_sha256)
                VALUES (1, @schema, @saved, CAST(@payload AS jsonb), @hash)
                ON CONFLICT(id) DO UPDATE SET
                    schema_version = EXCLUDED.schema_version,
                    saved_at_utc = EXCLUDED.saved_at_utc,
                    payload_json = EXCLUDED.payload_json,
                    payload_sha256 = EXCLUDED.payload_sha256;
                """;
            command.Parameters.AddWithValue("schema", state.SchemaVersion);
            command.Parameters.AddWithValue("saved", state.SavedAtUtc);
            command.Parameters.AddWithValue("payload", canonicalJson);
            command.Parameters.AddWithValue("hash", hash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await SynchronizeCoreProjectionAsync(connection, transaction, state, cancellationToken);
        var authoritativeBatches = _mutationBuffer.PendingAuthoritativeBatchIds;
        await MarkAuthoritativeMutationBarrierAsync(connection, transaction, state.SavedAtUtc, authoritativeBatches, cancellationToken);

        // Re-prove the HA lease immediately before the authoritative transaction commits. For
        // normal traffic the scoped commit lease still owns the HA semaphore here, so a planned
        // release/election cannot race this boundary. Promotion reload already owns that semaphore.
        if (ha is not null)
        {
            if (commitLease is not null)
                await commitLease.VerifyBeforeCommitAsync(cancellationToken);
            else
                _ = await ha.ValidatePromotingCommitWhileLeaseGateHeldAsync(fencingToken, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _mutationBuffer.MarkAuthoritativeBatchesCommitted(authoritativeBatches);
        }
    }

    private static async Task AssertHaFencingTokenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HaNodeStatusDto token,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT active_node_id, active_instance_id, lease_epoch
              FROM ha_cluster_state
             WHERE singleton = true
             FOR UPDATE;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("HA fencing state is missing from PostgreSQL.");

        var activeNode = reader.GetString(0);
        var activeInstance = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var leaseEpoch = reader.GetInt64(2);
        if (!activeNode.Equals(token.NodeId, StringComparison.OrdinalIgnoreCase) ||
            activeInstance != token.InstanceId ||
            leaseEpoch != token.LeaseEpoch)
        {
            throw new InvalidOperationException(
                $"HA fencing token rejected authoritative commit. Local={token.NodeId}/{token.InstanceId:D}/epoch-{token.LeaseEpoch}; " +
                $"database={activeNode}/{activeInstance?.ToString("D") ?? "none"}/epoch-{leaseEpoch}.");
        }
    }

    private static async Task MarkAuthoritativeMutationBarrierAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset savedAtUtc,
        IReadOnlyList<Guid> batchIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE command_journal
            SET authoritative_committed_at_utc = @committed
            WHERE authoritative_committed_at_utc IS NULL
              AND occurred_at_utc <= @committed
              AND (durability_batch_id IS NULL OR durability_batch_id = ANY(@batches));

            UPDATE outbox_events
            SET authoritative_committed_at_utc = @committed
            WHERE authoritative_committed_at_utc IS NULL
              AND occurred_at_utc <= @committed
              AND (durability_batch_id IS NULL OR durability_batch_id = ANY(@batches));
            """;
        command.Parameters.AddWithValue("committed", savedAtUtc);
        command.Parameters.AddWithValue("batches", batchIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SynchronizeCoreProjectionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DISRoutePersistedState state, CancellationToken cancellationToken)
    {
        async Task ExecAsync(string sql, Action<NpgsqlCommand> bind)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            bind(command);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var agency = _operational.Agency;
        await ExecAsync("""
            INSERT INTO agencies(id, code, name) VALUES(@id,@code,@name)
            ON CONFLICT(id) DO UPDATE SET code=EXCLUDED.code, name=EXCLUDED.name;
            """, c => { c.Parameters.AddWithValue("id", agency.Id); c.Parameters.AddWithValue("code", agency.Code); c.Parameters.AddWithValue("name", agency.Name); });

        foreach (var item in state.Operational.Stations)
            await ExecAsync("""
                INSERT INTO stations(id,agency_id,code,name,address) VALUES(@id,@agency,@code,@name,@address)
                ON CONFLICT(id) DO UPDATE SET agency_id=EXCLUDED.agency_id,code=EXCLUDED.code,name=EXCLUDED.name,address=EXCLUDED.address;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("code",item.Code); c.Parameters.AddWithValue("name",item.Name); c.Parameters.AddWithValue("address",item.Address); });

        foreach (var item in state.Operational.Incidents)
            await ExecAsync("""
                INSERT INTO incidents(id,agency_id,station_id,display_number,nature,priority,address,cross_streets,municipality,caller_name,caller_phone,notes,lifecycle_state,version,received_at_utc,updated_at_utc,closed_at_utc)
                VALUES(@id,@agency,@station,@number,@nature,@priority,@address,@cross,@municipality,@caller,@phone,@notes,@state,@version,@received,@updated,@closed)
                ON CONFLICT(id) DO UPDATE SET station_id=EXCLUDED.station_id,display_number=EXCLUDED.display_number,nature=EXCLUDED.nature,priority=EXCLUDED.priority,address=EXCLUDED.address,cross_streets=EXCLUDED.cross_streets,municipality=EXCLUDED.municipality,caller_name=EXCLUDED.caller_name,caller_phone=EXCLUDED.caller_phone,notes=EXCLUDED.notes,lifecycle_state=EXCLUDED.lifecycle_state,version=EXCLUDED.version,updated_at_utc=EXCLUDED.updated_at_utc,closed_at_utc=EXCLUDED.closed_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("station",item.StationId); c.Parameters.AddWithValue("number",item.Number); c.Parameters.AddWithValue("nature",item.Nature); c.Parameters.AddWithValue("priority",item.Priority); c.Parameters.AddWithValue("address",item.Address); c.Parameters.AddWithValue("cross",item.CrossStreets); c.Parameters.AddWithValue("municipality",item.Municipality); c.Parameters.AddWithValue("caller",item.CallerName); c.Parameters.AddWithValue("phone",item.CallerPhone); c.Parameters.AddWithValue("notes",item.Notes); c.Parameters.AddWithValue("state",item.Status); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("received",item.ReceivedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); c.Parameters.AddWithValue("closed",(object?)item.ClosedAtUtc ?? DBNull.Value); });

        foreach (var item in state.Operational.Units)
            await ExecAsync("""
                INSERT INTO operational_units(id,agency_id,station_id,call_sign,discipline,unit_type,status,incident_id,updated_at_utc)
                VALUES(@id,@agency,@station,@call,@discipline,@type,@status,@incident,@updated)
                ON CONFLICT(id) DO UPDATE SET station_id=EXCLUDED.station_id,call_sign=EXCLUDED.call_sign,discipline=EXCLUDED.discipline,unit_type=EXCLUDED.unit_type,status=EXCLUDED.status,incident_id=EXCLUDED.incident_id,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("station",item.StationId); c.Parameters.AddWithValue("call",item.CallSign); c.Parameters.AddWithValue("discipline",item.Discipline); c.Parameters.AddWithValue("type",item.UnitType); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Operational.IncidentHistory ?? [])
            await ExecAsync("""
                INSERT INTO incident_state_history(id,agency_id,incident_id,version,cause,actor,state_json,recorded_at_utc)
                VALUES(@id,@agency,@incident,@version,@cause,@actor,CAST(@state AS jsonb),@recorded)
                ON CONFLICT DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.State.AgencyId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("cause",item.Cause); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item.State, JsonOptions)); c.Parameters.AddWithValue("recorded",item.RecordedAtUtc); });

        foreach (var item in state.Operational.UnitHistory ?? [])
            await ExecAsync("""
                INSERT INTO unit_state_history(id,agency_id,unit_id,incident_id,cause,actor,state_json,recorded_at_utc)
                VALUES(@id,@agency,@unit,@incident,@cause,@actor,CAST(@state AS jsonb),@recorded)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.State.AgencyId); c.Parameters.AddWithValue("unit",item.UnitId); c.Parameters.AddWithValue("incident",(object?)item.State.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("cause",item.Cause); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item.State, JsonOptions)); c.Parameters.AddWithValue("recorded",item.RecordedAtUtc); });

        foreach (var item in state.Features.DispatchActionHistory ?? [])
            await ExecAsync("""
                INSERT INTO dispatch_action_state_history(id,agency_id,dispatch_action_id,incident_id,cause,actor,state_json,recorded_at_utc)
                VALUES(@id,@agency,@action,@incident,@cause,@actor,CAST(@state AS jsonb),@recorded)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.State.AgencyId); c.Parameters.AddWithValue("action",item.DispatchActionId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("cause",item.Cause); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item.State, JsonOptions)); c.Parameters.AddWithValue("recorded",item.RecordedAtUtc); });

        var dispatchAgencies = state.Features.DispatchActions.ToDictionary(x => x.Id, x => x.AgencyId);
        foreach (var item in state.Features.DispatchDeliveryHistory ?? [])
            await ExecAsync("""
                INSERT INTO dispatch_delivery_state_history(id,agency_id,delivery_id,incident_id,cause,actor,state_json,recorded_at_utc)
                VALUES(@id,@agency,@delivery,@incident,@cause,@actor,CAST(@state AS jsonb),@recorded)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",dispatchAgencies.GetValueOrDefault(item.State.DispatchActionId, agency.Id)); c.Parameters.AddWithValue("delivery",item.DeliveryId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("cause",item.Cause); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item.State, JsonOptions)); c.Parameters.AddWithValue("recorded",item.RecordedAtUtc); });

        foreach (var item in state.Features.StationDeliveryHistory ?? [])
            await ExecAsync("""
                INSERT INTO station_delivery_state_history(id,agency_id,delivery_id,incident_id,cause,actor,state_json,recorded_at_utc)
                VALUES(@id,@agency,@delivery,@incident,@cause,@actor,CAST(@state AS jsonb),@recorded)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.State.AgencyId); c.Parameters.AddWithValue("delivery",item.DeliveryId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("cause",item.Cause); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item.State, JsonOptions)); c.Parameters.AddWithValue("recorded",item.RecordedAtUtc); });

        foreach (var item in state.Features.AlertHistory ?? [])
            await ExecAsync("""
                INSERT INTO alert_state_history(id,agency_id,alert_id,incident_id,cause,actor,state_json,recorded_at_utc)
                VALUES(@id,@agency,@alert,@incident,@cause,@actor,CAST(@state AS jsonb),@recorded)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.State.AgencyId); c.Parameters.AddWithValue("alert",item.AlertId); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("cause",item.Cause); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item.State, JsonOptions)); c.Parameters.AddWithValue("recorded",item.RecordedAtUtc); });

        foreach (var item in state.Features.VesselObservationHistory ?? [])
            await ExecAsync("""
                INSERT INTO vessel_observation_history(id,agency_id,mmsi,observed_at_utc,state_json)
                VALUES(@id,@agency,@mmsi,@observed,CAST(@state AS jsonb))
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("mmsi",item.Mmsi); c.Parameters.AddWithValue("observed",item.ObservedAtUtc); c.Parameters.AddWithValue("state",JsonSerializer.Serialize(item, JsonOptions)); });

        foreach (var list in state.Operational.Activities.Values)
            foreach (var item in list)
                await ExecAsync("""
                    INSERT INTO incident_activity(id,incident_id,category,message,actor,occurred_at_utc) VALUES(@id,@incident,@category,@message,@actor,@occurred)
                    ON CONFLICT(id) DO NOTHING;
                    """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("category",item.Category); c.Parameters.AddWithValue("message",item.Message); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("occurred",item.OccurredAtUtc); });

        foreach (var item in state.Operational.Messages)
            await ExecAsync("""
                INSERT INTO messages(id,agency_id,incident_id,channel,sender,body,sent_at_utc) VALUES(@id,@agency,@incident,@channel,@sender,@body,@sent)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("channel",item.Channel); c.Parameters.AddWithValue("sender",item.Sender); c.Parameters.AddWithValue("body",item.Text); c.Parameters.AddWithValue("sent",item.SentAtUtc); });

        foreach (var item in state.Operational.Workforce)
            await ExecAsync("""
                INSERT INTO workforce_shifts(id,agency_id,work_date,shift_name,station_id,position,employee_name,qualification,status)
                VALUES(@id,@agency,@date,@shift,@station,@position,@employee,@qualification,@status)
                ON CONFLICT(id) DO UPDATE SET work_date=EXCLUDED.work_date,shift_name=EXCLUDED.shift_name,station_id=EXCLUDED.station_id,position=EXCLUDED.position,employee_name=EXCLUDED.employee_name,qualification=EXCLUDED.qualification,status=EXCLUDED.status;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("date",item.Date); c.Parameters.AddWithValue("shift",item.ShiftName); c.Parameters.AddWithValue("station",item.StationId); c.Parameters.AddWithValue("position",item.Position); c.Parameters.AddWithValue("employee",item.EmployeeName); c.Parameters.AddWithValue("qualification",item.Qualification); c.Parameters.AddWithValue("status",item.Status); });

        foreach (var item in state.Features.StaffingRules ?? [])
            await ExecAsync("""
                INSERT INTO workforce_staffing_rules(id,agency_id,station_id,shift_name,position,minimum_count,required_qualification,enabled,updated_by,updated_at_utc)
                VALUES(@id,@agency,@station,@shift,@position,@minimum,@qualification,@enabled,@actor,@updated)
                ON CONFLICT(id) DO UPDATE SET station_id=EXCLUDED.station_id,shift_name=EXCLUDED.shift_name,position=EXCLUDED.position,minimum_count=EXCLUDED.minimum_count,required_qualification=EXCLUDED.required_qualification,enabled=EXCLUDED.enabled,updated_by=EXCLUDED.updated_by,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("station",(object?)item.StationId ?? DBNull.Value); c.Parameters.AddWithValue("shift",item.ShiftName); c.Parameters.AddWithValue("position",item.Position); c.Parameters.AddWithValue("minimum",item.MinimumCount); c.Parameters.AddWithValue("qualification",item.RequiredQualification); c.Parameters.AddWithValue("enabled",item.Enabled); c.Parameters.AddWithValue("actor",item.UpdatedBy); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Features.WorkforceAssignments ?? [])
            await ExecAsync("""
                INSERT INTO workforce_assignments(id,agency_id,work_date,shift_name,station_id,unit_id,position,employee_id,employee_name,qualification,assignment_type,state,effective_at_utc,ends_at_utc,source,updated_by,updated_at_utc)
                VALUES(@id,@agency,@date,@shift,@station,@unit,@position,@employeeId,@employee,@qualification,@type,@state,@effective,@ends,@source,@actor,@updated)
                ON CONFLICT(id) DO UPDATE SET work_date=EXCLUDED.work_date,shift_name=EXCLUDED.shift_name,station_id=EXCLUDED.station_id,unit_id=EXCLUDED.unit_id,position=EXCLUDED.position,employee_id=EXCLUDED.employee_id,employee_name=EXCLUDED.employee_name,qualification=EXCLUDED.qualification,assignment_type=EXCLUDED.assignment_type,state=EXCLUDED.state,effective_at_utc=EXCLUDED.effective_at_utc,ends_at_utc=EXCLUDED.ends_at_utc,source=EXCLUDED.source,updated_by=EXCLUDED.updated_by,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("date",item.Date); c.Parameters.AddWithValue("shift",item.ShiftName); c.Parameters.AddWithValue("station",item.StationId); c.Parameters.AddWithValue("unit",(object?)item.UnitId ?? DBNull.Value); c.Parameters.AddWithValue("position",item.Position); c.Parameters.AddWithValue("employeeId",(object?)item.EmployeeId ?? DBNull.Value); c.Parameters.AddWithValue("employee",item.EmployeeName); c.Parameters.AddWithValue("qualification",item.Qualification); c.Parameters.AddWithValue("type",item.AssignmentType); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("effective",item.EffectiveAtUtc); c.Parameters.AddWithValue("ends",(object?)item.EndsAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("source",item.Source); c.Parameters.AddWithValue("actor",item.UpdatedBy); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Features.WorkforceScheduleChanges ?? [])
            await ExecAsync("""
                INSERT INTO workforce_schedule_changes(id,agency_id,work_date,assignment_id,kind,summary,actor,occurred_at_utc)
                VALUES(@id,@agency,@date,@assignment,@kind,@summary,@actor,@occurred)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("date",item.Date); c.Parameters.AddWithValue("assignment",(object?)item.AssignmentId ?? DBNull.Value); c.Parameters.AddWithValue("kind",item.Kind); c.Parameters.AddWithValue("summary",item.Summary); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("occurred",item.OccurredAtUtc); });

        foreach (var item in state.Features.WorkforceRosterSnapshots ?? [])
            await ExecAsync("""
                INSERT INTO workforce_roster_snapshots(id,agency_id,work_date,revision,state,posted_at_utc,posted_by,notes,content_sha256,snapshot_json)
                VALUES(@id,@agency,@date,@revision,@state,@posted,@actor,@notes,@sha,CAST(@snapshot AS jsonb))
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("date",item.Date); c.Parameters.AddWithValue("revision",item.Revision); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("posted",item.PostedAtUtc); c.Parameters.AddWithValue("actor",item.PostedBy); c.Parameters.AddWithValue("notes",item.Notes); c.Parameters.AddWithValue("sha",item.ContentSha256); c.Parameters.AddWithValue("snapshot",JsonSerializer.Serialize(item, JsonOptions)); });

        foreach (var item in state.Operational.Documents)
        {
            await ExecAsync("""
                INSERT INTO documents(id,agency_id,title,classification,current_version,status,created_by,created_at_utc,updated_at_utc)
                VALUES(@id,@agency,@title,@classification,@version,@status,@createdBy,@created,@updated)
                ON CONFLICT(id) DO UPDATE SET title=EXCLUDED.title,classification=EXCLUDED.classification,current_version=EXCLUDED.current_version,status=EXCLUDED.status,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("title",item.Title); c.Parameters.AddWithValue("classification",item.Classification); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("createdBy",item.CreatedBy); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });
            await ExecAsync("""
                INSERT INTO document_versions(id,document_id,version,content,status,author,created_at_utc)
                VALUES(@id,@document,@version,@content,@status,@author,@created)
                ON CONFLICT(document_id,version) DO UPDATE SET content=EXCLUDED.content,status=EXCLUDED.status,author=EXCLUDED.author;
                """, c => { c.Parameters.AddWithValue("id",DeterministicVersionId(item.Id,item.Version)); c.Parameters.AddWithValue("document",item.Id); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("content",item.Content); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("author",item.CreatedBy); c.Parameters.AddWithValue("created",item.UpdatedAtUtc); });
        }

        foreach (var item in state.Features.DmsDocumentRevisions ?? [])
            await ExecAsync("""
                INSERT INTO dms_document_revisions(id,document_id,version,content,state,author,content_sha256,created_at_utc,published_by,published_at_utc)
                VALUES(@id,@document,@version,@content,@state,@author,@sha,@created,@publishedBy,@publishedAt)
                ON CONFLICT(document_id,version) DO UPDATE SET state=EXCLUDED.state,published_by=EXCLUDED.published_by,published_at_utc=EXCLUDED.published_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("document",item.DocumentId); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("content",item.Content); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("author",item.Author); c.Parameters.AddWithValue("sha",item.ContentSha256); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("publishedBy",(object?)item.PublishedBy ?? DBNull.Value); c.Parameters.AddWithValue("publishedAt",(object?)item.PublishedAtUtc ?? DBNull.Value); });

        foreach (var item in state.Features.DmsDistributions ?? [])
            await ExecAsync("""
                INSERT INTO dms_distributions(id,agency_id,document_id,document_version,scope_type,scope_value,required_read,due_at_utc,state,assigned_by,assigned_at_utc)
                VALUES(@id,@agency,@document,@version,@scopeType,@scopeValue,@required,@due,@state,@actor,@assigned)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,due_at_utc=EXCLUDED.due_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("document",item.DocumentId); c.Parameters.AddWithValue("version",item.DocumentVersion); c.Parameters.AddWithValue("scopeType",item.ScopeType); c.Parameters.AddWithValue("scopeValue",item.ScopeValue); c.Parameters.AddWithValue("required",item.RequiredRead); c.Parameters.AddWithValue("due",(object?)item.DueAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("actor",item.AssignedBy); c.Parameters.AddWithValue("assigned",item.AssignedAtUtc); });

        foreach (var item in state.Features.DmsReadReceipts ?? [])
            await ExecAsync("""
                INSERT INTO dms_read_receipts(id,distribution_id,document_id,document_version,recipient,state,assigned_at_utc,read_at_utc,acknowledged_at_utc,acknowledged_by)
                VALUES(@id,@distribution,@document,@version,@recipient,@state,@assigned,@read,@ack,@ackBy)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,read_at_utc=EXCLUDED.read_at_utc,acknowledged_at_utc=EXCLUDED.acknowledged_at_utc,acknowledged_by=EXCLUDED.acknowledged_by;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("distribution",item.DistributionId); c.Parameters.AddWithValue("document",item.DocumentId); c.Parameters.AddWithValue("version",item.DocumentVersion); c.Parameters.AddWithValue("recipient",item.Recipient); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("assigned",item.AssignedAtUtc); c.Parameters.AddWithValue("read",(object?)item.ReadAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("ack",(object?)item.AcknowledgedAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("ackBy",(object?)item.AcknowledgedBy ?? DBNull.Value); });

        foreach (var item in state.Features.DmsAssetReferences ?? [])
            await ExecAsync("""
                INSERT INTO dms_asset_references(id,agency_id,document_id,document_version,file_name,content_type,storage_reference,content_sha256,classification,added_by,added_at_utc)
                VALUES(@id,@agency,@document,@version,@file,@contentType,@storage,@sha,@classification,@actor,@added)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("document",item.DocumentId); c.Parameters.AddWithValue("version",item.DocumentVersion); c.Parameters.AddWithValue("file",item.FileName); c.Parameters.AddWithValue("contentType",item.ContentType); c.Parameters.AddWithValue("storage",item.StorageReference); c.Parameters.AddWithValue("sha",(object?)item.ContentSha256 ?? DBNull.Value); c.Parameters.AddWithValue("classification",item.Classification); c.Parameters.AddWithValue("actor",item.AddedBy); c.Parameters.AddWithValue("added",item.AddedAtUtc); });

        foreach (var item in state.Features.DmsDocumentLinks ?? [])
            await ExecAsync("""
                INSERT INTO dms_document_links(id,agency_id,document_id,document_version,entity_type,entity_key,relationship,linked_by,linked_at_utc)
                VALUES(@id,@agency,@document,@version,@entityType,@entityKey,@relationship,@actor,@linked)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("document",item.DocumentId); c.Parameters.AddWithValue("version",item.DocumentVersion); c.Parameters.AddWithValue("entityType",item.EntityType); c.Parameters.AddWithValue("entityKey",item.EntityKey); c.Parameters.AddWithValue("relationship",item.Relationship); c.Parameters.AddWithValue("actor",item.LinkedBy); c.Parameters.AddWithValue("linked",item.LinkedAtUtc); });

        foreach (var item in state.Features.FormSignatures ?? [])
            await ExecAsync("""
                INSERT INTO form_signatures(id,form_id,template_id,template_version,signer,meaning,values_sha256,source,signed_at_utc)
                VALUES(@id,@form,@template,@version,@signer,@meaning,@sha,@source,@signed)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("form",item.FormId); c.Parameters.AddWithValue("template",item.TemplateId); c.Parameters.AddWithValue("version",item.TemplateVersion); c.Parameters.AddWithValue("signer",item.Signer); c.Parameters.AddWithValue("meaning",item.Meaning); c.Parameters.AddWithValue("sha",item.ValuesSha256); c.Parameters.AddWithValue("source",item.Source); c.Parameters.AddWithValue("signed",item.SignedAtUtc); });

        foreach (var item in state.Operational.RmsRecords)
            await ExecAsync("""
                INSERT INTO rms_records(id,agency_id,record_type,display_number,title,status,summary,incident_id,created_at_utc,updated_at_utc)
                VALUES(@id,@agency,@type,@number,@title,@status,@summary,@incident,@created,@updated)
                ON CONFLICT(id) DO UPDATE SET record_type=EXCLUDED.record_type,display_number=EXCLUDED.display_number,title=EXCLUDED.title,status=EXCLUDED.status,summary=EXCLUDED.summary,incident_id=EXCLUDED.incident_id,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("type",item.RecordType); c.Parameters.AddWithValue("number",item.Number); c.Parameters.AddWithValue("title",item.Title); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("summary",item.Summary); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Operational.RmsIncidentReports ?? [])
            await ExecAsync("""
                INSERT INTO rms_incident_reports(id,agency_id,incident_id,display_number,report_type,status,version,source_incident_version,report_json,created_at_utc,updated_at_utc,finalized_at_utc)
                VALUES(@id,@agency,@incident,@number,@type,@status,@version,@sourceVersion,CAST(@json AS jsonb),@created,@updated,@finalized)
                ON CONFLICT(id) DO UPDATE SET display_number=EXCLUDED.display_number,report_type=EXCLUDED.report_type,status=EXCLUDED.status,version=EXCLUDED.version,source_incident_version=EXCLUDED.source_incident_version,report_json=EXCLUDED.report_json,updated_at_utc=EXCLUDED.updated_at_utc,finalized_at_utc=EXCLUDED.finalized_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("number",item.Number); c.Parameters.AddWithValue("type",item.ReportType); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("sourceVersion",item.SourceIncidentVersion); c.Parameters.AddWithValue("json",JsonSerializer.Serialize(item, JsonOptions)); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); c.Parameters.AddWithValue("finalized",(object?)item.FinalizedAtUtc ?? DBNull.Value); });

        foreach (var item in state.Operational.RmsReportVersions ?? [])
            await ExecAsync("""
                INSERT INTO rms_report_versions(id,report_id,version,status,change_kind,actor,reason,snapshot_json,created_at_utc)
                VALUES(@id,@report,@version,@status,@kind,@actor,@reason,CAST(@snapshot AS jsonb),@created)
                ON CONFLICT(report_id,version) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("report",item.ReportId); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("kind",item.ChangeKind); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("reason",item.Reason); c.Parameters.AddWithValue("snapshot",item.SnapshotJson); c.Parameters.AddWithValue("created",item.CreatedAtUtc); });

        foreach (var item in state.Operational.RmsAttachments ?? [])
            await ExecAsync("""
                INSERT INTO rms_attachment_references(id,report_id,agency_id,file_name,classification,storage_reference,description,added_by,added_at_utc)
                VALUES(@id,@report,@agency,@file,@classification,@storage,@description,@actor,@added)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("report",item.ReportId); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("file",item.FileName); c.Parameters.AddWithValue("classification",item.Classification); c.Parameters.AddWithValue("storage",item.StorageReference); c.Parameters.AddWithValue("description",(object?)item.Description ?? DBNull.Value); c.Parameters.AddWithValue("actor",item.AddedBy); c.Parameters.AddWithValue("added",item.AddedAtUtc); });

        foreach (var item in state.Operational.NerisSubmissions ?? [])
            await ExecAsync("""
                INSERT INTO neris_submissions(id,rms_report_id,agency_id,incident_id,rms_report_version,profile_version,state,payload_json,payload_sha256,mapping_errors_json,mapping_warnings_json,provider_key,provider_reference,attempt_count,last_attempt_at_utc,last_error,created_by,created_at_utc,updated_at_utc)
                VALUES(@id,@report,@agency,@incident,@reportVersion,@profile,@state,CAST(@payload AS jsonb),@sha,CAST(@errors AS jsonb),CAST(@warnings AS jsonb),@provider,@reference,@attempts,@lastAttempt,@lastError,@createdBy,@created,@updated)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,provider_key=EXCLUDED.provider_key,provider_reference=EXCLUDED.provider_reference,attempt_count=EXCLUDED.attempt_count,last_attempt_at_utc=EXCLUDED.last_attempt_at_utc,last_error=EXCLUDED.last_error,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("report",item.RmsReportId); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("reportVersion",item.RmsReportVersion); c.Parameters.AddWithValue("profile",item.ProfileVersion); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("payload",item.PayloadJson); c.Parameters.AddWithValue("sha",item.PayloadSha256); c.Parameters.AddWithValue("errors",JsonSerializer.Serialize(item.MappingErrors, JsonOptions)); c.Parameters.AddWithValue("warnings",JsonSerializer.Serialize(item.MappingWarnings, JsonOptions)); c.Parameters.AddWithValue("provider",(object?)item.ProviderKey ?? DBNull.Value); c.Parameters.AddWithValue("reference",(object?)item.ProviderReference ?? DBNull.Value); c.Parameters.AddWithValue("attempts",item.AttemptCount); c.Parameters.AddWithValue("lastAttempt",(object?)item.LastAttemptAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("lastError",(object?)item.LastError ?? DBNull.Value); c.Parameters.AddWithValue("createdBy",item.CreatedBy); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Operational.NerisSubmissionAttempts ?? [])
            await ExecAsync("""
                INSERT INTO neris_submission_attempts(id,submission_id,attempt_number,operation_id,provider_key,state,actor,validation_errors_json,validation_warnings_json,provider_reference,error,requested_at_utc,completed_at_utc)
                VALUES(@id,@submission,@number,@operation,@provider,@state,@actor,CAST(@errors AS jsonb),CAST(@warnings AS jsonb),@reference,@error,@requested,@completed)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,validation_errors_json=EXCLUDED.validation_errors_json,validation_warnings_json=EXCLUDED.validation_warnings_json,provider_reference=EXCLUDED.provider_reference,error=EXCLUDED.error,completed_at_utc=EXCLUDED.completed_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("submission",item.SubmissionId); c.Parameters.AddWithValue("number",item.AttemptNumber); c.Parameters.AddWithValue("operation",item.OperationId); c.Parameters.AddWithValue("provider",item.ProviderKey); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("errors",JsonSerializer.Serialize(item.ValidationErrors, JsonOptions)); c.Parameters.AddWithValue("warnings",JsonSerializer.Serialize(item.ValidationWarnings, JsonOptions)); c.Parameters.AddWithValue("reference",(object?)item.ProviderReference ?? DBNull.Value); c.Parameters.AddWithValue("error",(object?)item.Error ?? DBNull.Value); c.Parameters.AddWithValue("requested",item.RequestedAtUtc); c.Parameters.AddWithValue("completed",(object?)item.CompletedAtUtc ?? DBNull.Value); });

        await ExecAsync("DELETE FROM idempotency_records;", _ => { });
        foreach (var item in state.IdempotencyReceipts ?? [])
            await ExecAsync("""
                INSERT INTO idempotency_records(operation_key,operation_kind,result_json,created_at_utc,expires_at_utc,request_sha256,result_type)
                VALUES(@key,@operation,CAST(@result AS jsonb),@created,NULL,@requestHash,@resultType)
                ON CONFLICT(operation_key) DO UPDATE SET operation_kind=EXCLUDED.operation_kind,result_json=EXCLUDED.result_json,created_at_utc=EXCLUDED.created_at_utc,expires_at_utc=NULL,request_sha256=EXCLUDED.request_sha256,result_type=EXCLUDED.result_type;
                """, c => { c.Parameters.AddWithValue("key",item.Key); c.Parameters.AddWithValue("operation",item.Operation); c.Parameters.AddWithValue("result",item.ResultJson); c.Parameters.AddWithValue("created",item.CommittedAtUtc); c.Parameters.AddWithValue("requestHash",item.RequestSha256); c.Parameters.AddWithValue("resultType",item.ResultType); });

        foreach (var item in state.Features.CollaborativeNarratives)
            await ExecAsync("""
                INSERT INTO collaborative_narratives(incident_id,version,body,last_editor,updated_at_utc)
                VALUES(@incident,@version,@body,@editor,@updated)
                ON CONFLICT(incident_id) DO UPDATE SET version=EXCLUDED.version,body=EXCLUDED.body,last_editor=EXCLUDED.last_editor,updated_at_utc=EXCLUDED.updated_at_utc;
                """, c => { c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("version",item.Version); c.Parameters.AddWithValue("body",item.Text); c.Parameters.AddWithValue("editor",item.LastEditor); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Features.CollaborativeNarrativeOperations ?? [])
            await ExecAsync("""
                INSERT INTO collaborative_narrative_operations(id,incident_id,client_base_version,applied_base_version,result_version,start_position,delete_count,insert_text,actor,rebased,applied_at_utc)
                VALUES(@id,@incident,@clientBase,@appliedBase,@result,@start,@deleteCount,@insertText,@actor,@rebased,@applied)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("clientBase",item.ClientBaseVersion); c.Parameters.AddWithValue("appliedBase",item.AppliedBaseVersion); c.Parameters.AddWithValue("result",item.ResultVersion); c.Parameters.AddWithValue("start",item.Start); c.Parameters.AddWithValue("deleteCount",item.DeleteCount); c.Parameters.AddWithValue("insertText",item.InsertText); c.Parameters.AddWithValue("actor",item.Actor); c.Parameters.AddWithValue("rebased",item.Rebased); c.Parameters.AddWithValue("applied",item.AppliedAtUtc); });

        foreach (var item in state.Features.OperationalMessages)
            await ExecAsync("""
                INSERT INTO operational_messages(id,agency_id,incident_id,channel,sender,recipient,priority,classification,body,reference_json,state,created_at_utc)
                VALUES(@id,@agency,@incident,@channel,@sender,@recipient,@priority,@classification,@body,CAST(@reference AS jsonb),@state,@created)
                ON CONFLICT(id) DO UPDATE SET recipient=EXCLUDED.recipient,priority=EXCLUDED.priority,classification=EXCLUDED.classification,body=EXCLUDED.body,reference_json=EXCLUDED.reference_json,state=EXCLUDED.state;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("channel",item.Channel); c.Parameters.AddWithValue("sender",item.Sender); c.Parameters.AddWithValue("recipient",(object?)item.Recipient ?? DBNull.Value); c.Parameters.AddWithValue("priority",item.Priority); c.Parameters.AddWithValue("classification",item.Classification); c.Parameters.AddWithValue("body",item.Body); c.Parameters.AddWithValue("reference",item.ReferenceJson); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("created",item.CreatedAtUtc); });

        foreach (var item in state.Features.MessageReceipts)
            await ExecAsync("""
                INSERT INTO message_receipts(id,message_id,recipient,device_id,state,queued_at_utc,delivered_at_utc,read_at_utc,acknowledged_at_utc)
                VALUES(@id,@message,@recipient,@device,@state,@queued,@delivered,@read,@ack)
                ON CONFLICT(id) DO UPDATE SET device_id=EXCLUDED.device_id,state=EXCLUDED.state,delivered_at_utc=EXCLUDED.delivered_at_utc,read_at_utc=EXCLUDED.read_at_utc,acknowledged_at_utc=EXCLUDED.acknowledged_at_utc;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("message",item.MessageId); c.Parameters.AddWithValue("recipient",item.Recipient); c.Parameters.AddWithValue("device",(object?)item.DeviceId ?? DBNull.Value); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("queued",item.QueuedAtUtc); c.Parameters.AddWithValue("delivered",(object?)item.DeliveredAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("read",(object?)item.ReadAtUtc ?? DBNull.Value); c.Parameters.AddWithValue("ack",(object?)item.AcknowledgedAtUtc ?? DBNull.Value); });

        foreach (var item in state.Features.OperationalGroups)
            await ExecAsync("""
                INSERT INTO operational_groups(id,agency_id,incident_id,name,group_type,leader,channel,status,created_at_utc,member_users_json)
                VALUES(@id,@agency,@incident,@name,@type,@leader,@channel,@status,@created,CAST(@members AS jsonb))
                ON CONFLICT(id) DO UPDATE SET name=EXCLUDED.name,group_type=EXCLUDED.group_type,leader=EXCLUDED.leader,channel=EXCLUDED.channel,status=EXCLUDED.status,member_users_json=EXCLUDED.member_users_json;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("incident",item.IncidentId); c.Parameters.AddWithValue("name",item.Name); c.Parameters.AddWithValue("type",item.Type); c.Parameters.AddWithValue("leader",item.Leader); c.Parameters.AddWithValue("channel",item.Channel); c.Parameters.AddWithValue("status",item.Status); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("members",JsonSerializer.Serialize(item.MemberUserIds ?? [], JsonOptions)); });

        foreach (var item in state.Features.ChatChannels ?? [])
            await ExecAsync("""
                INSERT INTO chat_channels(id,owner_agency_id,type,name,topic,station_id,incident_id,participating_agencies_json,member_users_json,allowed_roles_json,archived,created_by,created_at_utc,updated_at_utc,operational_group_id)
                VALUES(@id,@agency,@type,@name,@topic,@station,@incident,CAST(@agencies AS jsonb),CAST(@members AS jsonb),CAST(@roles AS jsonb),@archived,@actor,@created,@updated,@groupId)
                ON CONFLICT(id) DO UPDATE SET name=EXCLUDED.name,topic=EXCLUDED.topic,archived=EXCLUDED.archived,participating_agencies_json=EXCLUDED.participating_agencies_json,member_users_json=EXCLUDED.member_users_json,allowed_roles_json=EXCLUDED.allowed_roles_json,updated_at_utc=EXCLUDED.updated_at_utc,operational_group_id=EXCLUDED.operational_group_id;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.OwnerAgencyId); c.Parameters.AddWithValue("type",item.Type); c.Parameters.AddWithValue("name",item.Name); c.Parameters.AddWithValue("topic",item.Topic); c.Parameters.AddWithValue("station",(object?)item.StationId ?? DBNull.Value); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("agencies",JsonSerializer.Serialize(item.ParticipatingAgencyIds, JsonOptions)); c.Parameters.AddWithValue("members",JsonSerializer.Serialize(item.MemberUserIds, JsonOptions)); c.Parameters.AddWithValue("roles",JsonSerializer.Serialize(item.AllowedRoles, JsonOptions)); c.Parameters.AddWithValue("archived",item.Archived); c.Parameters.AddWithValue("actor",item.CreatedBy); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); c.Parameters.AddWithValue("groupId",(object?)item.OperationalGroupId ?? DBNull.Value); });

        foreach (var item in state.Features.ChatMessages ?? [])
            await ExecAsync("""
                INSERT INTO chat_messages(id,channel_id,owner_agency_id,sender,body,priority,classification,linked_incident_id,mentions_json,attachments_json,created_at_utc)
                VALUES(@id,@channel,@agency,@sender,@body,@priority,@classification,@incident,CAST(@mentions AS jsonb),CAST(@attachments AS jsonb),@created)
                ON CONFLICT(id) DO NOTHING;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("channel",item.ChannelId); c.Parameters.AddWithValue("agency",item.OwnerAgencyId); c.Parameters.AddWithValue("sender",item.Sender); c.Parameters.AddWithValue("body",item.Body); c.Parameters.AddWithValue("priority",item.Priority); c.Parameters.AddWithValue("classification",item.Classification); c.Parameters.AddWithValue("incident",(object?)item.LinkedIncidentId ?? DBNull.Value); c.Parameters.AddWithValue("mentions",JsonSerializer.Serialize(item.Mentions, JsonOptions)); c.Parameters.AddWithValue("attachments",JsonSerializer.Serialize(item.Attachments, JsonOptions)); c.Parameters.AddWithValue("created",item.CreatedAtUtc); });

        foreach (var item in state.Features.ChatReadStates ?? [])
            await ExecAsync("""
                INSERT INTO chat_read_states(channel_id,user_id,last_read_message_id,last_read_at_utc)
                VALUES(@channel,@user,@message,@read)
                ON CONFLICT(channel_id,user_id) DO UPDATE SET last_read_message_id=EXCLUDED.last_read_message_id,last_read_at_utc=EXCLUDED.last_read_at_utc;
                """, c => { c.Parameters.AddWithValue("channel",item.ChannelId); c.Parameters.AddWithValue("user",item.UserId); c.Parameters.AddWithValue("message",(object?)item.LastReadMessageId ?? DBNull.Value); c.Parameters.AddWithValue("read",(object?)item.LastReadAtUtc ?? DBNull.Value); });

        foreach (var item in state.Features.IntegrationEvents ?? [])
            await ExecAsync("""
                INSERT INTO integration_events(id,agency_id,provider_key,category,native_event_id,event_type,state,incident_id,unit_id,resource_key,source_timestamp,received_at,payload_sha256,payload_json,correlation_key,error)
                VALUES(@id,@agency,@provider,@category,@native,@eventType,@state,@incident,@unit,@resource,@source,@received,@sha,CAST(@payload AS jsonb),@correlation,@error)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,incident_id=EXCLUDED.incident_id,unit_id=EXCLUDED.unit_id,resource_key=EXCLUDED.resource_key,error=EXCLUDED.error;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("provider",item.ProviderKey); c.Parameters.AddWithValue("category",item.Category); c.Parameters.AddWithValue("native",item.NativeEventId); c.Parameters.AddWithValue("eventType",item.EventType); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("unit",(object?)item.UnitId ?? DBNull.Value); c.Parameters.AddWithValue("resource",(object?)item.ResourceKey ?? DBNull.Value); c.Parameters.AddWithValue("source",item.SourceTimestamp); c.Parameters.AddWithValue("received",item.ReceivedAtUtc); c.Parameters.AddWithValue("sha",item.PayloadSha256); c.Parameters.AddWithValue("payload",item.PayloadJson); c.Parameters.AddWithValue("correlation",item.CorrelationKey); c.Parameters.AddWithValue("error",(object?)item.Error ?? DBNull.Value); });

        foreach (var item in state.Features.IntegrationOperations ?? [])
            await ExecAsync("""
                INSERT INTO integration_operations(id,agency_id,provider_key,kind,state,incident_id,unit_id,request_json,request_sha256,attempt_count,provider_reference,error,possible_duplicate,created_at,updated_at)
                VALUES(@id,@agency,@provider,@kind,@state,@incident,@unit,CAST(@request AS jsonb),@sha,@attempts,@reference,@error,@duplicate,@created,@updated)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,attempt_count=EXCLUDED.attempt_count,provider_reference=EXCLUDED.provider_reference,error=EXCLUDED.error,possible_duplicate=EXCLUDED.possible_duplicate,updated_at=EXCLUDED.updated_at;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("provider",item.ProviderKey); c.Parameters.AddWithValue("kind",item.Kind); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("unit",(object?)item.UnitId ?? DBNull.Value); c.Parameters.AddWithValue("request",item.RequestJson); c.Parameters.AddWithValue("sha",item.RequestSha256); c.Parameters.AddWithValue("attempts",item.AttemptCount); c.Parameters.AddWithValue("reference",(object?)item.ProviderReference ?? DBNull.Value); c.Parameters.AddWithValue("error",(object?)item.Error ?? DBNull.Value); c.Parameters.AddWithValue("duplicate",item.PossibleDuplicate); c.Parameters.AddWithValue("created",item.CreatedAtUtc); c.Parameters.AddWithValue("updated",item.UpdatedAtUtc); });

        foreach (var item in state.Features.IntegrationAttempts ?? [])
            await ExecAsync("""
                INSERT INTO integration_attempts(id,operation_id,attempt_number,provider_operation_id,state,provider_reference,error,started_at,completed_at)
                VALUES(@id,@operation,@number,@providerOperation,@state,@reference,@error,@started,@completed)
                ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,provider_reference=EXCLUDED.provider_reference,error=EXCLUDED.error,completed_at=EXCLUDED.completed_at;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("operation",item.OperationId); c.Parameters.AddWithValue("number",item.AttemptNumber); c.Parameters.AddWithValue("providerOperation",item.ProviderOperationId); c.Parameters.AddWithValue("state",item.State); c.Parameters.AddWithValue("reference",(object?)item.ProviderReference ?? DBNull.Value); c.Parameters.AddWithValue("error",(object?)item.Error ?? DBNull.Value); c.Parameters.AddWithValue("started",item.StartedAtUtc); c.Parameters.AddWithValue("completed",(object?)item.CompletedAtUtc ?? DBNull.Value); });

        foreach (var item in state.Features.RecordingReferences ?? [])
            await ExecAsync("""
                INSERT INTO recording_references(id,agency_id,incident_id,provider_key,provider_reference,media_type,start_utc,end_utc,description,playback_uri,linked_at_utc)
                VALUES(@id,@agency,@incident,@provider,@reference,@media,@start,@end,@description,@uri,@linked)
                ON CONFLICT(id) DO UPDATE SET incident_id=EXCLUDED.incident_id,description=EXCLUDED.description,playback_uri=EXCLUDED.playback_uri;
                """, c => { c.Parameters.AddWithValue("id",item.Id); c.Parameters.AddWithValue("agency",item.AgencyId); c.Parameters.AddWithValue("incident",(object?)item.IncidentId ?? DBNull.Value); c.Parameters.AddWithValue("provider",item.ProviderKey); c.Parameters.AddWithValue("reference",item.ProviderReference); c.Parameters.AddWithValue("media",item.MediaType); c.Parameters.AddWithValue("start",item.StartUtc); c.Parameters.AddWithValue("end",item.EndUtc); c.Parameters.AddWithValue("description",item.Description); c.Parameters.AddWithValue("uri",(object?)item.PlaybackUri ?? DBNull.Value); c.Parameters.AddWithValue("linked",item.LinkedAtUtc); });
    }

    private static Guid DeterministicVersionId(Guid documentId, int version)
    {
        Span<byte> input = stackalloc byte[20];
        documentId.TryWriteBytes(input[..16]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(input[16..], version);
        var hash = System.Security.Cryptography.SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _writeGate.Dispose();
    }
}
