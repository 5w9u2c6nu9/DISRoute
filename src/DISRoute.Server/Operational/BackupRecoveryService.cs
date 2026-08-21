using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DISRoute.Contracts.Operational;

namespace DISRoute.Server.Operational;

/// <summary>
/// Creates portable, integrity-checked DISRoute state backups without embedding configuration secrets.
/// FileCheckpoint restores are staged and only applied during the next server start so live CAD traffic
/// can never observe a half-restored in-memory state. PostgreSQL recovery remains an infrastructure-level
/// PITR/restore operation and is intentionally not performed by the running application.
/// </summary>
public sealed class BackupRecoveryService : IDisposable
{
    private const int BackupFormatVersion = 1;
    private const string ManifestEntryName = "manifest.json";
    private const string StateEntryName = "state.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly StatePersistenceService _persistence;
    private readonly OperationalMutationBuffer _mutationBuffer;
    private readonly FeatureStore _features;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackupRecoveryService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed record BackupManifest(
        int FormatVersion,
        string Product,
        int StateSchemaVersion,
        DateTimeOffset CreatedAtUtc,
        string SourceProvider,
        string StateSha256,
        long StateBytes,
        string CreatedBy,
        string? Note,
        string ServerVersion);

    public BackupRecoveryService(
        StatePersistenceService persistence,
        OperationalMutationBuffer mutationBuffer,
        FeatureStore features,
        IConfiguration configuration,
        ILogger<BackupRecoveryService> logger)
    {
        _persistence = persistence;
        _mutationBuffer = mutationBuffer;
        _features = features;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<BackupArtifactDto> CreateAsync(CreatePortableBackupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var target = _mutationBuffer.Queued;
            var timeoutSeconds = Math.Clamp(_configuration.GetValue("Persistence:CommitTimeoutSeconds", 5), 1, 60);
            if (!await _mutationBuffer.WaitUntilPersistedAsync(target, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken))
                throw new InvalidOperationException("Cannot create a backup while the durable mutation journal is behind authoritative state.");

            await _persistence.FlushAsync(cancellationToken);
            if (!string.Equals(_persistence.Status.State, "Healthy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(_persistence.Status.LastError ?? "Persistence is degraded; backup creation was refused.");

            var stateJson = await _persistence.ExportJsonAsync(cancellationToken);
            var state = JsonSerializer.Deserialize<DISRoutePersistedState>(stateJson, JsonOptions)
                ?? throw new InvalidDataException("DISRoute exported an empty runtime state.");
            if (state.SchemaVersion > _persistence.SupportedSchemaVersion)
                throw new InvalidDataException($"State schema {state.SchemaVersion} is newer than supported schema {_persistence.SupportedSchemaVersion}.");

            var stateBytes = Encoding.UTF8.GetBytes(stateJson);
            var stateSha = Sha256Hex(stateBytes);
            var createdAtUtc = DateTimeOffset.UtcNow;
            var actor = string.IsNullOrWhiteSpace(request.Actor) ? "unknown" : request.Actor.Trim();
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            var manifest = new BackupManifest(
                BackupFormatVersion,
                "DISRoute",
                state.SchemaVersion,
                createdAtUtc,
                _persistence.Provider,
                stateSha,
                stateBytes.LongLength,
                actor,
                note,
                typeof(BackupRecoveryService).Assembly.GetName().Version?.ToString() ?? string.Empty);

            var directory = BackupDirectory();
            Directory.CreateDirectory(directory);
            var fileName = $"disroute-backup-{createdAtUtc:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.zip";
            var finalPath = Path.Combine(directory, fileName);
            var temporaryPath = finalPath + ".tmp";

            try
            {
                await WriteArchiveAsync(temporaryPath, manifest, stateBytes, cancellationToken);
                File.Move(temporaryPath, finalPath, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            var verification = await VerifyPathAsync(finalPath, cancellationToken);
            if (!verification.Valid)
            {
                File.Delete(finalPath);
                throw new InvalidDataException($"Newly-created DISRoute backup failed verification: {verification.Error}");
            }

            var archiveSha = verification.ArchiveSha256 ?? throw new InvalidDataException("Backup verification did not return an archive checksum.");
            var info = new FileInfo(finalPath);
            _features.SetBackupStatus(new BackupStatusDto("PortableState", "Healthy", createdAtUtc, null, $"Verified backup {fileName}"));
            PruneOldBackups(directory);
            OperationalLog.PortableBackupCreated(_logger, fileName);
            return new BackupArtifactDto(fileName, info.Length, createdAtUtc, state.SchemaVersion, _persistence.Provider, stateSha, archiveSha, true, note);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupArtifactDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var directory = BackupDirectory();
        if (!Directory.Exists(directory)) return [];
        var results = new List<BackupArtifactDto>();
        foreach (var path in Directory.EnumerateFiles(directory, "disroute-backup-*.zip", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetCreationTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verification = await VerifyPathAsync(path, cancellationToken);
            if (verification.Valid)
            {
                var manifest = await ReadManifestAsync(path, cancellationToken);
                var info = new FileInfo(path);
                results.Add(new BackupArtifactDto(
                    info.Name,
                    info.Length,
                    manifest.CreatedAtUtc,
                    manifest.StateSchemaVersion,
                    manifest.SourceProvider,
                    manifest.StateSha256,
                    verification.ArchiveSha256 ?? string.Empty,
                    true,
                    manifest.Note));
            }
            else
            {
                var info = new FileInfo(path);
                results.Add(new BackupArtifactDto(info.Name, info.Length, info.CreationTimeUtc, 0, "Unknown", string.Empty, verification.ArchiveSha256 ?? string.Empty, false, verification.Error));
            }
        }
        return results;
    }

    public async Task<BackupVerificationDto> VerifyAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = ResolveBackupPath(fileName);
        return await VerifyPathAsync(path, cancellationToken);
    }

    public async Task<StageRestoreResultDto> StageRestoreAsync(string fileName, StageRestoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Confirmation, "RESTORE ON RESTART", StringComparison.Ordinal))
            throw new InvalidOperationException("Restore confirmation must exactly equal 'RESTORE ON RESTART'.");
        if (!_persistence.Provider.Equals("FileCheckpoint", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Online application restore is disabled for PostgreSQL. Use the qualified PostgreSQL backup/PITR recovery procedure instead.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = ResolveBackupPath(fileName);
            var verification = await VerifyPathAsync(path, cancellationToken);
            if (!verification.Valid) throw new InvalidDataException(verification.Error ?? "Backup verification failed.");
            var stateJson = await ReadStateJsonAsync(path, cancellationToken);
            var state = JsonSerializer.Deserialize<DISRoutePersistedState>(stateJson, JsonOptions)
                ?? throw new InvalidDataException("Backup state was empty.");
            if (state.SchemaVersion > _persistence.SupportedSchemaVersion)
                throw new InvalidOperationException($"Backup state schema {state.SchemaVersion} is newer than supported schema {_persistence.SupportedSchemaVersion}.");

            var pendingPath = _persistence.PendingRestorePath
                ?? throw new InvalidOperationException("The active persistence provider does not support staged application restore.");
            var hash = Sha256Hex(Encoding.UTF8.GetBytes(stateJson));
            var envelope = new FileCheckpointEnvelope(state.SchemaVersion, state.SavedAtUtc, stateJson, hash);
            var envelopeJson = JsonSerializer.Serialize(envelope, JsonOptions);
            var temporaryPath = pendingPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(pendingPath) ?? AppContext.BaseDirectory);
            try
            {
                await File.WriteAllTextAsync(temporaryPath, envelopeJson, new UTF8Encoding(false), cancellationToken);
                await FlushFileAsync(temporaryPath, cancellationToken);
                File.Move(temporaryPath, pendingPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            _features.SetBackupStatus(new BackupStatusDto("PortableState", "RestoreStaged", null, null, $"{Path.GetFileName(path)} staged by {request.Actor}; restart required."));
            OperationalLog.PortableBackupStaged(_logger, Path.GetFileName(path));
            return new StageRestoreResultDto(Path.GetFileName(path), true, true, "Verified backup staged. Restart DISRoute.Server to apply it. The existing checkpoint will be preserved as a pre-restore rollback file.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private string BackupDirectory()
    {
        var configured = _configuration["Persistence:BackupDirectory"];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        if (_persistence.Provider.Equals("FileCheckpoint", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_persistence.Location))
            return Path.Combine(Path.GetDirectoryName(_persistence.Location) ?? AppContext.BaseDirectory, "backups");
        return Path.Combine(AppContext.BaseDirectory, "data", "backups");
    }

    private string ResolveBackupPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Backup file name is required.", nameof(fileName));
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal) || !safeName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid backup file name.");
        var path = Path.Combine(BackupDirectory(), safeName);
        if (!File.Exists(path)) throw new FileNotFoundException("DISRoute backup was not found.", safeName);
        return path;
    }

    private async Task<BackupVerificationDto> VerifyPathAsync(string path, CancellationToken cancellationToken)
    {
        string? archiveSha = null;
        try
        {
            archiveSha = await Sha256FileAsync(path, cancellationToken);
            var manifest = await ReadManifestAsync(path, cancellationToken);
            if (manifest.FormatVersion != BackupFormatVersion) throw new InvalidDataException($"Unsupported backup format {manifest.FormatVersion}.");
            if (!string.Equals(manifest.Product, "DISRoute", StringComparison.Ordinal)) throw new InvalidDataException("Backup product marker is invalid.");
            if (manifest.StateSchemaVersion > _persistence.SupportedSchemaVersion) throw new InvalidDataException($"Backup schema {manifest.StateSchemaVersion} is newer than supported schema {_persistence.SupportedSchemaVersion}.");
            var stateJson = await ReadStateJsonAsync(path, cancellationToken);
            var bytes = Encoding.UTF8.GetBytes(stateJson);
            var actualStateSha = Sha256Hex(bytes);
            if (bytes.LongLength != manifest.StateBytes) throw new InvalidDataException("Backup state length does not match its manifest.");
            if (!FixedTimeHexEquals(manifest.StateSha256, actualStateSha)) throw new InvalidDataException("Backup state failed SHA-256 integrity validation.");
            var state = JsonSerializer.Deserialize<DISRoutePersistedState>(stateJson, JsonOptions) ?? throw new InvalidDataException("Backup state JSON is invalid.");
            if (state.SchemaVersion != manifest.StateSchemaVersion) throw new InvalidDataException("Backup manifest and state schema versions do not match.");
            return new BackupVerificationDto(Path.GetFileName(path), true, state.SchemaVersion, manifest.CreatedAtUtc, actualStateSha, archiveSha, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or CryptographicException)
        {
            return new BackupVerificationDto(Path.GetFileName(path), false, null, null, null, archiveSha, ex.Message);
        }
    }

    private static async Task WriteArchiveAsync(string path, BackupManifest manifest, byte[] stateBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            await using (var entryStream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(entryStream, manifest, JsonOptions, cancellationToken);

            var stateEntry = archive.CreateEntry(StateEntryName, CompressionLevel.Optimal);
            await using (var entryStream = stateEntry.Open())
                await entryStream.WriteAsync(stateBytes, cancellationToken);
        }
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task<BackupManifest> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(ManifestEntryName) ?? throw new InvalidDataException("Backup manifest is missing.");
        await using var entryStream = entry.Open();
        return await JsonSerializer.DeserializeAsync<BackupManifest>(entryStream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Backup manifest is invalid.");
    }

    private static async Task<string> ReadStateJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(StateEntryName) ?? throw new InvalidDataException("Backup state payload is missing.");
        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private void PruneOldBackups(string directory)
    {
        var retention = Math.Clamp(_configuration.GetValue("Persistence:BackupRetentionCount", 30), 1, 1000);
        foreach (var path in Directory.EnumerateFiles(directory, "disroute-backup-*.zip", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Skip(retention))
            File.Delete(path);
    }

    private static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static async Task FlushFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
