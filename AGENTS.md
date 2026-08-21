# AGENTS.md — Codex Rules for DISRoute

## Mission

Build DISRoute as a production-quality public-safety operations platform with a **Windows-first operational desktop**, a **Windows-or-Linux server/backend**, and a **Raspberry Pi/Linux ARM64 wall-display target**. The implementation must preserve the architectural boundaries and operational rules in this planning pack rather than optimizing for a quick demo.

## Mandatory rules

- Read the planning pack before changing architecture.
- Platform scope is explicit: full interactive operational clients are Windows-first; Raspberry Pi/Linux ARM64 is for Station Display/wallboard roles only; the backend must support Windows Server and Linux. Read `PLATFORM_TARGETS.md` before making platform or packaging decisions.
- Do not broaden Raspberry Pi support into dispatcher, supervisor, full Station workstation, Admin, RMS, Workforce, or Documents clients without a new approved decision.
- Isolate OS-specific dependencies behind explicit interfaces/adapters. Domain, application, contracts, provider abstractions, and portable server code must not accumulate unnecessary Windows-only dependencies.
- The repository must remain DISRoute-only. Do not import or preserve legacy product/vendor branding. Migration code and documentation use neutral terms such as `LegacySource`, `LegacyBridge`, and `LegacyImport`.
- Core CAD state is server authoritative. Desktop, Station, Mobile, and integrations do not directly write the production database.
- PostgreSQL is the production database target. SQLite may remain only for local development/demo until the PostgreSQL phase lands.
- Use UTC internally. Preserve source, occurred, received, and committed timestamps where relevant.
- Every safety-significant command must be idempotent and auditable.
- Never use silent last-write-wins for safety-critical concurrent changes.
- Realtime presence/typing is ephemeral. Committed operational changes are durable events.
- Do not introduce whole-record edit locks for incidents.
- Optional providers must support `NotConfigured`, `Available`, and `Unavailable` capability states. `NotConfigured` removes the feature from the user workflow instead of showing broken placeholders.
- Never block core CAD because an optional provider is slow or unavailable.
- No SMS MFA.
- Never suppress dependency vulnerability warnings to make a build green. Fix the dependency or isolate/remove it.
- Treat compiler/analyzer warnings as errors unless a reviewed repository-level exception documents why it is safe.
- Secrets and provider credentials remain server-side and must never be exposed to clients or embedded in URLs.
- Color is never the sole carrier of operational meaning.
- Drag-and-drop is never the only way to perform an operational action; keyboard/button alternatives are mandatory.
- Safety-critical actions must be deliberate but low-friction.
- Do not add autonomous safety-critical behavior merely because data can be inferred. Recommendations are preferred unless policy explicitly enables automation.
- Do not make destructive corrections to operational history. Append corrections with provenance.
- Signed/finalized DMS/form versions are immutable; edits create a new version/amendment.
- Never make a firehouse/station user an agency-wide administrator solely because of physical assignment.
- Module toggles are per agency and must disable UI, jobs, alerts, and provider behavior—not just navigation.

## Project structure direction

Use clear bounded projects/namespaces such as:

- `DISRoute.Domain`
- `DISRoute.Application`
- `DISRoute.Contracts`
- `DISRoute.Data`
- `DISRoute.Security`
- `DISRoute.Server`
- `DISRoute.Client.Common`
- `DISRoute.CAD`
- `DISRoute.Station`
- `DISRoute.Station.Display` when the dedicated Raspberry Pi/Linux ARM64 wall-display profile is split from the current Station scaffold
- `DISRoute.Mobile` when created
- `DISRoute.GIS`
- `DISRoute.Video`
- `DISRoute.Integrations`
- feature/provider projects under neutral, explicit namespaces.

Keep domain logic independent from Avalonia, HTTP, databases, and vendor SDKs.

## Change discipline

For each implementation slice:

1. State the requirement(s) being implemented.
2. Identify affected bounded contexts and provider contracts.
3. Add/modify tests first where practical.
4. Implement the smallest coherent vertical slice.
5. Build all affected projects.
6. Run unit/integration/security checks.
7. Search the repository for forbidden legacy branding and stale namespace/project references.
8. Update `CURRENT_STATE.md` and decision docs if architecture changes.
9. Do not silently change settled product behavior. Record proposed deviations and why.

## Definition of done

A feature is not done until:

- server-side authorization exists where needed;
- concurrency behavior is defined;
- audit/event behavior is defined;
- offline/degraded behavior is defined where relevant;
- provider failures are isolated;
- UI works in Classic, Light, and Dark token systems;
- keyboard and accessibility behavior exists;
- multi-monitor/docking behavior is considered for Windows desktop features;
- the applicable platform build target passes (`win-x64`, server `linux-x64`, and Station Display `linux-arm64` as those release projects land);
- tests cover normal, failure, duplicate/retry, and permission paths;
- no warnings or known vulnerable dependencies are introduced;
- docs are updated.
