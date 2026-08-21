# DISRoute Pass 25 — PostgreSQL / HA production hardening and qualification harness

**Date:** 2026-08-21  
**Base checkpoint:** Pass 24 (`c99c9ab8af52b80dfa5bf53e4c1111d63430c09ef3bd5417d8b3f19ed0c28281`)

## Purpose

Pass 25 hardens the production PostgreSQL authority boundary and makes HA/DR failure behavior explicitly testable. It does not add an unrelated product feature set.

## Implemented

### Migration integrity

- Added `PostgreSqlDatabaseSafety` and immutable `disroute_schema_migrations` ledger semantics.
- Migration filenames/versions must be contiguous and every published SQL file receives a SHA-256 identity.
- Existing ledger rows must match filename and hash or startup fails closed; unexpected/extra ledger rows are rejected as well.
- Each migration SQL body and its immutable ledger identity are committed atomically in one PostgreSQL transaction, so a crash cannot leave an applied migration without its matching hash record.
- Concurrent writable-node startup serializes migration establishment with a dedicated advisory lock.
- Read-only replicas verify the replicated ledger/schema without attempting DDL.
- Normalized pre-ledger migrations 012–020 so the historical 001–020 chain deterministically advances `schema_info`; the verifier enforces this.

### Promotion and fencing

- Dispatch/HQ promotion candidates must prove their local PostgreSQL endpoint is writable and not in recovery/read-only mode.
- Eligibility is re-probed after lease acquisition.
- New primary lease epoch is committed before writes are advertised.
- Newly promoted nodes reload the latest hash-validated PostgreSQL runtime checkpoint before `AcceptingWrites=true`, then re-ping the lease/witness session and re-prove the local database is writable/non-recovery before opening the write gate.
- Every normal authoritative PostgreSQL checkpoint holds a scoped HA commit lease through database commit, so planned drain/lease release cannot race underneath an in-flight checkpoint on the same node.
- Every authoritative PostgreSQL checkpoint/projection transaction locks and verifies `ha_cluster_state` node ID + process instance ID + lease epoch, and the live advisory/witness session is revalidated immediately before commit.
- An old primary cannot make a late authoritative checkpoint after a newer epoch supersedes it.
- Active lease/witness sessions are pinged by real database I/O each coordinator cycle.

### Command crash semantics

- Leadership loss before authoritative checkpoint commit now makes the command fail and restores its in-memory mutation batch.
- A transaction that already committed under a valid fencing token is not rolled back merely because the HTTP response later fails. Same idempotency-key retry is the recovery path.
- Local HA drain/resume controls bypass the normal post-command durability filter because releasing leadership is their intentional operation and HA transition events are directly durable.

### Qualification tooling

- Added development-only crash points after durable command commit and after durable integration `Submitting` but before provider invocation. Production rejects enabling them.
- Added `scripts/postgresql-ha-qualification.sh`, guarded by an explicit destructive opt-in, with evidence output and optional replica/backup phases. The harness exact-compares all 20 database ledger filenames/hashes to the published source and makes stale-standby reload observable by proving the standby lacks the crash-boundary incident before commit and contains it after promotion before retry.
- Added `docs/ops/POSTGRESQL-HA-QUALIFICATION.md`.
- Persistence status exposes supported schema version, database role and migration-integrity state.

## Verification performed in this artifact environment

- Full 18-project `.NET 10` restore: **PASS**
- Full Release build: **PASS — 0 warnings / 0 errors**
- `DISRoute.SelfTest`: **PASS — incidents=7, units=6, audit=145, alerts=3**
- Source verifier: **PASS — 120 C# files / 18 projects / 311 API routes / 20 migrations**
- Full FileCheckpoint runtime restart + backup + staged restore regression: **PASS**
- CAD startup smoke: **PASS**
- Station startup smoke: **PASS**
- Operations Center startup smoke: **PASS**
- Station Display startup smoke: **PASS**
- Mobile startup smoke: **PASS**
- PostgreSQL qualification script syntax/safety guard: **PASS**
- Live destructive PostgreSQL 18.6 application-HA qualification: **PASS**
- Physical streaming read-only replica startup qualification: **PASS**
- Logical PostgreSQL backup/restore qualification: **PASS**
- Final measured local application crash failover: **528 ms**
- Production runtime rejects `Qualification:EnableCrashInjection=true`: **PASS**

## Live PostgreSQL qualification status

**EXECUTED AND PASSED in this artifact environment on 2026-08-21.**

A disposable PostgreSQL 18.6 / Percona Server for PostgreSQL 18.6.1 x86_64 instance was initialized locally from the supplied portable server bundle. The destructive Pass 25 harness was run repeatedly while defects were repaired, then the final run completed all core phases plus the optional physical read-only replica and logical backup/restore phases.

Final live qualification proved:

- concurrent empty-database startup serialized migrations 001–020 and produced schema version 20;
- all 20 immutable migration version/filename/SHA-256 identities exactly matched the published source;
- exactly one application writer was ready at a time;
- crash after authoritative PostgreSQL commit but before HTTP acknowledgement preserved exactly one committed command result, and idempotent retry resolved to it;
- the promoted standby exposed the committed incident before retry, proving stale in-memory state was replaced by the committed PostgreSQL checkpoint;
- crash after durable integration `Submitting` restored as `NeedsReconciliation` / `UnknownAfterRestore` instead of replaying the side effect;
- restarted old-primary instances remained fenced behind the newer lease epoch;
- planned drain/resume transferred authority without allowing the resumed node to displace the current primary;
- pre-ledger migration identity backfill succeeded and migration-hash tampering failed startup closed;
- an actual PostgreSQL physical streaming hot standby reported `pg_is_in_recovery()=true` / transaction read-only, replicated all 20 ledger rows, started DISRoute as `ReadOnlyReplica`, and never became ready/writable;
- logical `pg_dump` / `pg_restore` into a separate database retained the exact migration ledger and crash-boundary committed incident.

The final evidence is preserved at `docs/ops/evidence/PASS-25-POSTGRESQL-HA-EVIDENCE.json`. The final measured application crash failover in this local qualification topology was **528 ms**. This number is an artifact-environment observation, not a production RTO guarantee.

Two real defects were discovered by live PostgreSQL qualification and fixed before the final passing run: PostgreSQL `jsonb` canonicalization made the pre-insert checkpoint SHA-256 unverifiable after restart, and the HA drain/resume endpoints accidentally returned a nested `Task<IResult>` that ASP.NET attempted to serialize. Both fixes are included in this cumulative checkpoint.

This pass still does **not** claim production certification of PostgreSQL physical-replica **promotion**, witness-loss/network-partition behavior, STONITH/quorum, PITR/physical-backup recovery, or measured Dispatch/HQ production RPO/RTO. Those require the selected production PostgreSQL HA technology and actual Dispatch/HQ topology.

## Preserved architecture

Authority order remains **Dispatch Primary → Dispatch Local Standby → HQ DR**. Dispatch-only 911/radio/Eventide integrations remain Dispatch-site services. Automatic geographic application promotion remains off by default and does not replace database-layer promotion/quorum.
