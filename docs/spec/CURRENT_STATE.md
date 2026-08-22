# Current DISRoute Implementation State — Pass 30 / 1.0.0-rc.1

## Authoritative checkpoint

The current implementation is the cumulative Pass 30 release-candidate source checkpoint (`1.0.0-rc.1`). It contains the full application through all previous passes. Pass 30 freezes the implementation with an exact SDK, dependency lock files, deterministic release packaging, CycloneDX SBOM generation, CI advisory gating and an explicit production-acceptance matrix rather than adding a new operational subsystem.

## Platform baseline

- Full interactive operational workstations remain Windows-first.
- `DISRoute.Server` remains a Windows Server/Linux target.
- `DISRoute.Station.Display` remains the Raspberry Pi/Linux ARM64 read-mostly wallboard target.
- `DISRoute.Mobile` is a touch-first field companion/shared UI foundation. The current repository includes a desktop-hosted Avalonia harness for build/startup validation; native Android/iOS packaging and real-device qualification remain pending.

## Current solution

18 .NET 10 projects, including Server, CAD, full Station, Station Display, Operations Center, Mobile, Mobile.Core, shared client/contracts/security/data/GIS/video/integrations, LegacyBridge and SelfTest.

## Implemented operational depth

- CAD intake/dispatch/status/narrative, command/groups, mutual aid, relationships/merge/split, audited corrections, GIS/site intelligence and replay.
- Station delivery/acknowledgement/run-sheet printing and dedicated Station Display.
- Supervisor alert and Mayday/radio-emergency lifecycle.
- Canonical CAD-to-RMS reporting with provenance/version/finalization/amendments and NERIS bridge/submission/reconciliation pipeline.
- Workforce staffing rules, qualification-aware roster, vacancies/call-outs/relief/overtime, immutable posted rosters and printing.
- DMS/forms/approval workflows with immutable revisions, required-read tracking, entity links, signatures and external asset references.
- Internal chat with Direct/Station/Group/Supervisor/Agency/MutualAid channels, realtime SignalR delivery, unread/read state, presence, mentions, search, links/attachment references and dynamic Station/operational-group access.
- Mobile field package with assignment scope, crew/Mayday/scene/preplan/CRG/DMS/chat/radio context, encrypted stale cache, explicit offline queue and signed-lease/stale-check reconciliation.
- Durable integration gateway/provider operations for normalized 911, radio/status and AIS ingestion; provider-backed routing, recording lookup, ONVIF discovery/import and radio side effects with retry/reconciliation and durable attempt history.
- File-checkpoint runtime persistence, command journal, backup/restore, durable idempotency and PostgreSQL-shaped migrations/projection through schema version 20.
- HA/DR coordination architecture preserving Dispatch Primary -> Dispatch Local Standby -> HQ DR.

## Current Pass 30 release-candidate baseline

- 18-project Release build with warnings-as-errors: PASS — 0 warnings / 0 errors.
- SelfTest including least-privilege role matrix: PASS.
- Source structure verifier: PASS — 124 C# / 18 projects / 313 API routes / 20 migrations.
- Six-scenario live operational end-to-end qualification: PASS.
- Active-dispatched-incident application authority-transfer continuity on PostgreSQL: PASS.
- Process restart / durable idempotency / backup / staged restore / historical replay / RMS / NERIS / Workforce / DMS / Chat / Mobile / integration-gateway runtime smoke: PASS.
- Pass 25 PostgreSQL application-HA regression and Pass 26 physical-HA/DR/PITR regression: PASS.
- Pass 27 Production deployment/security regression: PASS.
- Avalonia startup smoke: CAD, Station, Operations Center, Station Display and Mobile PASS.
- All eight documented cross-platform publish targets and deployment-manifest integrity verification: PASS.

## External production acceptance still outstanding

- penetration/security review beyond the implemented production-hardening regression;
- complete accessibility, keyboard, theme, docking/multi-monitor and operator acceptance on target Windows workstations;
- live Windows Server SCM lifecycle and signed production installer acceptance;
- live Raspberry Pi Station Display hardware acceptance;
- native Android/iOS Mobile packaging, MDM, secure-store, push, offline-map and field-device qualification;
- selection/certification of the production PostgreSQL quorum/STONITH/fencing manager and real Dispatch↔HQ infrastructure RPO/RTO;
- live vendor conformance for selected 911, radio/status, Eventide/recording, GIS/routing, NERIS, ONVIF/cameras and AIS/marine systems;
- agency operational acceptance, training, production runbooks and release-candidate sign-off.

Pass 28 whole-system qualification evidence remains in `docs/ops/OPERATIONAL-E2E-QUALIFICATION.md` and `docs/ops/evidence/PASS-28-*.json`. Pass 29 stress/failure evidence is documented in `docs/ops/PASS-29-STRESS-QUALIFICATION.md` and `docs/ops/evidence/PASS-29-*.json`. Pass 30 release acceptance and limitations are in `docs/ops/RELEASE-CANDIDATE-ACCEPTANCE.md`, `KNOWN-LIMITATIONS.md`, `RELEASE-CHECKLIST.md`, and the `1.0.0-rc.1` release notes.

See `IMPLEMENTATION-COVERAGE.md` and the numbered `PASS-*-NOTES.md` files for the cumulative implementation history.
