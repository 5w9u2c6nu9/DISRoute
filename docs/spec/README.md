# DISRoute Master Planning Pack

This pack is the authoritative implementation plan for DISRoute. It consolidates the product, architecture, workflows, security, user experience, deployment, integrations, resilience, testing, and implementation sequencing decisions made before the Codex build phase.

## Product family

- DISRoute CAD — dispatcher/call-taker desktop client.
- DISRoute Station — full Windows firehouse workstation capability.
- DISRoute Station Display — lightweight Raspberry Pi/Linux ARM64 wallboard/information-screen deployment profile.
- DISRoute Mobile — responder phone/tablet/MDT client.
- DISRoute Maps — operational GIS, routing, traffic, marine, CRG, and situational layers.
- DISRoute RMS — records, prevention, investigations, permits, and related back-office capabilities.
- DISRoute Workforce — public-safety scheduling, staffing, time management, and employee self-service.
- DISRoute Documents — DMS, policies, forms, approvals, signatures, acknowledgements, and records workflows.
- DISRoute Radio — vendor-neutral radio integration and listen-only streaming services.
- DISRoute Connect — integration/provider framework.
- DISRoute Admin — Operations Center, configuration, health, security, modules, and diagnostics.

## Design goals

1. Safety-critical CAD operations are authoritative, transactional, auditable, fast, and resilient.
2. Multiple dispatchers can work the same incident at the same time without record locks.
3. The UI is dense, predictable, keyboard-efficient, low-stress, and extremely multi-monitor friendly.
4. Optional integrations fail independently; CAD remains usable when providers are unavailable.
5. Every major subsystem is provider-neutral and capability-driven.
6. Historical Replay can reconstruct what DISRoute knew at a specific moment.
7. Security is server-enforced, least-privilege, MFA-capable, and auditable.
8. Agencies may enable only the modules they need.
9. Full operational workstations are Windows-first; Raspberry Pi is reserved for wall displays; the server can run on Windows Server or Linux.
10. The codebase and all new artifacts use DISRoute-only branding. Migration components use neutral names such as LegacySource, LegacyBridge, and LegacyImport.

## Reading order for Codex

1. `AGENTS.md`
2. `DECISIONS.md`
3. `ARCHITECTURE.md`
4. `PRODUCT_SPEC.md`
5. `DATA_MODEL.md`
6. `WORKFLOWS_CAD.md`
7. `REALTIME_COLLABORATION.md`
8. `UI_UX.md`
9. `SECURITY.md`
10. `PLATFORM_TARGETS.md`
11. `ROADMAP.md`
12. Feature-specific documents as the current phase requires.

`OPEN_DECISIONS.md` lists choices that must remain abstracted until a concrete provider/library is selected.
