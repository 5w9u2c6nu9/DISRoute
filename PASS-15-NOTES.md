# DISRoute — Pass 15 Notes

**Date:** 2026-08-20  
**Base:** Pass 14 (`6f29c4afaadb1658fe456fc6cc555ed24ff87c7d9c8559c4ee2f15502e78ec77`)  
**Theme:** Windows CAD audited incident correction, relationships, merge and split workflows

## Implemented

- Added typed Windows-client access for incident corrections, relationships, merge and split.
- Added a CAD `INCIDENT MGMT` workspace with three focused operational surfaces:
  - `CORRECT`: audited correction of Nature, Priority, Address, CrossStreets, Municipality, CallerName, CallerPhone or Notes with a mandatory reason;
  - `RELATE / MERGE`: incident relationship creation and source-to-target merge;
  - `SPLIT`: creation of a new linked incident while preserving the original incident.
- Correction history is visible in CAD with original value, corrected value, reason, actor and timestamp.
- Relationship history identifies inbound vs outbound relationships and resolves the other incident to its incident number/nature/status when available.
- Merge targets are restricted in the UI to open incidents in the same agency.
- A merge requires the operator to type the source incident number exactly before execution, reducing accidental destructive merges.
- Merge continues to use optimistic source/target versions and durable payload-bound idempotency already present in the server workflow.
- Split continues to preserve the source incident and creates an audited `SplitFrom` relationship to the source.
- Server relationship creation now rejects an agency ID that does not match the source incident and rejects direct cross-agency relationships through this workflow.
- Server merge explicitly rejects cross-agency source/target incidents.
- Realtime broadcasts were completed for these workflows:
  - corrections now broadcast both the correction record and updated incident projection;
  - direct relationship creation broadcasts to the agency and both incident groups;
  - merge broadcasts the closed source, updated target, changed unit board and generated relationship;
  - split broadcasts the new incident and generated relationship.

## Regression coverage

`DISRoute.SelfTest` now additionally proves:

- relationship agency mismatch rejection;
- split relationship preservation;
- merge closes the source incident;
- merge transfers an assigned unit to the target incident;
- merge records the `MergedInto` relationship; and
- merge retry with the same durable idempotency key returns the original result without repeating the operation.

## Verification completed

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=5, units=6, audit=76, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 232 API routes, 13 migrations**.
- File-checkpoint runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.
- Linux/Xvfb startup smoke: **PASS for CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL execution remains **NOT RUN** because this artifact environment has no PostgreSQL runtime.

## HA/DR preservation

No HA/DR, fencing, durable-idempotency, PostgreSQL authoritative publication-barrier, primary-only side-effect, site-intelligence or historical-replay behavior was removed or weakened. Dispatch Primary -> Dispatch Local Standby -> HQ DR remains unchanged.

## Still intentionally not claimed complete

Supervisor workflow depth, dockable/floating multi-monitor workspace behavior, deeper Station/run-sheet operations, vendor-specific GIS/traffic/FileGDB/9-1-1/radio/recording integrations, identity-provider qualification and production PostgreSQL HA/DR validation remain incomplete.
