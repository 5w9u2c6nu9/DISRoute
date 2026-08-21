# Supervisor Operations and DISRoute Admin / Operations Center

## Supervisor operations

Supervisor workspace is a role-specific view over the same live platform, not a disconnected app.

### Overview

Dense view of active incidents, priority events, unit/resource availability, coverage gaps, marine availability, fire watches, relocations, OOS resources, active alerts and degraded systems.

### Soft incident ownership

Show primary dispatcher and other collaborators. Reassignment of primary responsibility is auditable but never creates a record lock.

### Overrides

Authorized supervisor may override availability/resource restrictions, response recommendations, certain priority/closure/reopen constraints, etc. Require explicit confirmation/reason according to policy. Override never erases underlying state.

### Corrections

Supervisor correction appends original/corrected values, actor, reason and source evidence.

### Coverage

Capability-aware coverage/depletion by area/station/battalion and recommendations for Relocation. Recommendations consider resource type/capability/staffing and remain human-controlled by default.

### Major incident / emergencies

Command/division/group/staging tree, channels, mutual aid, additional alarms, Mayday/radio-emergency board, active handler/acknowledgement and latest location/crew context.

### Shift handoff

Structured handoff summary of active incidents, fire watches, relocations, OOS, unresolved alerts, mutual aid, reporting needs and bulletins. Record outgoing/incoming acceptance without rewriting ownership history.

### Operational bulletins

Time/geography/audience scoped notices such as water-supply issue, road closure, temporary channel, planned event, station/resource change. Relevant bulletins surface automatically at call/location.

## Operations Center

Central control plane for platform health/configuration.

Navigation areas:

- Operations
- Resources
- Stations
- GIS & Premises
- Communications
- 9-1-1
- Video
- Reporting
- Workforce
- Documents
- Users & Security
- Themes & Workspaces
- Integrations
- Data Management
- System
- Audit & Diagnostics
- Training

## Health landing page

Human-readable status for core API/realtime/database, dispatch/Station/Mobile clients, printers/cameras, gateways/providers, backups, storage, replication, certificates, clock sync, versions and items needing attention.

## Configuration change control

High-impact configuration uses Draft -> Validate -> Preview/Test -> Publish, versioned and auditable. This applies to response plans, station alerting, radio mappings, GIS boundaries/imports, permissions, forms/workflows, retention, module settings and critical provider configuration.

## Resource administration

Operational unit and physical apparatus records, capabilities, home/current assignments, maintenance and device links. Normal display naming follows agency naming.

## Response plan editor

Capability/staffing/geography/time/premise-aware run-card editor with simulation preview before publish.

## GIS administration

Staged import, field mapping, preview, publish, source provenance, layer health and update policies.

## User/security administration

Identity bindings, roles, scoped permissions, MFA status, device inventory, break-glass health, session revocation, temporary grants.

## Diagnostics/support bundle

Generate sanitized support bundles with versions, service health, selected logs, provider status, sanitized config, performance metrics and recent errors. Never include secrets by default.

## Training

Isolated training environment/database with obvious visual indicator. Real station alerts, radio controls, 9-1-1 writes, reporting submissions and production data access are blocked. Provide provider/event/failure simulators.

## Configuration inheritance

Configuration should support inheritance with explicit overrides:

`System defaults -> Agency -> Division/District -> Station -> Device`

Store only necessary overrides and provide effective-configuration preview so administrators can understand why a station/device has a particular value.

## Scheduled reports and storage governance

Support permissioned scheduled report delivery, storage quotas/retention/archive policies, legal holds and usage visibility. Reporting/storage work remains isolated from live CAD transaction resources.
