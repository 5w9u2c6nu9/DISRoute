# Testing and Quality Plan

## Test layers

### Unit
Domain rules, response planning, state transitions, permission evaluation, effective-time assignments, deduplication, validation, routing constraints, workflow rules.

### Integration
PostgreSQL persistence, transactional outbox, SignalR/realtime, provider adapters, file/object storage, identity providers, migration staging.

### Contract/API
Versioned DTOs/events/provider contracts, backwards compatibility rules, authorization and invalid input.

### UI
Windows desktop: keyboard workflows, docking/layout restore, theme resources, multi-monitor adaptation, collaboration conflicts, critical alert visibility, accessibility, DPI/session behavior.

Station Display: Raspberry Pi/Linux ARM64 kiosk startup, layout scaling, incident takeover, reconnect/cache behavior, degraded-state visibility, watchdog/update behavior, and read-mostly permission enforcement.

### Security
Tenant isolation, role/scope boundaries, temporary assignment expiry, break-glass, device revocation, secrets exposure, SSRF/video discovery, file upload, signature authorization, module-disable enforcement.

### Migration
Schema discovery, dry run, idempotent rerun, bad/partial records, reconciliation, provenance.

### Failure/chaos

- primary app node loss;
- Dispatch primary -> local standby failover;
- entire Dispatch site loss -> HQ DR;
- database failover mid-dispatch;
- 9-1-1/radio/recording provider outage;
- printer/station outage;
- AVL/AIS/traffic storms;
- client reconnect storm;
- clock drift;
- WebSocket/realtime failure with API still healthy;
- offline MDT conflicting status;
- two dispatchers editing/assigning simultaneously;
- server restart with active incidents;
- stale replica ineligible for promotion.

## Platform build/release matrix

As the release projects land, CI must validate at minimum:

- **Windows x64:** full solution build/test; CAD, Supervisor/Station/Admin desktop targets and Windows-specific tests.
- **Linux x64:** server/backend build/test/publish and portable shared libraries.
- **Linux ARM64:** Station Display build/publish and targeted integration/UI smoke tests on real or representative ARM64 hardware.

A shared/server package becoming Windows-only unexpectedly is a release regression. A Windows desktop change that breaks Station Display ARM64 publishing is also a release regression. OS-specific adapters are exempt only when the platform restriction is explicit and isolated.

Server release qualification must cover both Windows Server and Linux deployment packages before claiming both as supported for that release.

## Performance/load

Test normal day, major incident, regional surge, integration storm, 200+ client reconnect, large GIS/AVL/AIS load, heavy reporting while CAD remains within safety-critical SLOs.

## Release gate

- all affected projects build warning-free;
- dependency vulnerability scan clean/accepted with remediation plan, never hidden;
- tests pass;
- no secrets;
- no legacy branding in repository;
- authorization tests for changed sensitive flows;
- migrations reversible/forward-safe where feasible;
- rollback/upgrade plan documented;
- CURRENT_STATE updated;
- required platform matrix targets for the changed components pass.
