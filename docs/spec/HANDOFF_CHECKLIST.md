# Codex Handoff Checklist

## First run

- [ ] Read `AGENTS.md`, `DECISIONS.md`, and `PLATFORM_TARGETS.md`.
- [ ] Confirm .NET 10 SDK/workloads required by Avalonia are installed.
- [ ] Restore dependencies.
- [ ] Build the full `DISRoute.slnx` with warnings as errors.
- [ ] Run any current tests.
- [ ] Launch Server, CAD and Station development clients.
- [ ] Fix build/runtime issues before architectural expansion.
- [ ] Run repository search confirming DISRoute-only branding.

## Baseline quality

- [ ] Add CI build/test/analyzer/dependency scan.
- [ ] Add platform matrix jobs for Windows x64 desktop/server, Linux x64 server, and Linux ARM64 Station Display publish as applicable.
- [ ] Add formatting/editorconfig where needed.
- [ ] Add architecture tests for project dependency direction.
- [ ] Add a branding check in CI.
- [ ] Add secrets scanning.

## Before each phase

- [ ] Read relevant spec docs.
- [ ] Confirm unresolved provider/library choices remain abstracted.
- [ ] Define vertical slice acceptance criteria.
- [ ] Define authorization, concurrency, audit, offline/degraded behavior.
- [ ] Add failure/idempotency tests.

## Production gate

- [ ] PostgreSQL production persistence.
- [ ] Server-side authorization and tenant isolation.
- [ ] MFA/device identity.
- [ ] event/outbox/idempotency.
- [ ] realtime scale/reconnect tests.
- [ ] provider circuit breakers/timeouts.
- [ ] HA/DR failover and restore drills.
- [ ] performance SLO load tests.
- [ ] Windows accessibility/keyboard/multi-monitor/DPI tests.
- [ ] Raspberry Pi/Linux ARM64 Station Display kiosk/reconnect/cache/update tests.
- [ ] Windows Server and Linux server deployment qualification.
- [ ] signed/reproducible release artifacts.
- [ ] security/dependency scan clean.
- [ ] migration rehearsal/reconciliation if historical data is imported.
