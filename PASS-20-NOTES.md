# DISRoute — Pass 20 Notes

**Date:** 2026-08-20  
**Base checkpoint:** Pass 19  
**Base SHA-256:** `0a808cb8ce9bfcd59158c069fa054003f5befe129d94b5e86185191aebf5b331`  
**Scope:** Workforce scheduling / daily roster operations, staffing rules, effective assignments, schedule-change history, roster publication and provider-backed print. No HA/DR redesign and no reduction of prior CAD/Station/RMS/NERIS functionality.

## Implemented

- Deepened the existing Workforce module instead of introducing a parallel scheduling model.
- Added persisted qualification-aware staffing rules scoped by agency, optional station, shift and position, with minimum counts and enable/disable state.
- Added effective workforce assignments with station, optional apparatus/unit, position, employee identity, qualification, assignment type, state, effective time, optional end time, source and authenticated audit actor.
- Assignment states support operational roster needs including `Assigned`, `Acting`, `Detail`, `Overtime`, `Training`, `Open`, `CallOut` and `Relieved`.
- Open vacancies are first-class assignments but **never count as filled staffing** and DISRoute does not autonomously fill them.
- Added call-out workflow that atomically ends the filled assignment and creates an explicit open vacancy.
- Added individual atomic relief workflow so accountability does not show an artificial gap between outgoing and replacement personnel.
- Added qualification-aware minimum staffing evaluation with per-rule filled/vacancy counts and deficiency warnings. Existing legacy staffing API remains compatible and now uses the richer evaluator.
- Added immutable posted roster snapshots with revision numbers, posting actor/time, notes, staffing evaluation and SHA-256 content identity. Later current-schedule changes do not mutate earlier posted revisions.
- Added structured same-day schedule-change events with actor/time and assignment linkage.
- Added current-roster projection separating effective current assignments from the most recent posted roster.
- Added provider-neutral roster print view with `Current Schedule` and optional `Same-Day Changes`, point-in-time timestamp and content SHA-256.
- Added provider-backed roster printing through the existing `IPrintProvider` abstraction and printer-key selection.
- Deepened Windows Operations Center Workforce UI with:
  - current effective roster and staffing status;
  - assignment/open-shift creation;
  - call-out and atomic relief controls;
  - staffing-rule creation;
  - posted roster revision history;
  - same-day change history;
  - print preview and provider-backed printing;
  - existing leave/trade/rotation/overtime/timesheet lists retained under Requests / Time.
- Strengthened existing Workforce integrity checks:
  - overtime station must match agency;
  - timesheet station/incident must match agency;
  - legacy shift trade offered shift must match agency;
  - administrative leave/trade/rotation/timesheet approval actor attribution is taken from authenticated server context rather than trusted client text.
- Added portable-state/checkpoint persistence for staffing rules, assignments, schedule changes and roster snapshots.
- Added PostgreSQL migration `016_workforce_roster_depth.sql`; state schema is now 16 with normalized projection tables for staffing rules, assignments, schedule changes and immutable roster snapshots.
- Existing leave requests, trades, rotations, overtime opportunities, timesheets, payroll export and legacy roster endpoints remain preserved.

## Verification

- Full 17-project .NET 10 Release restore/build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=7, units=6, audit=101, alerts=2**.
- Source verifier: **106 C# files, 17 projects, 269 API routes, 16 migrations**.
- Runtime persistence/idempotency/backup/staged-restore/historical-replay/RMS/NERIS/Workforce smoke: **PASS**.
- Runtime Workforce regression creates and posts roster revision 1, calls out the assigned driver, proves a real unfilled vacancy/deficiency without autonomous assignment, explicitly covers it with an overtime assignment, posts revision 2, verifies print-with-changes, then proves the current roster, immutable revisions/hashes and change history survive a real server restart and staged backup restore.
- Provider-backed Workforce roster print call through the Development simulation provider: **PASS**.
- Avalonia startup smoke: **PASS** for CAD, Station, Operations Center and Station Display.

## SelfTest coverage added

- Qualification-aware Driver and Officer minimum staffing rules.
- Cross-agency staffing-rule rejection.
- Fully staffed evaluation and posted revision 1.
- Call-out produces a real deficiency and an explicit Open vacancy.
- Open vacancy never satisfies minimum staffing.
- No autonomous personnel assignment occurs.
- Atomic individual relief records outgoing `Relieved` state and replacement assignment.
- Explicit overtime coverage restores minimum staffing.
- Posted revision 1 remains immutable after later changes and revision 2 publication.
- Current roster vs most-recent posted roster distinction.
- Structured CallOut/Relief same-day history.
- Print-with-changes content and SHA-256 identity.
- Export/import preservation of rules, effective assignments, snapshots and structured changes.

## Qualification boundary / remaining Workforce work

Pass 20 implements the supervisor/admin daily staffing and roster operations slice. It does **not** claim the full Workforce specification is production-complete. Remaining work includes employee/mobile/web self-service authorization and UX, leave-balance/projection engines, recurring rotation expansion into a master schedule, whole-crew relief, duty-driven temporary security-scope grants/revocation, real-time employee notifications, configurable rest/seniority/collective-bargaining policy engines, production payroll-provider validation, and agency-branded PDF generation/Windows printer qualification. The current provider-neutral roster print content is text-based and validated through the simulation print provider.

Live PostgreSQL execution of schema 16 is not claimed in this artifact environment; the existing HA/DR and PostgreSQL qualification boundary remains unchanged.

The Pass 8–19 HA/DR, replay, idempotency, CAD, Station, Mayday, RMS and NERIS work remains preserved.
