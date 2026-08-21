# DISRoute — Pass 13 Notes

**Date:** 2026-08-20  
**Base:** Pass 12 (`de38d6e3cfcb6c9fe9da6ead7815fe14dd40fcc8bf17be5d666498b3271724f7`)  
**Theme:** Windows CAD command/supervisor incident operations and operational messaging

## Implemented

- Added typed client APIs for incident operational groups, command assignments, mutual aid and operational messaging.
- Added a CAD `COMMAND` workspace tab exposing:
  - command-role assignment, optionally tied to the selected unit;
  - group/division creation with type and leader;
  - mutual-aid requests with agency/resource/quantity;
  - mutual-aid state transitions including Accepted, EnRoute, Arrived, Declined, Canceled and Completed.
- Added a CAD `MESSAGES` workspace tab exposing:
  - incident-scoped operational-message history;
  - channel, optional recipient and priority selection;
  - operational-message send with acknowledgement receipt targeting when a recipient is specified;
  - promotion of a selected operational message into the immutable incident narrative.
- Incident refresh now loads and presents command assignments, groups/divisions, mutual aid and incident operational messages alongside the Pass 12 response/dispatch/Fire Watch/relocation surfaces.
- The implementation uses the existing server-authoritative workflows and does not introduce side-channel client state.

## Regression coverage

The existing SelfTest already exercises command assignment, group/division creation, mutual-aid state transitions, operational messaging, message receipts and narrative promotion. Pass 13 routes those already-tested server capabilities through the typed Windows client and compiles/startup-smokes the resulting UI.

## Verification completed

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=3, units=6, audit=73, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 230 API routes, 13 migrations**.
- File-checkpoint runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.
- Linux/Xvfb startup smoke: **PASS for CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL execution remains **NOT RUN** because this artifact environment has no PostgreSQL runtime.

## HA/DR preservation

No HA/DR, fencing, durable-idempotency, outbox publication-barrier, primary-only side-effect or historical-replay behavior was removed or weakened. Dispatch Primary -> Dispatch Local Standby -> HQ DR remains unchanged.

## Still intentionally not claimed complete

Further operator-surface depth remains for GIS/preplans/CRGs, incident correction/relationships/merge-split, richer supervisor workflows, workspace/multi-monitor behavior, production identity adapters and target-specific integrations. Production PostgreSQL HA/DR qualification still requires real infrastructure.
