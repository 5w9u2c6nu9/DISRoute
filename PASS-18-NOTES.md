# DISRoute — Pass 18 Notes

**Date:** 2026-08-20  
**Base checkpoint:** Pass 17  
**Scope:** RMS incident-report foundation and CAD-to-records handoff; no scope reduction and no HA/DR redesign.

## Implemented

- Canonical typed RMS incident report created from an authoritative CAD incident.
- CAD handoff includes incident identity/version, station, nature/priority, address/cross streets/municipality, caller fields, received/closed timestamps, participating-unit snapshots from retained unit history, incident activity, command assignments, mutual aid and narrative.
- Explicit field provenance: `CAD`, `RecordsUser`, and `RmsAmendment`.
- Draft CAD refresh updates CAD-owned facts but preserves explicit Records-user overrides.
- Draft -> InReview -> Finalized lifecycle with optimistic report versions and authenticated server actor attribution.
- Finalized records reject destructive direct editing; post-final changes require a reasoned amendment and at least one actual content change.
- Immutable report-version snapshots.
- Attachment/document references with classification/storage reference/description; arbitrary attachment bytes are not embedded in the RMS state record.
- Canonical readiness validation separated from provider-specific NERIS validation/submission.
- Typed RMS API/client routes and SignalR report/attachment broadcasts.
- Operations Center RMS workflow for CAD handoff, detail/provenance/validation/version/attachment review, CAD refresh, Draft editing, review return/finalize, finalized amendment and attachment references.
- Existing prevention, generic RMS, NERIS, hazmat, investigation, maintenance and personnel records modules preserved.
- Legacy generic RMS linkage now rejects incident/agency mismatch.
- Portable checkpoint state, after-action/records exports and PostgreSQL projections include canonical RMS report/version/attachment state.
- PostgreSQL migration 014 adds normalized RMS report, immutable version and attachment-reference tables. Schema version is now 14.
- Runtime smoke now creates/finalizes RMS through HTTP, verifies it after process restart, and verifies it again after staged backup restore.

## Verification

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=7, units=6, audit=90, alerts=2**.
- Source verifier: **PASS — 101 handwritten C# files, 17 projects, 245 API routes, 14 migrations**.
- Runtime persistence/idempotency/backup/staged-restore/historical-replay/RMS smoke: **PASS**.
- Avalonia startup smoke: **PASS** for CAD, Station, Operations Center and Station Display.

## RMS regression coverage added

- CAD-derived provenance capture.
- Participating unit, command and mutual-aid snapshots.
- Open incident is not finalization-ready.
- Records-owned address override survives later CAD address change while untouched CAD-owned nature refreshes.
- Closed timestamp and retained unit participation survive CAD closure/unit release.
- Review return, resubmit and finalization.
- Direct edit after finalization rejected.
- Reason-only/no-op amendment rejected.
- Reasoned amendment remains finalized and adds amendment provenance.
- Attachment reference and immutable version history persistence.
- One canonical report per agency/incident/report type.
- RMS report/version/attachment export-import round trip.

## Qualification boundary

This pass does **not** claim production NERIS certification or live PostgreSQL qualification. Canonical DISRoute RMS readiness is deliberately separate from provider-specific NERIS validation and transport. Production NERIS schema/profile mapping, provider acceptance, credentials, retry/queue certification and live PostgreSQL schema/migration execution still require target infrastructure and are future work.

The Pass 8-17 HA/DR, replay, idempotency, authoritative PostgreSQL publication barrier, CAD, Station and Mayday work remains preserved.
