# DISRoute — Pass 10 Notes

**Date:** 2026-08-20  
**Base:** Authoritative Pass 9 (`3dbd553ea5537f5ce8f4d3f820df52abbaf04edd29829b2c0a202fa31794352d`)  
**Theme:** Durable command idempotency across restart/failover

## Why this pass was next

Pass 9 replay and Pass 8 HA/DR were preserved. The next correctness gap found while comparing the implementation with the full architecture/resilience specification was that OperationalStore and FeatureStore kept idempotency results only in process memory and cleared them during state import. A safety-significant retry after process restart or HA failover could therefore execute twice even though the client correctly reused the original idempotency key.

## Implemented

- Added a shared `DurableIdempotencyStore` used by OperationalStore and FeatureStore.
- Each retained key is bound to:
  - authoritative operation name;
  - SHA-256 fingerprint of the request scope/payload (including target ID where applicable and authoritative actor where supplied separately);
  - result type;
  - serialized original result;
  - commit timestamp.
- Exact retries replay the original result without repeating the mutation.
- Reuse of an existing key for a different command, target or payload fails closed with an `InvalidOperationException` instead of returning an unrelated result or executing a second mutation.
- Durable receipts now cover the existing idempotent CAD/feature paths, including:
  - incident create/update/assign/status/close/reopen/narrative;
  - merge/split;
  - Dispatch Commit;
  - additional alarm and dispatch cancellation;
  - AVL advisory resolution and incident correction;
  - collaborative narrative replace/patch;
  - mobile offline queued-operation IDs.
- Runtime checkpoint schema advanced from 11 to 12 and now includes the shared receipt ledger.
- PostgreSQL checkpoint projection synchronizes receipt state to the existing `idempotency_records` table.
- Added migration `012_durable_idempotency.sql` for request SHA-256/result-type metadata.
- State import no longer clears separate store-local caches; the shared durable ledger is imported once at the top-level persistence boundary.
- Source verifier now excludes generated `bin`/`obj` C# files from the source count so build artifacts no longer change the reported source metric.

## Regression coverage added

`DISRoute.SelfTest` now proves after export/import (restart-equivalent state restoration):

- retrying the original create key returns the same incident and does not add another incident;
- retrying the original Dispatch Commit returns the same dispatch action and does not add another dispatch action;
- the tuple-shaped Dispatch Commit result survives serialization/deserialization through the receipt ledger;
- a previously accepted mobile queued operation is reported as `DuplicateIgnored`;
- reusing the original create key with a changed payload is rejected and does not change state.

`scripts/runtime-smoke.sh` now additionally proves across a real server process restart:

- retrying the original create returns the original incident;
- retrying the original close returns the original close result;
- incident count is unchanged;
- mutation-journal queued count is unchanged;
- existing Pass 9 historical replay still reconstructs the pre-close state;
- backup verification, staged restore and rollback-on-restart still pass.

## Verification

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS**.
- Source verifier: **PASS — 95 hand-written C# files, 17 projects, 230 API routes, 12 migrations**.
- Runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.

## HA/DR preservation

No Pass 8 HA/DR behavior was removed or weakened. Primary fencing, monotonic lease epochs, planned drain/resume, Dispatch/HQ topology, stale heartbeat/replication status, optional external witness requirement, primary-only checkpoint/journal/side-effect behavior and the Dispatch Primary -> Dispatch Local Standby -> HQ DR priority remain intact.

## Still intentionally not claimed complete

This pass closes the restart/failover retry-deduplication gap in the current command model. The larger hardening item remains: move safety-significant commands toward fully normalized PostgreSQL per-command transaction boundaries where authoritative relational state, idempotency receipt, journal and outbox are committed atomically. Real multi-node PostgreSQL replication/promotion/PITR/network-partition/DR qualification also still requires target infrastructure.
