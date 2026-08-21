# DISRoute Pass 8 — HA/DR recovery and hardening

Pass 8 is the authoritative checkpoint that reconstructs and advances the HA/DR work that had not been preserved as a numbered source archive.

## Major changes

- Real .NET 10 baseline retained: all 17 projects build with 0 warnings / 0 errors.
- Fixed PostgreSQL advisory-lock reentrancy in HA leadership.
- Added durable monotonic lease epochs and leadership transition events.
- Added site/configured-role/node topology, stale heartbeat detection and live replication status.
- Added planned drain/resume controls for local switchover.
- Added optional external witness requirement for automatic geographic application promotion.
- Added primary-only checkpointing, command journal/outbox writing, station-delivery work and alert escalation.
- Added Operations Center live HA node/replica/event visibility.
- Added migration 010 for HA topology/fencing metadata.
- Added HA deployment examples, runbook and two-node smoke harness.
- Removed obsolete demo client/contracts and the demo MFA exemption.
- Fixed runtime-smoke launcher so it executes the real compiled Server DLL reliably.

## Verified in this environment

- Full solution Release build: 0 warnings, 0 errors.
- DISRoute.SelfTest: PASS.
- Source verifier: PASS.
- FileCheckpoint runtime persistence/backup/restore smoke: PASS.

The HA two-node smoke harness requires a disposable PostgreSQL server and is included for target-environment execution.
