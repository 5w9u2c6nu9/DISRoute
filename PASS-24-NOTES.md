# DISRoute Pass 24 — Integrations / Gateways

Pass 24 deepens the existing provider abstraction into a durable external-integration operations layer while preserving the Dispatch-only 911/radio/recording topology and fail-cleanly boundary for optional providers.

## Implemented

- Added normalized durable gateway-event records for 911, radio/status and AIS/marine ingestion with provider-native IDs, payload hashes, source timestamps and correlation metadata.
- 911 gateway ingestion is idempotent and updates one canonical call lifecycle instead of creating duplicate call records for replayed provider events.
- Radio emergency gateway events deduplicate and raise a single critical operational alert; radio provider side effects use the durable provider-operation path.
- AIS gateway events deduplicate and update the retained vessel observation/state model used by operational GIS/replay.
- Added durable outbound provider-operation and immutable attempt history with Pending/Submitting/Confirmed/Failed/NeedsReconciliation states, provider references, errors and explicit possible-duplicate handling.
- Restored/failover state that was Submitting is converted to NeedsReconciliation/UnknownAfterRestore; DISRoute never blindly resends an ambiguous external side effect.
- Provider success without a durable provider reference is not treated as safely complete.
- The Submitting transition is durably checkpointed/projected before the external provider invocation. PostgreSQL mode performs that boundary under the existing authoritative command gate.
- Added provider-backed routing, recording/Eventide-style lookup, ONVIF discovery/import and radio emergency-clear operations. Simulation providers exercise the same contracts used by future live vendor adapters.
- Added durable recording references linked to incidents/provider references with duplicate suppression.
- Added Operations Center Integrations / Gateways controls and visibility for provider health, recent normalized events, provider operations/attempts, recordings, 911/radio/AIS simulation, routing, ONVIF discovery/import and retry/reconciliation workflows.
- Added typed client API coverage for the integration dashboard and gateway/provider operations.
- Portable state schema advanced to 20 and PostgreSQL migration `020_integration_gateways.sql` adds normalized integration event, provider operation, attempt and recording-reference tables. State persistence actively projects/upserts the new records.

## Regression coverage

SelfTest proves:

- exact duplicate 911 events do not duplicate call intake;
- one provider call ID advances through a normalized 911 lifecycle;
- duplicate radio emergency events create one critical alert;
- AIS events deduplicate;
- recording references deduplicate by provider identity;
- confirmed provider operations retain a durable provider reference and attempt history;
- an operation restored while Submitting becomes NeedsReconciliation/PossibleDuplicate rather than automatically retrying;
- integration events, recording references and provider-operation state persist across export/import.

Runtime smoke additionally exercises real HTTP paths for duplicate-safe 911 and AIS ingest, provider routing, recording lookup, ONVIF discovery/import and a radio provider side effect, then verifies the resulting normalized events/operations/recording/camera/vessel state after an actual server restart and again after staged backup restore.

## Verification result

- Full solution Release build: PASS — 18 projects, 0 warnings, 0 errors.
- SelfTest: PASS — `incidents=7, units=6, audit=145, alerts=3`.
- Source verifier: PASS — 118 hand-written C# files, 18 projects, 311 API routes, 20 migrations.
- Runtime persistence/restart/backup/restore/replay plus Pass 24 gateway/provider regression: PASS.
- Avalonia startup smoke: PASS — CAD, Station, Operations Center, Station Display and Mobile (Mobile independently rerun when the combined sandbox wrapper reached its external timeout).

## Deliberate non-claims / remaining production work

- Simulation/generic provider contracts are not a claim of certification against a specific PSAP/911 vendor, radio console/status vendor, Eventide deployment, ONVIF camera fleet, routing/traffic vendor, Esri/FileGDB environment or AIS feed.
- Vendor credentials, certificates, protocol conformance, rate limits, payload mappings, retry policy and acceptance tests still require the selected live agency/vendor systems.
- FileGDB/Esri production adapter depth and live traffic-provider qualification remain future integration work even though GIS/provider abstractions are present.
- Live PostgreSQL HA/DR failover/failback qualification remains a later production pass.
