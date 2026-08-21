# DISRoute — Pass 12 Notes

**Date:** 2026-08-20  
**Base:** Pass 11 (`efa0a026204a7e866a73c12041b1fa95a67a5d9609aa09ae0b51d75229da3c53`)  
**Theme:** Windows CAD incident-workspace depth and collaborative-edit safety

## Problems closed in this pass

The server already exposed several operational workflows that the Windows CAD client did not surface. The shared incident narrative also used whole-document replacement: if another dispatcher changed the document first, the client handled the 409 by reloading the server copy over the operator's unsaved text. That defeated the server's existing non-overlapping patch/rebase semantics and created an avoidable operator data-loss path.

## Implemented

- Reworked CAD shared-narrative save to calculate and submit a minimal contiguous patch against the loaded base version.
- Non-overlapping concurrent edits can now use the server's operational-transform/rebase path and return the merged document.
- Overlapping conflicts preserve the operator's unsaved local text and clearly report that nothing was overwritten.
- Added an explicit `Reload Latest (discard local)` action so discarding local narrative work requires deliberate operator action.
- Added reusable/tested `NarrativePatchBuilder` in `DISRoute.Client.Common`.
- Expanded the Windows CAD call-intake strip with callback-number capture and duplicate-candidate checking.
- Manual CAD incident creation now also writes a linked manual call-intake record; if that secondary record fails, the UI explicitly reports that the incident was still created.
- Added multi-unit resource-board selection, assignment and status updates.
- Surfaced response-plan recommendations and warnings in the dispatcher workspace.
- Surfaced incident dispatch history, delivery health summary, additional-alarm commit and dispatch cancellation.
- Surfaced Fire Watch start/check/end controls.
- Surfaced relocation start/end controls with destination-station selection and required reason.
- Replaced string-only alert display with actionable alert rows supporting acknowledge, claim, escalate and resolve.
- Extended `FeatureApiClient` with typed calls for the above existing server endpoints rather than introducing parallel APIs.

## Regression coverage

- SelfTest now validates narrative-patch generation/round-trip for insert, delete, replace, empty and unchanged documents.
- SelfTest now covers relocation create/complete and dispatcher alert acknowledge/claim/resolve behavior in addition to the pre-existing response recommendation, additional alarm and Fire Watch coverage.
- Existing durable idempotency, PostgreSQL publication barrier, HA/DR and historical replay tests remain intact.

## Verification completed

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=3, units=6, audit=73, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 230 API routes, 13 migrations**.
- File-checkpoint runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.
- Linux/Xvfb startup smoke: **PASS for CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL execution remains **NOT RUN** in this artifact environment because no PostgreSQL runtime is installed.

## HA/DR preservation

No Pass 8/10/11 HA, fencing, durable-idempotency or PostgreSQL publication-barrier behavior was removed or weakened. Dispatch Primary -> Dispatch Local Standby -> HQ DR remains the promotion priority, and all server-authoritative command/side-effect rules are unchanged by this client-focused pass.

## Still intentionally not claimed complete

The Windows CAD client is materially deeper, but full incident-workspace coverage still includes additional supervisor/command/group/mutual-aid, GIS/preplan, correction, messaging, workspace/multi-monitor and role-specific acceptance work. Target-specific integrations and live PostgreSQL HA/DR qualification remain separate production-certification work.
