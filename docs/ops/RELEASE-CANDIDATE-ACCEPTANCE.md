# DISRoute 1.0.0-rc.1 Release-Candidate Acceptance

This matrix separates **implementation qualification** from **agency/production acceptance**. An item marked External Required is deliberately not represented as certified by the repository.

| Area | RC status | Evidence / boundary |
|---|---|---|
| 18-project Release build / warnings-as-errors | Qualified | Pass 30 final build + SelfTest |
| Source structure / migration immutability | Qualified | `scripts/verify-source.py`, 20 migrations |
| Locked dependency graph | Qualified | 18 `packages.lock.json` files; locked restore |
| Dependency inventory / SBOM | Qualified | CycloneDX 1.6 generated from all lock files |
| Reproducible publish payload | Qualified in implementation environment | two independent publish trees must hash identically |
| Deterministic deployment packaging | Qualified | fixed `SOURCE_DATE_EPOCH`; duplicate packages byte-identical |
| Operational E2E | Qualified | Pass 28 6/6 scenarios |
| Application HA / physical PostgreSQL HA / PITR | Qualified in local destructive topology | Passes 25–29 |
| Load/idempotency/crash/failure stress | Qualified in implementation environment | Pass 29 stress and load-failover harnesses |
| Production configuration/TLS/secrets/rate-limit safety | Qualified | Pass 27/29 production-security harness |
| Windows Service source/package path | Build/package qualified | **External Required:** live Windows Server SCM lifecycle |
| Raspberry Pi Station Display | Build/package qualified | **External Required:** real ARM64 Pi hardware/endurance |
| Signed Windows installers/releases | Not certified | **External Required:** organization code-signing identity/certificate and installer acceptance |
| Native Android/iOS Mobile | Not a v1 RC deliverable | Current Mobile project is desktop-hosted field validation harness |
| PostgreSQL quorum/STONITH manager | Application contract qualified | **External Required:** select/certify production HA manager and fencing implementation |
| Dispatch ↔ HQ geographic RPO/RTO | Not certified | **External Required:** production network/sites and failure drills |
| 911/radio/Eventide/NERIS/GIS/ONVIF/AIS vendor conformance | Provider contracts/harnesses implemented | **External Required:** real vendor credentials/protocols and acceptance |
| Independent penetration test | Not performed | **External Required** before production approval |
| Accessibility / keyboard / multi-monitor operator acceptance | Not fully certified | **External Required** on target Windows workstations |
| Agency UAT/training/go-live sign-off | Not performed | **External Required** |

**RC meaning:** `1.0.0-rc.1` is a complete implementation candidate suitable for controlled deployment acceptance. It is not a claim that external hardware, vendor, security-assessment, signing, or agency-go-live gates have been completed.
