# DISRoute Implementation Roadmap

The roadmap is sequencing, not a reduction of the full product scope.

## Phase 0 — Baseline stabilization

- verify renamed DISRoute solution builds;
- remove stale prototype-only artifacts;
- establish CI, formatting, analyzers, dependency scanning;
- establish platform matrix scaffolding: Windows x64 desktop/server, Linux x64 server, Linux ARM64 Station Display publish;
- add repository-level architecture tests/branding check;
- keep SQLite demo temporarily only to validate current UI/API behavior.

## Phase 1 — Production domain/data foundation

- PostgreSQL production persistence;
- migrations/schema versioning;
- multi-agency scope;
- immutable IDs/external identifiers;
- incident/call/location/resource canonical models;
- event/outbox/idempotency/audit;
- UTC/time provenance;
- provider registry/capability state;
- module framework.

## Phase 2 — Identity/security

- native + AD/LDAP + OIDC identities;
- MFA/passkeys/TOTP;
- role/scope permission engine;
- device identities;
- temporary duty scopes;
- break-glass;
- session revocation/audit.

## Phase 3 — Realtime collaboration

- realtime hub/scale-out;
- presence/field presence;
- collaborative narrative foundation;
- optimistic concurrency/conflict UI;
- client heartbeat/reconnect/reconciliation.

## Phase 4 — Core CAD vertical slice

- Call Intake;
- duplicate call detection;
- Incident Workspace;
- Resource Board;
- response recommendations;
- dispatch commit;
- assignment/status workflows;
- fire watch;
- relocation;
- closure/correction/merge/split;
- alert framework.

## Phase 5 — Windows desktop experience

- dock/floating framework;
- multi-monitor behavior;
- roaming layouts;
- Classic/Light/Dark tokens;
- theme package contract;
- command palette/search foundation;
- accessibility/keyboard polish;
- Windows-specific DPI/session/authentication integration without leaking Windows dependencies into shared domain/server code.

## Phase 6 — GIS / routing

- operational map/provider abstraction;
- premise/hydrant/water network import;
- preplans/CRGs;
- shared map objects;
- dispatcher road closures;
- traffic provider/routing inputs;
- AVL;
- marine/AIS abstraction;
- Replay map foundation.

## Phase 7 — Station

- full Windows Station workstation profile;
- dedicated Raspberry Pi/Linux ARM64 Station Display/wallboard profile;
- station device identity/scope;
- wallboard/incidents;
- Pi kiosk autostart/watchdog/remote health/signed-update path;
- local continuity cache;
- durable delivery state;
- run-sheet print service;
- station radio profiles;
- preplan/CRG/camera panels.

## Phase 8 — Communications integrations

- conventional radio gateway first based on actual dispatch equipment;
- 9-1-1 adapter based on actual call-handling environment;
- recording correlation;
- Zetron FSA provider;
- listen-only Icecast service;
- messaging channels/incident chat.

## Phase 9 — Supervisor/Admin/Operations Center

- coverage view;
- corrections/overrides/handoff/bulletins;
- module manager;
- provider/config health;
- client/clock monitoring;
- updates/device inventory/backups/DR views;
- support diagnostics.

## Phase 10 — Workforce + Documents

- scheduling/rotations/staffing;
- leave/overtime/trades/timesheets;
- daily roster print/change history;
- DMS/versioning/search;
- publication/acknowledgement;
- structured forms/workflow/signatures.

## Phase 11 — RMS foundations

- fire reporting/NERIS;
- premises/occupancy;
- inspections/prevention;
- permits/hazmat/investigations;
- apparatus/equipment maintenance;
- records export.

## Phase 12 — Mobile

- rugged touch workflow;
- incident/status/map/preplan/CRG;
- offline queue/cache;
- messaging;
- listen-only radio;
- device security.

## Phase 13 — HA/DR and server-platform productionization

- qualify Windows Server and Linux backend deployment packages;
- Dispatch Primary + Dispatch Standby + HQ DR;
- PostgreSQL replication/failover/fencing;
- endpoint/service failover;
- DR drills;
- restore verification;
- performance/capacity certification.

## Later

- local AIS receiver;
- advanced MCI;
- deeper EMS extensions/ePCR integration;
- advanced CAD-to-CAD;
- richer analytics;
- broader provider ecosystem;
- future additional operational domains only after dedicated design/security review.
