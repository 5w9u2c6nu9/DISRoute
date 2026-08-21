# DISRoute PostgreSQL / HA qualification — Pass 25

This runbook qualifies the Pass 25 PostgreSQL authority/fencing path against disposable, target-like infrastructure. It is intentionally destructive and is not a normal application smoke test.

## What Pass 25 protects

- Published migrations 001–020 are identified by immutable filename + SHA-256 ledger rows; unexpected ledger rows fail validation.
- A migration SQL body and its ledger row commit atomically in the same PostgreSQL transaction.
- Concurrent DISRoute startup serializes migration application with a dedicated PostgreSQL advisory lock.
- A promotion candidate must prove its local database is not in recovery, is not transaction/default read-only, and can perform an actual rolled-back write.
- After winning a new application lease epoch, a standby reloads the latest authoritative PostgreSQL checkpoint, then revalidates the lease/witness session and local database writability before advertising writes.
- Normal authoritative checkpoint commits hold a scoped HA commit lease through PostgreSQL commit, preventing local drain/lease release from racing an in-flight transaction.
- Every authoritative checkpoint/projection transaction row-locks `ha_cluster_state`, must match its node ID, process instance ID and lease epoch, and re-pings the live lease/witness session immediately before commit.
- A command that loses authority before the PostgreSQL checkpoint cannot return a false durable success.
- A provider operation restored from the durable `Submitting` boundary becomes `NeedsReconciliation` rather than being blindly sent again.

## Destructive harness prerequisites

Required:

- disposable PostgreSQL database (never production)
- `psql`
- .NET 10 Release build of DISRoute
- `curl`, `python3`
- Npgsql and libpq forms of the target connection string

Run:

```bash
export DISROUTE_PGQUAL_ALLOW_DESTRUCTIVE=YES_I_UNDERSTAND
export DISROUTE_PGQUAL_CONNECTION_STRING='Host=127.0.0.1;Port=5432;Database=disroute_qual;Username=disroute;Password=...'
export DISROUTE_PGQUAL_PSQL_DSN='postgresql://disroute:password@127.0.0.1:5432/disroute_qual'
./scripts/postgresql-ha-qualification.sh
```

The first phase drops and recreates the database's `public` schema. The safety token is therefore mandatory.

## Optional target-like inputs

```bash
export DISROUTE_PGQUAL_READONLY_CONNECTION_STRING='Host=...;Database=...;Username=...;Password=...'
export DISROUTE_PGQUAL_WITNESS_CONNECTION_STRING='Host=...;Database=...;Username=...;Password=...'
export DISROUTE_PGQUAL_RESTORE_PSQL_DSN='postgresql://.../disroute_restore_qual'
```

`DISROUTE_PGQUAL_REPLICA_CONNECTION_STRING` is accepted as an alias for the read-only replica input. `pg_dump`/`pg_restore` are required only for the optional logical recovery phase.

## Required harness evidence

The harness fails on the first violated invariant and, on success, writes `evidence.json` below `DISROUTE_PGQUAL_WORK` (default `/tmp/disroute-pgqual`). It proves:

1. two application nodes can start concurrently on an empty database while migrations are serialized;
2. the migration ledger has exactly 20 rows, every version/filename/SHA-256 exactly matches the published source, and `schema_info.version=20`;
3. exactly one fresh application writer exists;
4. a forced process crash after database commit but before HTTP response preserves exactly one incident and same-key retry resolves to it;
5. the standby API lacks the crash-boundary incident before the active commit, then the promoted standby exposes that committed incident before any retry is sent, directly proving stale in-memory state was replaced by promotion reload;
6. the restarted old primary does not reclaim authority while the survivor owns the newer epoch;
7. a crash after durable provider `Submitting` but before provider invocation restores that operation as `NeedsReconciliation` / `UnknownAfterRestore`;
8. planned drain transfers authority and resume does not displace the current primary;
9. a pre-ledger database can establish the migration ledger from the idempotent historical chain;
10. tampering an applied migration ledger hash makes startup fail closed;
11. optional read-only replica startup reports `ReadOnlyReplica` and never becomes ready/writable;
12. optional logical backup/restore retains the ledger and crash-boundary committed incident.

## Qualification-only crash injection

The harness starts servers in Development and explicitly enables `Qualification:EnableCrashInjection`. Supported request header points are:

- `X-DISRoute-Qualification-Crash: after-durable-commit`
- `X-DISRoute-Qualification-Crash: integration-after-submitting`

Production startup rejects `Qualification:EnableCrashInjection=true`. Do not weaken that guard.

## Geographic DR boundary

Application promotion and database promotion are separate layers. The HQ database/platform layer must decide when a physical replica is safe to promote and must provide the required quorum/witness/STONITH semantics. DISRoute automatic geographic application promotion remains disabled by default and requires an independent configured witness plus a locally writable database.

The application can inspect replication lag, but Pass 25 does **not** claim that DISRoute itself safely promotes a PostgreSQL physical replica or resolves a two-datacenter database split brain. Agencies must qualify their selected PostgreSQL HA technology and recovery-point policy separately.

## Checkpoint-environment result

Pass 25 live qualification was executed on 2026-08-21 against a disposable local PostgreSQL 18.6 / Percona Server for PostgreSQL 18.6.1 x86_64 primary. A second PostgreSQL instance was created with `pg_basebackup -R` and verified as a physical streaming hot standby (`pg_is_in_recovery()=true`, transaction read-only), and a separate restore database was used for the logical `pg_dump` / `pg_restore` phase.

The final harness run passed all core phases, the read-only replica phase, and the logical backup/restore phase. Its exact evidence is preserved at `docs/ops/evidence/PASS-25-POSTGRESQL-HA-EVIDENCE.json`; measured application crash failover in that local run was 528 ms. That measurement is not a production RTO guarantee.

The checkpoint does not claim production physical-replica promotion, witness-loss/network-partition, STONITH/quorum, PITR/physical-backup recovery, or Dispatch/HQ production RPO/RTO certification; those remain deployment-topology acceptance work.
