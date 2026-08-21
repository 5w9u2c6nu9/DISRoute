# DISRoute Pass 26 — PostgreSQL Physical HA + Dispatch/HQ DR Qualification

**Status:** COMPLETE — cumulative checkpoint candidate pending final packaging gates  
**Date:** 2026-08-21  
**Baseline:** Pass 25 (`e82970ac03d536a8b69c004fea18ff33d96098e6eaf59f5a8627b476e4eaac08`)

## Purpose

Pass 26 extends the application-level PostgreSQL correctness proven in Pass 25 into a real physical-streaming failover/failback lab. It does not replace PostgreSQL HA infrastructure. Instead it defines and enforces the boundary: when independently writable PostgreSQL timelines are possible, DISRoute requires an independent application witness and requires the database HA layer to provide external fencing/STONITH before promotion.

## Production source changes

- Added `HaDatabaseTopologySafety` with explicit `SharedPrimary` and `PhysicalFailover` modes.
- `PhysicalFailover` fails startup unless both an independent `WitnessConnectionString` and `DatabaseFencingMode=External` are configured.
- HA status now exposes database topology, database fencing mode, and whether an external witness is configured.
- Physical hot standbys no longer attempt HA heartbeat writes while PostgreSQL is in recovery.
- PostgreSQL realtime relay uses read-only durable-outbox polling while a physical replica is in recovery, then returns to LISTEN/NOTIFY after promotion.
- Split-brain evaluation counts only writer heartbeats from the current durable lease epoch. Replicated stale writer rows from a previous timeline/epoch no longer create a false split-brain condition after a valid promotion.
- Deployment examples retain `SharedPrimary` / `None` as the conservative default; production operators must deliberately opt into physical-failover semantics.
- SelfTest and source verification enforce the physical-HA safety contract and qualification artifacts.

## Destructive qualification

`scripts/postgresql-physical-ha-qualification.sh` was executed against Percona Server for PostgreSQL 18.6 (PostgreSQL 18.6-compatible binaries) using three real PostgreSQL clusters: Dispatch database primary, HQ physical streaming standby, and an independent witness. Network partitions were injected through `scripts/tcp-fault-proxy.py`.

The uninterrupted final run proved:

1. Dispatch starts as the sole authoritative application writer.
2. HQ starts against a real physical hot standby and remains read-only.
3. Loss of the Dispatch witness path fails closed to zero writers.
4. Loss of the Dispatch database path fails closed to zero writers.
5. After the old database primary is explicitly fenced/stopped, the HQ physical standby can be promoted and HQ application authority transfers through the independent witness.
6. Restarting the un-rewound old Dispatch PostgreSQL primary does not allow its application node to become authoritative while HQ owns the witness.
7. `pg_rewind` safely returns the former Dispatch database primary as a streaming standby.
8. Planned physical failback to Dispatch succeeds only after HQ is explicitly fenced and the Dispatch standby is promoted.
9. Archive-assisted `pg_rewind` safely returns the former HQ primary as a streaming standby.
10. A physical base backup plus archived WAL performs PITR to an exact transaction-time boundary: the incident committed before the target is present and the later incident is absent.
11. The 20-row immutable migration ledger survives the physical/PITR path.

Final evidence is preserved at `docs/ops/evidence/PASS-26-POSTGRESQL-PHYSICAL-HA-EVIDENCE.json`.

### Qualification measurements

- Physical Dispatch → HQ application failover: **2485 ms** in the disposable local lab.
- Physical HQ → Dispatch application failback: **3112 ms** in the disposable local lab.
- Full destructive physical qualification: **38885 ms**.

These are measurements from this qualification environment, **not production RTO guarantees**.

## Pass 25 regression

The Pass 25 destructive PostgreSQL/HA harness was rerun on the Pass 26 source and passed its mandatory phases: 20 migration hashes, exactly-one-writer, crash-after-commit, stale standby reload, provider `Submitting` reconciliation, planned drain/resume, old-primary fencing, pre-ledger backfill, and migration-tamper rejection. Optional replica/restore phases were skipped in that regression invocation because the stronger Pass 26 physical/PITR harness separately qualified those concerns.

## Explicit non-claims

Pass 26 does **not** certify a particular production PostgreSQL HA manager, cluster quorum product, hardware STONITH implementation, WAN topology, storage platform, or cloud failover service. The qualification harness performs explicit PostgreSQL process fencing before promotion. A real deployment must provide equivalent or stronger external database fencing through its chosen PostgreSQL HA layer.

Pass 26 also does not claim live certification of 911, radio, Eventide/recording, NERIS, GIS, ONVIF, AIS, or other vendor systems, nor does it turn Dispatch-only 911/radio/recording gateways into HQ services.

## Final checkpoint gates

On the exact cumulative Pass 26 source tree before packaging:

- offline .NET 10 restore: **PASS**;
- full 18-project Release build: **PASS — 0 warnings / 0 errors**;
- SelfTest: **PASS — incidents=7, units=6, audit=145, alerts=3**;
- source verifier: **PASS — 121 C# files / 18 projects / 311 API routes / 20 migrations**;
- Pass 25 destructive PostgreSQL/HA mandatory-phase regression: **PASS**;
- Pass 26 physical PostgreSQL HA/DR/PITR qualification: **PASS**;
- FileCheckpoint restart / backup / staged restore regression: **PASS**;
- Production qualification crash-injection rejection: **PASS**;
- CAD / Station / Operations Center / Station Display / Mobile startup smokes: **PASS**.
