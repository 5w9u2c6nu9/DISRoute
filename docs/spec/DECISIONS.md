# DISRoute Decision Log

## Brand and codebase

- Product name: **DISRoute**.
- Product family names use DISRoute consistently.
- New code, namespaces, assemblies, comments, docs, logs, tests, sample data, fixtures, package names, migration tools, and planning files use DISRoute-only branding.
- Migration/recovery components use neutral terms such as `LegacySource`, `LegacyBridge`, `LegacyImport`.

## Technology

- .NET 10 target for the current implementation line.
- Avalonia for the Windows-first desktop UI and reusable UI components; cross-platform capability is preserved where useful but Linux desktop parity is not a primary requirement.
- Full interactive operational clients are Windows-first: CAD, Supervisor, full Station workstation, Admin/Operations Center, and desktop RMS/Workforce/Documents workflows.
- Raspberry Pi/Linux ARM64 is a first-class target **only for Station Display/wallboard/information-screen roles**.
- DISRoute Server/backend is supported on either Windows Server or Linux.
- Platform-specific vendor dependencies are isolated behind provider/gateway boundaries rather than forcing the whole platform onto one OS.
- ASP.NET Core authoritative backend.
- PostgreSQL production target; SQLite development/demo only.
- SignalR/WebSocket-style realtime transport.
- Transactional outbox/domain-event approach.
- Provider/plugin boundaries for external systems.

## User experience

- Dense, utility-first CAD presentation.
- Built-in Classic, Light, and Dark skins.
- Extensible versioned theme package framework.
- Strong multi-monitor docking/floating support.
- Centrally stored roaming workspace layouts.
- Realtime multi-dispatcher collaboration is foundational.
- Low-stress operation is a top-level UX requirement.
- Soft presence indicators; no normal whole-record locks.

## Operational status semantics

Classic theme visual language uses the supplied operational status reference:

- On Air — green family.
- At Quarters — light/white family.
- Responding — red family.
- At Scene — light/white surface with red status text.
- Off Duty — yellow family.
- Relocation — light blue/cyan family.
- Out of Service — dark/black surface with yellow text.
- Code 55 — magenta/pink family; semantic meaning is **Temporarily Off Duty**.

Exact RGB values are theme implementation details; do not claim calibrated historic values from photographed references. Status text/icons remain visible independently of color.

## Apparatus naming

- Normal operational unit and apparatus display names match, e.g. `E1` unit using `E1` apparatus.
- The data model still separates operational unit identity from physical apparatus identity for maintenance, VIN, AVL hardware, history, and reserve swaps.
- A reserve swap keeps the operational unit identity while showing the temporary physical apparatus assignment.

## Security

- Native users + AD/LDAP + OIDC/Entra identity sources.
- Identity source is separate from DISRoute roles/permissions.
- MFA: TOTP, passkeys/WebAuthn/FIDO2/security keys, recovery codes. No SMS.
- Local break-glass administrator exists and is heavily protected/audited.
- Server-side authorization for every sensitive operation.
- Device identities for Station, Mobile/MDT, services, and gateways.

## GIS / CRG / preplans

- Preplans are global/shared by premise, not per station.
- CRG overlays are TIFF-first; preplan documents are PDF-first.
- CRG metadata may be incomplete; usable files must not be rejected solely for missing metadata.
- Multi-floor CRG selector and bulk upload with per-file floor assignment are required.
- Original source remains authoritative; optimized derivatives may be generated.
- File Geodatabase, GeoJSON, KML/KMZ, WMS/WMTS and future feed import/provider paths.
- Traffic is a first-class live map layer and routing input.
- Dispatcher-created road closures/restrictions propagate globally and immediately affect routing.
- Marine operations and AIS are first-class, provider-neutral capabilities.

## Station / Mobile

- Full Station workstations are Windows-first interactive clients.
- Raspberry Pi/Linux ARM64 is used for read-mostly Station Display/wallboard roles only; it is not the full Station workstation target.
- Station is a first-class role/device-restricted capability across those two deployment profiles.
- Station alerting, station display delivery, acknowledgements, and run-sheet printing are separate tracked facts.
- Mobile radio is listen-only; no PTT controls.
- Icecast is the standard listen-only streaming backend design.
- Mobile is touch-first with large one-tap status controls and rugged/tablet-friendly layouts.

## Integrations

- Radio integration is vendor-neutral and supports multiple systems/providers simultaneously.
- 9-1-1 integration is provider-neutral and supports legacy call data plus NG9-1-1 concepts such as structured location and EIDO exchange.
- Recording integration remains a reference/association layer; the recorder remains system of record.
- Station alerting may use Zetron, native DISRoute alerting, or configured combinations.
- Camera integration supports ONVIF discovery + direct IP/hostname + manual stream entry.

## Deployment

- The core server/backend may run on Windows Server or Linux; agencies are not forced to one server OS.
- Production HA deployments use a validated supported server/DB stack; dual-OS product support does not require mixed-OS nodes inside one HA cluster.
- Preferred production failover order: **Dispatch Primary → Dispatch Local Standby → HQ DR**.
- Dispatch-only integrations: 9-1-1 gateway, radio gateway, and recording-system integration.
- HQ is geographic DR for core DISRoute, not the normal first failover target.
- Firehouses normally run Station/Mobile clients and continuity caches, not full core servers.
- Exactly one authoritative writable core/database at a time; split-brain is unacceptable.

## Workforce / DMS

- Workforce is public-safety scheduling/time-management, not full payroll/benefits HRIS.
- DMS supports policies, records, versioning, acknowledgements, approval, and signatures.
- Generic structured forms have independent approval/signature workflows separate from publication approval.
- Payroll/benefits/tax calculation remains external; DISRoute supports validated exports/integrations.

## Modules

- Optional modules can be enabled/disabled per agency.
- Disabling preserves data/config unless an explicit archival/deletion workflow is invoked.
- Core identity/security/audit/CAD integrity services cannot be disabled.
