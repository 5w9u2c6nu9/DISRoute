using System.Text;
using System.Security.Cryptography;
using Npgsql;

namespace DISRoute.Server.Operational;

internal sealed record PublishedPostgreSqlMigration(int Version, string FileName, string Sha256, string Sql);

internal static class PostgreSqlDatabaseSafety
{
    internal const long MigrationAdvisoryLockKey = 0x4452534D494752; // stable DISRoute migration lock key
    public static async Task EnsureUtf8EncodingAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW server_encoding";
        var value = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        if (!string.Equals(value, "UTF8", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "UTF-8", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"DISRoute PostgreSQL requires UTF8 database encoding because operational JSON and text may contain Unicode. " +
                $"The connected database reports server_encoding='{value ?? "unknown"}'. Create or restore the DISRoute database with UTF8 encoding before startup.");
        }
    }

    public static async Task<bool> IsWritablePrimaryAsync(NpgsqlConnection connection, bool proveWrite, CancellationToken cancellationToken)
    {
        await using (var state = connection.CreateCommand())
        {
            state.CommandText = """
                SELECT NOT pg_is_in_recovery()
                   AND current_setting('transaction_read_only') = 'off'
                   AND current_setting('default_transaction_read_only') = 'off'
                """;
            if (!((bool?)(await state.ExecuteScalarAsync(cancellationToken)) ?? false)) return false;
        }

        if (!proveWrite) return true;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var writeProbe = connection.CreateCommand())
        {
            writeProbe.Transaction = transaction;
            writeProbe.CommandText = """
                CREATE TEMP TABLE disroute_ha_write_probe(value integer) ON COMMIT DROP;
                INSERT INTO disroute_ha_write_probe(value) VALUES (1);
                """;
            await writeProbe.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.RollbackAsync(cancellationToken);
        return true;
    }

    public static IReadOnlyList<PublishedPostgreSqlMigration> LoadPublishedMigrations(string migrationDirectory)
    {
        if (!Directory.Exists(migrationDirectory))
            throw new DirectoryNotFoundException($"DISRoute migration directory not found: {migrationDirectory}");

        var files = Directory.GetFiles(migrationDirectory, "*.sql")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidOperationException("No DISRoute PostgreSQL migrations were published.");

        var migrations = new List<PublishedPostgreSqlMigration>(files.Length);
        var expectedVersion = 1;
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var separator = fileName.IndexOf('_');
            if (separator <= 0 || !int.TryParse(fileName.AsSpan(0, separator), out var version))
                throw new InvalidOperationException($"Migration filename must begin with a numeric version prefix: {fileName}");
            if (version != expectedVersion)
                throw new InvalidOperationException($"PostgreSQL migration sequence is not contiguous. Expected {expectedVersion:000}, found {version:000} ({fileName}).");

            var sql = File.ReadAllText(file);
            var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
            migrations.Add(new PublishedPostgreSqlMigration(version, fileName, sha256, sql));
            expectedVersion++;
        }

        return migrations;
    }

    public static async Task EnsureMigrationLedgerAndApplyAsync(
        NpgsqlConnection connection,
        IReadOnlyList<PublishedPostgreSqlMigration> migrations,
        CancellationToken cancellationToken)
    {
        await using (var bootstrap = connection.CreateCommand())
        {
            bootstrap.CommandText = """
                CREATE TABLE IF NOT EXISTS disroute_schema_migrations (
                    version integer PRIMARY KEY,
                    file_name text NOT NULL UNIQUE,
                    sha256 text NOT NULL,
                    applied_at_utc timestamptz NOT NULL DEFAULT now()
                );
                """;
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var migration in migrations)
        {
            var existing = await ReadLedgerEntryAsync(connection, migration.Version, cancellationToken);
            if (existing is not null)
            {
                ValidateLedgerEntry(migration, existing.Value.FileName, existing.Value.Sha256);
                continue;
            }

            // Apply the published migration and establish its immutable identity under one real
            // Npgsql transaction. Historical migrations which already carry an outer BEGIN/COMMIT
            // wrapper have only that wrapper removed; the migration body itself is unchanged. A
            // crash therefore commits both the schema change and its ledger identity, or neither.
            await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
            {
                await using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = GetMigrationBodyForTransaction(migration);
                    command.CommandTimeout = 120;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var record = connection.CreateCommand())
                {
                    record.Transaction = transaction;
                    record.CommandText = """
                        INSERT INTO disroute_schema_migrations(version, file_name, sha256, applied_at_utc)
                        VALUES (@version, @file, @sha, now());
                        """;
                    record.Parameters.AddWithValue("version", migration.Version);
                    record.Parameters.AddWithValue("file", migration.FileName);
                    record.Parameters.AddWithValue("sha", migration.Sha256);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }

            var recorded = await ReadLedgerEntryAsync(connection, migration.Version, cancellationToken)
                ?? throw new InvalidOperationException($"Migration {migration.Version:000} applied but its integrity ledger row was not recorded.");
            ValidateLedgerEntry(migration, recorded.FileName, recorded.Sha256);
        }

        await VerifyMigrationLedgerAsync(connection, migrations, cancellationToken);
    }


    internal static string GetMigrationBodyForTransaction(PublishedPostgreSqlMigration migration)
    {
        var sql = migration.Sql.Trim();
        const string begin = "BEGIN;";
        const string commit = "COMMIT;";
        var startsTransaction = sql.StartsWith(begin, StringComparison.OrdinalIgnoreCase);
        var endsTransaction = sql.EndsWith(commit, StringComparison.OrdinalIgnoreCase);
        if (startsTransaction != endsTransaction)
            throw new InvalidOperationException($"Migration {migration.FileName} has an incomplete explicit transaction wrapper.");

        if (!startsTransaction) return sql;

        var body = sql[begin.Length..^commit.Length].Trim();
        if (body.Length == 0)
            throw new InvalidOperationException($"Migration {migration.FileName} has an empty transaction body.");
        return body;
    }

    public static async Task VerifyMigrationLedgerAsync(
        NpgsqlConnection connection,
        IReadOnlyList<PublishedPostgreSqlMigration> migrations,
        CancellationToken cancellationToken)
    {
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT to_regclass('public.disroute_schema_migrations') IS NOT NULL";
            if (!((bool?)(await exists.ExecuteScalarAsync(cancellationToken)) ?? false))
                throw new InvalidOperationException("PostgreSQL standby does not contain the Pass 25 migration integrity ledger. Upgrade the writable primary before starting this replica with Pass 25.");
        }

        await using (var cardinality = connection.CreateCommand())
        {
            cardinality.CommandText = "SELECT count(*) FROM disroute_schema_migrations";
            var actualRows = Convert.ToInt32(await cardinality.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (actualRows != migrations.Count)
                throw new InvalidOperationException($"PostgreSQL migration ledger contains {actualRows} rows, expected exactly {migrations.Count}. Unknown, missing, or future migration identities are not accepted by this build.");
        }

        foreach (var migration in migrations)
        {
            var existing = await ReadLedgerEntryAsync(connection, migration.Version, cancellationToken)
                ?? throw new InvalidOperationException($"PostgreSQL migration ledger is incomplete: migration {migration.Version:000} ({migration.FileName}) is missing.");
            ValidateLedgerEntry(migration, existing.FileName, existing.Sha256);
        }

        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT version FROM schema_info WHERE singleton = true";
        var schemaValue = await schema.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("PostgreSQL schema_info row is missing.");
        var actualVersion = Convert.ToInt32(schemaValue, System.Globalization.CultureInfo.InvariantCulture);
        var expectedVersion = migrations[^1].Version;
        if (actualVersion != expectedVersion)
            throw new InvalidOperationException($"PostgreSQL schema_info version is {actualVersion}, expected {expectedVersion}.");
    }

    private static async Task<(string FileName, string Sha256)?> ReadLedgerEntryAsync(
        NpgsqlConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT file_name, sha256 FROM disroute_schema_migrations WHERE version = @version";
        lookup.Parameters.AddWithValue("version", version);
        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetString(0), reader.GetString(1));
    }

    private static void ValidateLedgerEntry(PublishedPostgreSqlMigration migration, string recordedFile, string recordedHash)
    {
        if (string.Equals(recordedFile, migration.FileName, StringComparison.Ordinal) &&
            string.Equals(recordedHash, migration.Sha256, StringComparison.OrdinalIgnoreCase)) return;

        throw new InvalidOperationException(
            $"PostgreSQL migration {migration.Version:000} integrity mismatch. " +
            $"Database records {recordedFile} / {recordedHash}; published source is {migration.FileName} / {migration.Sha256}. " +
            "Historical migrations are immutable; add a new migration instead of editing an applied one.");
    }
}
