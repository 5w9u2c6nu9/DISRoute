# DISRoute — Pass 16 Notes

**Date:** 2026-08-20  
**Base:** Pass 15 (`9f6b083874b4623939af3ecb375d1362066b20f1d410b9bc23a8333f9d131539`)  
**Theme:** Windows Station dispatch-delivery failure recovery and run-sheet workflow depth

## Implemented

- Expanded the Windows Station `Current Calls` workspace from passive status lists into an operational delivery/run-sheet workflow.
- Added a station-scoped delivery-path projection so a station workstation can inspect provider-level Station Display, Station Alert and Run Sheet delivery state without retrieving unrelated agencies/stations' dispatch-delivery records.
- Delivery-path rows now expose provider, destination, state, attempts, last error and update time.
- Added safe station-side retry of failed delivery paths.
- Hardened `RetryDelivery`: only `Failed` deliveries can be returned to `Pending`; confirmed/pending deliveries are rejected so an operator cannot accidentally duplicate a station alert/display/print delivery.
- Added realtime `dispatchDeliveryChanged`, `stationDeliveryChanged` and `runSheetChanged` broadcasts for retry, acknowledgement and run-sheet state changes.
- Station acknowledgement now uses the authenticated server actor at the endpoint boundary and repeated acknowledgement is idempotent at the store layer.
- Added typed client methods for station acknowledgement, station-scoped delivery paths, failed-delivery retry, provider-backed run-sheet printing and manual print confirmation.
- Added a typed `StationPrintResultDto` rather than leaking the integrations assembly's provider result contract into desktop clients.
- Added a real `Print Selected` action that invokes the configured print provider with a printer key and surfaces provider state/reference/error separately from the manual `Mark Printed` fallback.
- Manual run-sheet print marking is now idempotent and does not create repeated state transitions for an already printed sheet.
- Selected run-sheet content is rendered directly in the Station workspace for operator review.
- The Station 10-second refresh now preserves selected incident/delivery/run-sheet/path where possible instead of repeatedly throwing the operator back to an unselected list.
- Incident detail now shows address, status/priority, assigned station units and notes in the run-sheet workflow pane.

## Regression coverage

`DISRoute.SelfTest` now additionally proves:

- station-scoped provider-path projection returns the expected three station delivery paths;
- a confirmed provider delivery cannot be retried;
- a provider failure and error text are visible through the station-scoped path projection;
- a failed delivery can be retried back to `Pending` and subsequently confirmed; and
- repeated manual run-sheet print marking preserves the original printed timestamp.

## Verification completed

- Full 17-project .NET 10 Release build: **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=5, units=6, audit=81, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 233 API routes, 13 migrations**.
- File-checkpoint runtime persistence/restart/idempotency/backup/restore/replay smoke: **PASS**.
- Linux/Xvfb startup smoke: **PASS for CAD, Station, Operations Center and Station Display**.
- Live PostgreSQL execution remains **NOT RUN** because this artifact environment has no PostgreSQL runtime.

## HA/DR preservation

No HA/DR, fencing, durable-idempotency, PostgreSQL authoritative publication-barrier, primary-only side-effect, historical-replay, CAD site-intelligence or incident-management behavior was removed or weakened. Dispatch Primary -> Dispatch Local Standby -> HQ DR remains unchanged.

## Still intentionally not claimed complete

Station printing is provider-neutral and exercised through build/startup/store regression coverage, but production printer-driver/spooler qualification remains vendor/environment dependent. Supervisor workflow depth, dockable/floating multi-monitor workspace behavior, vendor-specific GIS/traffic/FileGDB/9-1-1/radio/recording integrations, identity-provider qualification and production PostgreSQL HA/DR validation remain incomplete.
