# DISRoute — Pass 17 Notes

**Date:** 2026-08-20  
**Base:** Pass 16.1 (`274a7f0656561721e6ec565f04a44cc03539cf760aa8ffaac862284d0ffafee6`)  
**Theme:** Supervisor oversight and Mayday/radio-emergency operations

## Implemented

- Expanded Mayday from a two-state declare/resolve record into an explicit emergency lifecycle: `Active -> Acknowledged -> Assigned -> Escalated -> Resolved`.
- Mayday declarations now fail closed if the supplied agency does not match the incident agency; supplied units must exist and belong to the incident agency.
- Mayday declarations still create a correlated `Critical` operational alert, with correlation key `mayday:{id}`.
- Added authenticated server-side Mayday actions for acknowledge, assignment/ownership, escalation and resolution; client-supplied actor identity is not trusted at the HTTP boundary.
- Added acknowledgement identity/time, assigned owner, escalation level, notes and update time to the persisted Mayday state.
- Acknowledge synchronizes the correlated alert to acknowledged state; assignment claims the correlated alert to the authenticated supervisor; resolution resolves the correlated critical alert.
- Mayday escalation preserves an existing claimed alert owner rather than discarding ownership while raising the emergency's escalation level.
- Added typed client API methods for retrieving Maydays and performing the four supervisor actions.
- Expanded Operations Center's operational view with a dedicated Mayday / Radio Emergency column showing explicit textual state, person, location, channel, owner and escalation level (not color-only signaling).
- Added Operations Center controls for Acknowledge, Assign to Supervisor, Escalate and Resolve.
- Realtime Mayday changes are broadcast to both agency and incident SignalR groups for all lifecycle actions.

## Regression coverage

`DISRoute.SelfTest` now proves:

- Mayday declaration creates a Critical correlated alert;
- acknowledgement identity/time is retained;
- supervisor ownership assignment is retained;
- escalation increments the emergency escalation level;
- linked critical-alert ownership survives Mayday escalation;
- an active escalated Mayday survives feature-state export/import (the restart/failover persistence boundary);
- a restored Mayday can be resolved after import; and
- Mayday resolution also resolves the correlated critical alert.

## Verification completed

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=6, units=6, audit=86, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 233 API routes, 13 migrations**.
- File-checkpoint runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.
- Linux/Xvfb startup smoke: **PASS for CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL HA execution remains **NOT RUN** because this artifact environment has no PostgreSQL topology.

## HA/DR preservation

No HA/DR, fencing, lease epoch, durable-idempotency, PostgreSQL authoritative publication barrier, primary-only side-effect, historical replay, Station workflow, CAD workflow, or site-intelligence behavior was removed or weakened. Dispatch Primary -> Dispatch Local Standby -> HQ DR remains unchanged.

The active Mayday persistence regression specifically proves the emergency object survives state export/import before resolution, but this is not a substitute for a production multi-node PostgreSQL network-partition/failover drill.

## Still intentionally not claimed complete

- Vendor/radio-specific emergency-button ingestion and console/radio protocol adapters remain provider-dependent.
- Personnel/accountability roster integration is not yet a full firefighter-accountability subsystem; Mayday person/location/channel/owner data are operational fields rather than a complete PAR/tag tracking product.
- Production multi-node PostgreSQL HA/DR, radio-console integration, real paging/alerting equipment, load/failure-mode qualification and public-safety acceptance testing remain required before certification.
