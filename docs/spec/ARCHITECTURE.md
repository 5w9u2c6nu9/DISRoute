# DISRoute Architecture

## Architectural style

DISRoute is an authoritative server platform with multiple realtime clients and provider-driven integrations. Clients never become independent competing CAD authorities during normal operation.

```text
Windows CAD / Supervisor / Station Workstation / Admin
        Pi Station Display / Mobile / Integrations
                    |
              DISRoute API
                    |
        Application / Domain layer
          |        |         |
     PostgreSQL  Event/Outbox  Provider framework
```

## Bounded responsibilities

### Domain
Pure operational concepts: incidents, calls, resources, assignments, statuses, personnel, premises, preplans, CRGs, road restrictions, messages, documents/forms, workforce assignments, alerts, audit facts, and provider-neutral observations.

### Application
Commands, validation, authorization decisions, workflows, conflict detection, transaction boundaries, projections, provider orchestration, and domain-event publication.

### Server/API
Authentication, transport, command endpoints, realtime hubs, health endpoints, configuration distribution, client sessions, rate limiting, and provider hosting/coordination. The core backend is a supported deployment target on both Windows Server and Linux. OS-specific service hosting and vendor SDK constraints remain at deployment/provider boundaries rather than leaking into the domain model.

### Data
PostgreSQL persistence, transactional outbox, projections/read models, spatial/PostGIS access where chosen, event/history storage, reporting/read separation, migration staging.

### Clients
The full interactive operational desktop is Windows-first: CAD, Supervisor, full Station workstation, Admin/Operations Center, and desktop RMS/Workforce/Documents workflows are designed and certified primarily for Windows.

Raspberry Pi/Linux ARM64 is a separate lightweight Station Display/wallboard target only. It reuses shared contracts/UI components where useful but has a read-mostly kiosk deployment profile, local operational display cache, remote health/update management, and no assumption of full workstation capability.

Mobile remains a future phone/tablet client. All clients share contracts and connection/reconciliation logic and do not directly mutate the production database.

### Integrations
Provider adapters translate external systems into canonical DISRoute commands/events/observations and isolate vendor SDKs from core domain code. A vendor SDK may require Windows or another specific host; that requirement applies to the adapter/gateway, not automatically to the DISRoute core server or all clients.

## Platform boundaries

`PLATFORM_TARGETS.md` is authoritative for supported roles. In particular:

- Windows is the reference platform for full interactive operational workstations.
- Raspberry Pi/Linux ARM64 is the first-class embedded target for Station Display/wallboard only.
- DISRoute Server is supported on Windows Server and Linux.
- Portable shared/server projects must not depend on Windows desktop APIs.
- Platform-specific capabilities belong behind interfaces and in role-specific projects.
- CI must validate Windows desktop, Linux server, and Linux ARM64 Station Display targets as those release projects mature.

## Authoritative state vs observations

DISRoute distinguishes authoritative operational state from external observations.

Examples:

- `E1 CAD status = En Route` is authoritative operational state.
- `AVL reports E1 moving` is an observation.
- `Radio gateway reports emergency ID` is a radio-system observation/state.
- `AIS reports vessel position` is an observation.
- `Station client received incident` is a delivery fact.

Observations can generate recommendations/alerts; they do not silently rewrite safety-critical state unless policy explicitly enables that automation.

## Commands and events

Safety-significant commands use unique operation IDs and optimistic concurrency/version checks. Examples:

- CreateIncident
- ChangeIncidentLocation
- AssignUnit
- ReassignUnit
- SetUnitStatus
- CommitDispatch
- CreateRoadClosure
- StartFireWatch
- CorrectOperationalTimestamp
- SubmitForm
- ApproveForm

Successful commands produce durable events/facts such as:

- IncidentCreated
- IncidentLocationChanged
- UnitAssigned
- UnitReassigned
- UnitStatusChanged
- IncidentDispatched
- StationDeliveryChanged
- RoadClosureCreated
- FireWatchStarted
- OperationalFactCorrected
- FormSubmitted
- FormApproved

## Transactional outbox

Database state and integration events are committed atomically through an outbox pattern. External side effects occur after the authoritative transaction and track their own delivery state/idempotency key.

A core dispatch transaction therefore does not wait synchronously for every station, printer, mobile device, or provider.

## Realtime

Use SignalR/WebSockets or an equivalent durable realtime abstraction for:

- committed incident/resource changes;
- client/session health;
- dispatcher presence and field presence;
- collaborative narrative operations;
- map objects;
- alerts;
- messages;
- integration delivery state;
- configuration/permission scope changes.

Scale-out must not make application-node affinity a correctness requirement.

## Provider framework

Every optional integration implements a stable capability-driven contract. Common provider state:

- `NotConfigured`: feature is absent from normal UI and layout reflows.
- `Available`: provider capabilities are usable.
- `Unavailable`: provider is configured but failed/degraded; show targeted failure state.

Provider contracts include capability discovery, source provenance, health, time/freshness, retry/circuit-breaker policy, and diagnostics.

## Multi-agency

Tenant/agency scope exists in the schema and authorization model from day one. Agency-specific modules, terminology, statuses, response plans, themes, integrations, retention, and permissions must not leak across tenants.

## Module framework

Core services are always present. Optional modules register capabilities, navigation, workflows, jobs, permissions, data schemas, and providers. Per-agency module state is evaluated server-side and reflected in clients.

## Historical model / Replay

The current operational projection is not the only source of truth for history. Replay must be able to reconstruct time-specific state from durable events, versioned reference data/configuration, observations, and corrections.

Historical reconstruction must not substitute today's unit name, staffing, response plan, preplan, CRG revision, road closure state, or radio mapping for the value that existed at the replay time.

## Attachments/object storage

Attachments are referenced by stable IDs, content hashes, classification, retention policy, owner/related record, version, and storage metadata. Binary storage can be filesystem/object-storage backed behind an abstraction. Secrets never appear in attachment URLs.

## Reporting isolation

Heavy analytics, exports, indexing, GIS import, DMS full-text extraction, and other background work use separate queues/resources/read models so they cannot starve core CAD transaction capacity.
