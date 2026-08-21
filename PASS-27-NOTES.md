# DISRoute Pass 27 — Production Deployment, Security & Operations Hardening

**Checkpoint date:** 2026-08-21

Pass 27 is cumulative from the authoritative Pass 26 source. It adds production deployment and operational hardening without reducing any prior CAD, Station, Operations Center, Station Display, Mobile, HA/DR, RMS/NERIS, Workforce, DMS, Chat or integration functionality. PostgreSQL remains the production persistence target and schema version remains 20.

## Production startup and secret safety

- Production requires PostgreSQL, authentication, HTTPS and MFA and retains the Pass 26 physical-failover independent-witness/external-database-fencing contract.
- External JSON configuration can be supplied through `DISROUTE_CONFIG_FILE` so deployment-specific settings remain outside the application directory.
- Sensitive settings support `secret://env/VARIABLE` and `secret://file/path`. Relative file references are constrained beneath `Security:Secrets:Directory`; Unix secret files fail closed if group/other permissions are present.
- PostgreSQL and HA witness connection strings, integration bearer tokens, LDAP bind passwords, TLS PFX passwords and MFA/offline-lease/session key material use the secret resolver.
- Production rejects configured plaintext sensitive values where a secret reference is required and rejects insecure HTTP provider endpoints unless explicitly allowed.

## TLS, proxy and request hardening

- Direct Kestrel TLS supports a configured PFX, secret-reference password, minimum remaining certificate lifetime and Unix private-file permission validation.
- Trusted reverse-proxy mode accepts forwarded headers only from explicitly configured proxy addresses/networks.
- Production emits HSTS for direct HTTPS plus CSP, frame, referrer, permissions and content-type security headers; the Kestrel `Server` header is suppressed.
- Fixed-window request limiting is configurable and keyed by authenticated identity or source IP.
- Anonymous readiness is intentionally sanitized to safe boolean checks only; deployment/upgrade operational details remain protected API surfaces.
- Unexpected API exceptions return generic production-safe errors rather than raw exception messages.

## Deployment and service management

- Linux server deployment includes a hardened `systemd` unit and installer using an unprivileged `disroute` account and restrictive service sandboxing.
- Windows Server includes native Service Control Manager hosting (`DISRoute.Server --windows-service`) plus install/update/uninstall PowerShell automation, service recovery settings, virtual-service-account use and ProgramData separation for mutable configuration/state.
- Windows update automation preserves the previous binary directory and automatically restores it when deployment/start fails.
- Windows SCM code is source/build qualified here; actual SCM installation/start/stop remains target-Windows acceptance because this qualification environment is Linux.

## Operational readiness and upgrade safety

Authenticated administrators can inspect:

- `GET /api/admin/deployment/status`
- `GET /api/admin/upgrade/preflight`

The readiness layer evaluates PostgreSQL/migration integrity, durable journal state, HA authority, free disk headroom, backup recency and ambiguous external provider operations without returning secrets. Upgrade preflight blocks while the node is not authoritative, the journal is not drained, migration integrity is invalid, or an external provider operation remains durably `Submitting`.

Published migration history remains immutable and schema version remains 20. Rolling upgrade/failback rules are documented separately for shared-primary and physical-PostgreSQL HA deployments.

## Cross-platform publish/package pipeline

Qualified publish targets:

- Server — Linux x64
- Server — Windows x64
- CAD — Windows x64
- Station — Windows x64
- Operations Center — Windows x64
- Mobile desktop field harness — Windows x64
- Station Display — Linux ARM64
- LegacyBridge — Windows x64

`scripts/package-deployment.py` produces a per-file SHA-256 manifest, embeds that manifest in the deployment ZIP, and integrity-tests the resulting archive. The final qualification bundle contained 314 payload files; every archived payload was independently re-hashed against the embedded manifest.

Evidence: `docs/ops/evidence/PASS-27-PUBLISH-EVIDENCE.json`.

## Live production-security qualification

The destructive Pass 27 production deployment harness ran successfully against a real disposable PostgreSQL 18.6-compatible database and proved:

- fail-closed Production startup with authentication or MFA disabled;
- rejection of plaintext sensitive provider configuration;
- rejection of reverse-proxy mode without explicit trust configuration;
- rejection of direct TLS without a certificate;
- external JSON configuration loading;
- PostgreSQL connection through `secret://file`;
- trusted `X-Forwarded-Proto` HTTPS semantics;
- sanitized anonymous readiness and protected administrative deployment status;
- direct Kestrel TLS plus HSTS;
- rejection of an overly permissive TLS PFX on Unix;
- security-header and Kestrel-header behavior;
- fixed-window request limiting;
- `systemd-analyze verify` on the hardened Linux unit.

Evidence: `docs/ops/evidence/PASS-27-PRODUCTION-DEPLOYMENT-EVIDENCE.json`.

## Cumulative PostgreSQL regression

Pass 27 reran and passed the mandatory Pass 25 destructive PostgreSQL HA regression. The run re-proved exact migration ledger identity, exactly-one-writer behavior, crash-after-commit recovery, stale-standby reload, provider ambiguity reconciliation, drain/resume, old-primary fencing, pre-ledger backfill and migration-tamper rejection. Measured application crash failover in this run was 525 ms; this is a lab measurement, not a production SLA.

Pass 27 also reran and passed the complete Pass 26 physical PostgreSQL HA/DR/PITR harness. It re-proved witness/database partitions failing closed, physical promotion, unsafe-old-primary application fencing, both rewind directions, planned physical failback, restored streaming replication and PITR include/exclude boundaries. This run measured 1.523 s physical failover and 2.299 s failback; those are qualification-environment measurements only.

## Explicit boundaries

Pass 27 does **not** claim:

- live Windows Server SCM installation/service-cycle certification;
- live Raspberry Pi hardware acceptance;
- native Android/iOS Mobile packaging or MDM deployment;
- signed Windows installer/package certificates;
- certification of a particular production PostgreSQL STONITH/quorum manager;
- live 911/radio/Eventide/NERIS/ONVIF/traffic/AIS vendor conformance;
- production load, penetration, accessibility or agency operational acceptance.

Those remain target-environment/release-candidate qualification work rather than being inferred from sandbox tests.

## Final Pass 27 verification

The exact final working tree passed:

- full 18-project offline restore;
- full Release build: **0 warnings / 0 errors**;
- SelfTest: **PASS — incidents=7, units=6, audit=145, alerts=3**;
- source verifier: **124 C# files / 18 projects / 313 API routes / 20 migrations**;
- process-level restart / backup / staged-restore regression: **PASS**;
- Production qualification crash-injection rejection: **PASS**;
- CAD startup smoke: **PASS**;
- Station startup smoke: **PASS**;
- Operations Center startup smoke: **PASS**;
- Station Display startup smoke: **PASS**;
- Mobile startup smoke: **PASS**;
- Pass 27 Production deployment/security qualification: **PASS**;
- Pass 25 PostgreSQL HA regression: **PASS**;
- Pass 26 physical PostgreSQL HA/DR/PITR regression: **PASS**;
- cross-platform publish of all eight documented targets: **PASS**;
- deployment ZIP + embedded SHA-256 manifest independent verification: **PASS**.

The runtime regression was updated to consume Pass 27's deliberately sanitized anonymous readiness contract instead of relying on removed internal journal fields.
