# DISRoute — Pass 19 Notes

**Date:** 2026-08-20  
**Base checkpoint:** Pass 18  
**Scope:** RMS-linked NERIS bridge mapping, validation, durable submission queue, failure/retry/reconciliation and operator workflow. No HA/DR redesign and no scope reduction.

## Implemented

- Finalized canonical RMS report versions are now the authoritative source for the new NERIS path.
- Added versioned `DISRoute-NERIS-Bridge-2026.1` projection with deterministic JSON payload and SHA-256 payload identity.
- Projection includes RMS/report identity, agency/incident identity, incident/location/caller details, units, command assignments, mutual aid, timeline, narrative/summary and attachment references.
- Mapping validation is explicit and distinct from provider validation. Unsupported bridge-profile versions fail closed.
- Added durable NERIS submission state with explicit `Ready`, `Queued`, `Submitting`, `Accepted`, `Confirmed`, `Failed`, `NeedsReconciliation` and `Superseded` semantics.
- Added immutable provider-attempt records with attempt number, operation ID, provider key, actor, validation errors/warnings, provider reference, error and timestamps.
- Added authoritative-writer-only NERIS submission worker. The worker persists `Submitting` before the external provider call and persists the provider result afterward; PostgreSQL mode uses the existing authoritative command/checkpoint and mutation-journal durability barrier.
- Restored in-flight submissions become `NeedsReconciliation` and their started attempt becomes `UnknownAfterRestore`; they are never automatically retried.
- Retry of an ambiguous submission requires explicit possible-duplicate acknowledgement unless the operator first reconciles the provider outcome.
- Provider success without a durable provider reference becomes `NeedsReconciliation` rather than being treated as complete.
- Added provider-validation preview and direct JSON export APIs.
- Added queue, retry and reconciliation API/client methods plus `nerisSubmissionChanged` realtime broadcasts.
- Operations Center now includes a dedicated NERIS Submissions workflow showing finalized RMS source reports, durable submission state, payload hash/JSON, mapping diagnostics, provider validation, queue/retry controls, reconciliation controls and immutable attempt history.
- Legacy manual NERIS creation now rejects incident/agency mismatch; its submit endpoint uses the authenticated server actor instead of trusting client actor text.
- Portable checkpoint state, after-action exports and records exports include RMS-linked NERIS submissions and attempt history.
- PostgreSQL migration `015_neris_submission_pipeline.sql` adds normalized submission/attempt tables. Schema version is now 15.
- RMS validation now keeps provider validation pending until the current RMS version has actually passed provider validation during a completed submission attempt.

## Verification

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=7, units=6, audit=90, alerts=2**.
- Source verifier after implementation: **105 C# files, 17 projects, 255 API routes, 15 migrations** (final count rechecked during packaging).
- Runtime persistence/idempotency/backup/staged-restore/historical-replay/RMS/NERIS smoke: **PASS**.
- Runtime NERIS transport regression deliberately fails the first attempt against unreachable `http-fire-reporting`, retries via `simulation-fire-reporting`, then verifies both immutable attempts and the accepted provider reference after restart and staged restore.
- Avalonia startup smoke: **PASS** for CAD, Station, Operations Center and Station Display.

## SelfTest coverage added

- Finalized RMS -> NERIS projection identity and SHA-256 payload hash.
- Bridge payload contains DISRoute profile and source incident identity.
- One projection per RMS version/profile.
- Unsupported bridge profile rejection.
- Durable queue state and in-flight attempt state.
- Export/import of an in-flight attempt becomes `NeedsReconciliation` / `UnknownAfterRestore`.
- Unsafe retry after ambiguous restore is rejected.
- Reconciled failure can be safely re-queued while retaining the immutable prior attempt.

## Qualification boundary

`DISRoute-NERIS-Bridge-2026.1` is a DISRoute provider-neutral bridge profile and is **not represented as an officially certified NERIS production schema**. Official NERIS/profile mapping, endpoint credentials, provider acceptance, certification/conformance tests and live PostgreSQL execution still require the selected target infrastructure.

The Pass 8–18 HA/DR, replay, idempotency, authoritative PostgreSQL publication barrier, CAD, Station, Mayday and RMS work remains preserved.
