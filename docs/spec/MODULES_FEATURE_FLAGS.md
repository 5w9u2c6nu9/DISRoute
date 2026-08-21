# Per-Agency Module Management and Feature Flags

## Module manager

Authorized administrators can enable/disable optional modules per agency/tenant.

Examples of optional modules:

- GIS advanced layers
- Station
- Mobile
- Messaging
- Documents/DMS
- Workforce
- Prevention/Inspections
- Permits
- Investigations
- Marine/AIS
- Video/Cameras
- Advanced Reporting
- NERIS adapter
- other provider-specific capabilities

Core identity/security, audit/event history, core CAD incident/resource integrity and essential system health cannot be disabled.

## Disable behavior

Disabling a module must remove/stop for that agency:

- navigation/UI;
- background jobs;
- scheduled tasks;
- module alerts;
- provider activity;
- module-specific commands/APIs except controlled administration/export;
- permissions/capabilities surfaced to clients.

Data/config is preserved by default so re-enabling restores state. Deletion/archival is a separate explicit workflow.

## Dependency validation

Before disabling, evaluate dependent modules/workflows/configuration and show consequences. Do not leave broken empty panels or providers running behind a hidden menu.

## Feature flags

Use staged internal feature flags for development/pilot rollout separately from agency module enablement. Feature flags are not a substitute for authorization.
