# Deployment, High Availability, Continuity, and Disaster Recovery

## Preferred production topology

Failover order:

1. **Dispatch Primary**
2. **Dispatch Local Standby**
3. **HQ Geographic DR**

Dispatch hosts the primary core/database plus a local hot/warm standby. HQ hosts a secondary DR core/database replica.

The 9-1-1 gateway, radio gateway, and recording integration are Dispatch-site-only. They connect to a Dispatch-local logical endpoint that follows the active Dispatch core.

## Server operating-system support

The authoritative DISRoute backend is a supported deployment target on **Windows Server or Linux**. Core API, realtime, authorization, audit, provider orchestration, persistence semantics, health, and HA/DR behavior must remain equivalent.

OS-specific hosting belongs in deployment packaging (for example Windows Service versus `systemd`) or isolated provider gateways. A vendor integration that requires Windows does not make the whole DISRoute core Windows-only.

A production HA installation should use a validated homogeneous deployment profile unless a mixed-OS topology has been explicitly qualified. Product support for both Windows Server and Linux means an agency may choose either supported stack; it does not require mixing them inside one failover cluster.

Raspberry Pi is not a production DISRoute core/database server target. It is reserved for Station Display/wallboard use.

## Failure cases

### Dispatch primary server fails

Promote/fail locally to Dispatch Standby so local integrations continue normally. Clients reconnect automatically via logical endpoint/service discovery.

### Entire Dispatch site lost

HQ becomes authoritative for core DISRoute. CAD/GIS/Station/Mobile/Messaging/Documents/Workforce/core services continue, but Dispatch-only 9-1-1/radio/recording integrations are clearly degraded/unavailable until connectivity/site returns.

### HQ unavailable while Dispatch healthy

Production continues; Operations Center flags DR readiness/replication problem.

## Database

PostgreSQL primary at Dispatch with local Dispatch replica and HQ DR replica. Promotion eligibility depends on health and replication lag. Exactly one writable authoritative database at a time.

Use quorum/witness/fencing appropriate to the final HA stack. Split-brain is unacceptable; controlled degraded mode is preferable to divergent CAD truth.

## Starting recovery objectives

Deployment-dependent design targets:

- core CAD app RTO < 5 minutes;
- committed CAD RPO near zero where replication architecture supports it;
- database RTO < 15 minutes target for severe failover;
- database RPO < 1 minute target, preferably near zero;
- Station/Mobile operational cache available immediately at last successful sync;
- documents/analytics may have lower recovery priority than CAD.

## Client degraded mode

CAD clients retain current active incidents, assignments, recent narrative, unit state, premise/preplan/CRG, channels, alerts and relevant map cache. Offline/degraded state is unmistakable. Only explicitly allowed operations queue locally.

Offline incident creation may use temporary globally unique identifiers and reconcile to authoritative visible numbers after recovery while preserving the temporary ID.

## Station continuity

Once a station receives a call, cache call/address/map/preplan/CRG/channel/run-sheet package locally. Server/network loss must not blank the current call.

## Mobile continuity

Encrypted current-incident/map/preplan/CRG cache plus queued permitted status/message operations. Preserve occurred time and operation ID. Authorization leases are time limited.

## Restart/idempotency

Restart/failover must never re-tone stations, reprint completed run sheets, duplicate assignments/messages, replay radio commands, resubmit reports or recreate 9-1-1 call records. Durable operation/delivery IDs drive reconciliation.

## Backups

Separate policies for:

- PostgreSQL + point-in-time recovery;
- attachments/documents;
- configuration/version snapshots;
- GIS sources/derivatives;
- audit/event history;
- protected secret/key recovery.

## Restore testing

Operations Center tracks last successful backup and last verified restore for database, config and document storage. Backup success without restore testing is insufficient.

## Safe maintenance

Nodes drain before update/restart when redundancy is available. Surface active incidents, critical alerts and connected-client impact before disruptive maintenance. Support staged update rings: Development -> Training -> Pilot -> Production.

## Alternate dispatch location

Roaming identity/workspace allows authorized dispatchers to relocate to HQ/alternate consoles and receive active operational state with layout adaptation.
