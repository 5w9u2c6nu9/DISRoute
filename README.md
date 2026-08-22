# DISRoute

DISRoute is a Windows-first public-safety operations platform under active development. Full operational desktop clients are primarily Windows; the backend is intended to support Windows Server or Linux; Raspberry Pi/Linux ARM64 is reserved for lightweight Station Display/wallboard roles.

This repository is the active DISRoute implementation. It compiles as an 18-project .NET 10 solution and includes runnable CAD, Station, Operations Center, Station Display, Mobile field-harness and Server applications. The current cumulative baseline is Pass 29: deployment/HA foundations, end-to-end operational workflows, and reusable load/resilience/security failure-stress qualification are implemented and regression-tested. Target hardware, live vendor conformance and final release-candidate acceptance remain. The authoritative product and architecture specification lives in `docs/spec/`.

## Current projects

- `DISRoute.Domain`
- `DISRoute.Application`
- `DISRoute.Contracts`
- `DISRoute.Data`
- `DISRoute.Security`
- `DISRoute.Server`
- `DISRoute.Client.Common`
- `DISRoute.CAD`
- `DISRoute.Station` — full Windows Station workstation client.
- `DISRoute.Station.Display` — dedicated Raspberry Pi/Linux ARM64 read-mostly Station Display/wallboard client.
- `DISRoute.Mobile.Core`
- `DISRoute.Mobile` — touch-first field companion/shared mobile UI with a desktop-hosted Avalonia validation harness; native Android/iOS packaging is still pending.
- `DISRoute.OperationsCenter`
- `DISRoute.LegacyBridge`
- `DISRoute.SelfTest`
- `DISRoute.GIS`
- `DISRoute.Video`
- `DISRoute.Integrations`

## First developer action

Read `AGENTS.md` and `docs/spec/CURRENT_STATE.md`, then restore/build the full `DISRoute.slnx` before implementing new features.

## Architecture direction

Core operational state is server authoritative. PostgreSQL is the production database target. Clients communicate through DISRoute APIs/realtime services. External systems are isolated behind provider interfaces. Safety-significant actions are audited/idempotent and do not use silent last-write-wins.

## Platform baseline

Read `docs/spec/PLATFORM_TARGETS.md` before changing runtime targets or adding OS-specific dependencies. Windows is the reference platform for full operational workstations. Raspberry Pi/Linux ARM64 is a wall-display target only. The core server must remain deployable on Windows Server or Linux.
