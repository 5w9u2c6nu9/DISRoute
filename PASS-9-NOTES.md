# DISRoute Pass 9 — Historical incident replay

Pass 9 extends the preserved Pass 8 HA/DR checkpoint with incident-centric historical replay and keeps the full build/runtime baseline green.

## Major changes

- Added an incident Replay service that reconstructs the operational view as of a requested UTC timestamp instead of substituting current projections silently.
- Added immutable incident/unit/dispatch/delivery/station-delivery/alert state histories and retained AIS observation history used by replay.
- Added replay classifications for `KNOWN`, `OBSERVED`, and `CORRECTED LATER` facts plus explicit limitations when a domain lacks sufficient retained history.
- Added `/api/replay/incidents/{id}/snapshot?atUtc=...` and retained the existing replay timeline API.
- Added a CAD Incident Replay window with timestamp selection, historical incident/unit/dispatch/observation summaries, timeline, corrections and replay limitations.
- Added PostgreSQL migration 011 for normalized historical state/observation tables.
- Extended runtime smoke coverage to prove that a pre-close replay snapshot remains `Dispatched` after the incident is later closed, survives process restart, and remains correct after backup/restore rollback.

## Verified in this environment

- Full 17-project .NET 10 Release build: 0 warnings, 0 errors.
- DISRoute.SelfTest: PASS.
- Source verifier: PASS (154 C# files, 17 projects, 230 API routes, 11 migrations).
- Runtime persistence/backup/restore/replay smoke: PASS.
