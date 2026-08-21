# DISRoute Current Implementation State — 2026-08-21

This file records the implementation state of the source tree in this archive. It is intentionally conservative: source present in the repository is distinguished from behavior that has been compiled, integration-tested, or validated against real agency infrastructure.

## Implemented in source

- Windows-first CAD client using the operational API for manual call intake, duplicate checking, multi-unit assignment/status, response recommendations, patch/rebase collaborative narrative, Dispatch Commit/additional alarms/cancellation, Fire Watch, relocations, command/mutual-aid/messaging workflows, site intelligence with premise/preplan/CRG-floor/hydrant/map/road-restriction context, audited corrections, incident relationships/merge/split, actionable alerts, search, close/reopen, Mayday, replay and advanced CAD workflows.
- Full Windows Station client using operational station snapshots, delivery acknowledgements, run-sheet tracking, units, preplans/CRGs, cameras and radio streams.
- Raspberry Pi/Linux ARM64 Station Display with local continuity cache and degraded/cached mode.
- Operations Center client surfacing operations, HA/DR, client/provider health, Workforce, Documents/Forms, RMS, security and audit data.
- Server-authoritative incidents and units with version checks, durable payload-bound idempotency receipts, activity history, realtime SignalR groups, presence, workspace layouts and themes.
- Call records, duplicate-call candidate scoring, versioned response plans and recommendations, additional alarms, dispatch update/cancel semantics and AVL advisories.
- Incident relationships, merge/split preservation, operational groups/divisions, command assignment, mutual-aid state and Mayday handling.
- Dispatch Commit fan-out, per-destination delivery state, provider-aware delivery worker, station deliveries, run sheets and mobile delivery state.
- Fire Watch and relocation workflows.
- Premises, preplans, CRG assets, hydrants, map objects, road restrictions with explicit end lifecycle, incident site-intelligence aggregation/proximity semantics, fallback routing and vessel/AIS observations.
- Alerts, claim/acknowledge/resolve plus progressive unacknowledged escalation, supervisor bulletins, and persistent operational messaging with delivery/read/acknowledgement receipts and narrative promotion.
- Client heartbeat and provider health/configuration; cameras and listen-only radio stream registry.
- Workforce leave, trades, shifts/staffing, rotations, overtime, timesheets, payroll export and daily roster state.
- DMS document versions/acknowledgements, form templates/instances, signatures and approval-routing workflows.
- RMS inspections, permits, violations, hazmat, investigations, maintenance, personnel qualifications and NERIS report lifecycle state.
- Device/user security records, configuration draft/publish, tamper-evident audit chain, TOTP/recovery codes, signed MFA sessions, offline authorization leases, HA/DR status and mobile sync/offline-safe operation reconciliation.
- Historical Incident Replay reconstructs incident, unit, dispatch, delivery, station, alert, AVL/AIS and related operational state as-of a selected UTC timestamp; corrected-later facts and incomplete-history limitations are surfaced explicitly in the API and CAD Replay window.
- Provider-neutral interfaces for station alerting, print, routing, radio, 9-1-1, recording, AVL, AIS and fire reporting, with development simulation providers and configurable HTTP/file providers for station alerting, fire reporting and printing.
- PostgreSQL production schema and migrations through version 20, including the operational/back-office/mobile/integration domains, HA topology/fencing, authoritative mutation barriers, and a Pass 25 immutable migration-integrity ledger with SHA-256 identities for every published migration.
- Durable PostgreSQL mutation journal/outbox publication with per-command durability batches and an authoritative checkpoint/projection publication barrier, node heartbeat/advisory-lock write fencing and PostgreSQL LISTEN/NOTIFY event relay source paths.
- Atomic file-checkpoint development persistence and PostgreSQL production checkpoint/restore with payload integrity, automatic migrations and normalized live PostgreSQL projections for key operational/back-office domains.
- After-action/records export packages and redacted administrator support bundles.
- LegacyBridge migration executable plus Windows/Linux/ARM64 publish/deployment scripts.
- End-to-end self-test source exercising representative CAD -> dispatch -> station -> GIS -> messaging -> back-office -> persistence -> security/audit flows.

## Verification completed in the artifact environment

- Full 18-project `.NET 10` Release build succeeds with warnings treated as errors: **0 warnings, 0 errors**.
- `DISRoute.SelfTest` passes representative CAD, dispatch, GIS, messaging, Workforce, DMS, RMS, persistence, security/audit and collaborative-narrative paths.
- Runtime smoke proves create -> assign -> narrative -> close -> durable journal/checkpoint -> restart -> idempotent create/close retry without duplicate state or journal work -> backup verification -> staged restore -> restart rollback behavior.
- Source verifier passes project/XML/JSON structure, route collision checks, ProjectReference resolution, migration continuity, unfinished-marker checks and legacy-branding guard.
- PostgreSQL migrations are contiguous through schema version 20; source verification also requires every migration to advance `schema_info` to its own version.
- All five Avalonia applications compile; Linux/Xvfb startup smoke has been established for CAD, Station, Operations Center, Station Display and Mobile.
- Self-contained Windows x64, Linux x64 and Linux ARM64 publish paths have been produced; the Linux x64 published Server has been runtime-tested.

## Pass 8 HA/DR hardening

- Fixed advisory-lock reentrancy: a primary no longer re-acquires the same session lock every heartbeat.
- Added monotonic HA lease epochs and durable leadership transition events.
- Added Dispatch/HQ site identity, configured node role, heartbeat age/stale detection and live node topology status.
- Added planned local drain/resume controls for safe maintenance/switchover.
- Added PostgreSQL replication-lag inspection and promotion-eligibility reporting.
- Standby nodes no longer run station-delivery or alert-escalation side effects.
- Standby nodes no longer write stale runtime checkpoints or command-journal/outbox events.
- Geographic automatic application promotion is disabled by default and requires an external witness connection; HQ must also be connected to an already-writable PostgreSQL database.
- Added HA deployment examples, runbook and a disposable two-node local failover smoke harness.
- Removed obsolete demo DTO/client artifacts and demo MFA bypass.

## Pass 9 historical replay

- Added retained state histories for incident, unit, dispatch action/delivery, station delivery and alert transitions plus retained vessel observations.
- Added incident-centric historical snapshot reconstruction with explicit KNOWN / OBSERVED / CORRECTED LATER semantics and limitation reporting.
- Added a CAD Replay window and client API for selecting a historical UTC instant.
- Runtime smoke now verifies that a historical pre-close snapshot survives close, process restart and backup/restore rollback without being rewritten by current state.

## Pass 10 durable command idempotency

- Replaced process-local OperationalStore/FeatureStore idempotency dictionaries with one shared durable command-retry ledger.
- Bound every retained key to the authoritative operation name plus SHA-256 of the request scope/payload and the serialized result type; key reuse for a different command/payload now fails closed.
- Persisted idempotency receipts in schema-12 runtime state and synchronized them to PostgreSQL `idempotency_records` in the same checkpoint/projection transaction.
- Preserved idempotency for create/update/assign/status/close/reopen/narrative, merge/split, Dispatch Commit, additional alarm/cancel dispatch, AVL advisory resolution, correction, collaborative narrative edits and mobile offline operation IDs.
- Added SelfTest restart/import regressions for create, Dispatch Commit tuple-result replay, mobile duplicate suppression and changed-payload collision rejection.
- Extended runtime smoke to prove a real process restart does not duplicate an incident or mutation-journal work when the original create/close keys are retried.
- Source verifier now excludes generated `bin`/`obj` C# files from its source-file metric, so checkpoint source counts remain stable after a build.
- This closes restart/failover retry duplication in the current command model; it does **not** claim that every CAD command is already one fully normalized PostgreSQL relational transaction.

## Pass 11 PostgreSQL authoritative publication barrier

- Added a node-level `AuthoritativeCommandGate` so PostgreSQL API mutation execution cannot race a periodic checkpoint on the same authoritative node.
- Added per-command durability batch correlation to mutation envelopes, `command_journal` and `outbox_events`.
- PostgreSQL outbox rows are no longer eligible for publication merely because the journal writer inserted them; `PostgreSqlOutboxPublisherService` now requires `authoritative_committed_at_utc`.
- The PostgreSQL checkpoint/normalized-projection transaction marks the matching pending command batches authoritative only after the state snapshot/projections have been written in that same transaction.
- Pending failed batches remain non-publishable; later successful checkpoints can reconcile still-pending in-process batches, while process-loss leaves pre-checkpoint rows non-authoritative rather than emitting ghost external events.
- API error/exception paths restore the pre-command in-memory snapshot for PostgreSQL commands and abandon their durability batch instead of promoting a partially failed command.
- `/health/ready` now also fails readiness while an authoritative command batch remains pending.
- Primary-only dispatch-delivery completion and alert-escalation mutations now use the same PostgreSQL command/checkpoint gate and force a durability boundary after their state change.
- Migration `013_authoritative_outbox_barrier.sql` adds durability-batch and authoritative-commit metadata and backfills pre-barrier rows as already authoritative so upgrade does not strand prior events.
- SelfTest covers durability-batch correlation/completion/abandonment. Full Release build, existing durable-idempotency tests, backup/restore/replay runtime smoke and source verification remain green.
- A live PostgreSQL server is not available in this artifact environment, so the new SQL execution path is compiled and structurally verified but is **not** being represented as live-database-qualified yet.

## Pass 12 Windows CAD incident-workspace depth

- Shared narrative edits now submit minimal patches, allowing the server to rebase non-overlapping concurrent dispatcher edits.
- Overlapping narrative conflicts preserve unsaved local text; the operator must explicitly choose `Reload Latest (discard local)` to replace it.
- Manual intake now captures callback number, checks duplicate candidates and records a linked manual call record after incident creation.
- The CAD resource board supports multi-unit assignment/status changes and surfaces response-plan recommendations/warnings.
- Incident dispatch operations now show dispatch history/delivery health and expose additional-alarm and cancellation actions.
- Fire Watch, relocation and alert acknowledge/claim/escalate/resolve workflows are now directly available in the dispatcher workspace.
- Full Release build, SelfTest, source verifier, runtime persistence/restore/replay smoke and all-four-client Xvfb startup smoke remain green.

## Pass 13 Windows CAD command/supervisor operations and messaging

- The dispatcher workspace now exposes incident command-role assignment and group/division creation.
- Mutual-aid requests and operational state transitions are directly manageable from CAD.
- Incident-scoped operational messaging is visible/sendable from CAD, including directed receipt targeting.
- Selected operational messages can be promoted into the immutable incident narrative through the audited server path.
- Full Release build, SelfTest, source verifier, runtime persistence/restore/replay smoke and all-four-client Xvfb startup smoke remain green.

## Not yet production-certified

The source now compiles and runs, but these items still require additional implementation or real target infrastructure before a production public-safety declaration:

- Harden the PostgreSQL command model further toward fully relational per-command transaction boundaries instead of relying on in-process stores plus durable journal/checkpoint projections for every workflow.
- Validate PostgreSQL replication, promotion, PITR, external witness behavior, quorum/fencing and geographic DR against the actual Dispatch/HQ infrastructure.
- Complete/qualify OIDC/Entra, passkey/WebAuthn/FIDO2 and device-certificate/PKI flows against selected identity systems.
- Implement and certify target-specific 9-1-1, radio, Eventide/recording, NERIS production transport, routing/traffic, ONVIF/RTSP, AVL, AIS and FileGDB adapters against actual vendor systems.
- Complete production-grade dockable/floating multi-monitor workspace behavior and remaining role-specific workflow/UI depth.
- Perform load, penetration, accessibility, failure-mode, upgrade/rollback, offline/reconnect, multi-dispatcher race and operational acceptance testing on target hardware/infrastructure.

Do not represent DISRoute as production-certified public-safety CAD/RMS software until those items are completed and independently validated.

## Pass 14 Windows CAD site intelligence

- Added incident site-intelligence aggregation for matched premises, published preplans, CRG/TIFF floor assets, hydrants, active road restrictions and incident/global map objects.
- Scene-coordinate proximity is only calculated when an incident Point map object establishes coordinates; otherwise the operator receives an explicit limitation warning.
- CAD now includes a site-intelligence workspace with the required CRG floor selector plus premise hazards/access/fire-protection/utilities and water-supply context.
- CAD can create incident Point map objects and time-bounded dispatcher road restrictions, and can explicitly end selected restrictions.
- Historical replay preserves the active state of road restrictions at past timestamps before a later end.
- Release build, SelfTest, source verification, runtime persistence/restore/replay smoke and all-four-client startup smoke remain green.

## Pass 15 Windows CAD incident management

- Added an incident-management workspace for audited field corrections, relationship management, merge and split.
- Corrections require a reason and surface correction history to the operator.
- Merge retains optimistic source/target version checks, durable idempotency and now requires typed confirmation of the source incident number in CAD.
- Relationship/merge workflows reject agency mismatches, and server realtime broadcasts now carry correction, relationship, merge, split and changed-unit projections to other dispatch clients.
- SelfTest validates merge unit transfer/close/relationship/idempotent retry plus split preservation and relationship agency rejection.
- Release build, SelfTest, source verification, runtime persistence/restore/replay smoke and all-four-client startup smoke remain green.

## Pass 16 — Windows Station operational delivery/run-sheet depth

The Windows Station workstation now exposes station-scoped provider delivery health and failure details, safe failed-delivery retry, typed station acknowledgement, provider-backed run-sheet printing with printer-key/result visibility, manual print confirmation, complete selected run-sheet content and selection-preserving refresh behavior. The server rejects retry of any delivery that is not actually failed, preventing duplicate station notifications/prints caused by an accidental retry of already-confirmed work. Station acknowledgement and manual printed-state transitions are idempotent. Realtime station/delivery/run-sheet change broadcasts were added. Full Release build, SelfTest, source verifier, restart/backup/restore/replay smoke and all four Avalonia startup smokes pass. Live PostgreSQL execution remains pending target infrastructure.


## Pass 18 RMS incident reporting foundation

- Added canonical CAD-to-RMS incident-report handoff with incident/version identity, timestamps, location/caller data, retained participating units, activity, command, mutual aid and narrative snapshots.
- Added explicit CAD / RecordsUser / RmsAmendment field provenance; Draft CAD refresh preserves user-owned overrides.
- Added optimistic-versioned Draft -> InReview -> Finalized review workflow, reasoned finalized amendments, immutable report versions and attachment/storage references.
- Added canonical readiness validation without conflating it with provider-specific NERIS validation; NERIS transport remains provider-neutral and asynchronous.
- Added Operations Center report workflow and typed client/API routes while preserving existing prevention, NERIS, hazmat, investigation and personnel RMS modules.
- Added migration 014 and checkpoint/PostgreSQL/export persistence for RMS reports, immutable versions and attachment references.
- Runtime smoke now proves a finalized RMS report survives process restart and staged backup restore in addition to the existing CAD replay/idempotency checks.
- Full 17-project Release build is clean at 0 warnings / 0 errors; SelfTest, source verifier, runtime smoke and all four Avalonia startup smokes pass. Live PostgreSQL/production NERIS provider qualification is still not claimed.


## Pass 19 — RMS-linked NERIS mapping and durable submission

- Finalized RMS report versions now generate deterministic `DISRoute-NERIS-Bridge-2026.1` payloads with SHA-256 identity; unsupported profile versions fail closed.
- Mapping validation is explicit and separate from provider validation/transport. The RMS readiness endpoint now reports provider validation as no longer pending once the current RMS version has completed a successful provider-validation stage.
- Added durable NERIS submission state and immutable attempt history with stable provider operation IDs, selected provider key, provider reference, errors and validation warnings.
- Added authoritative-primary-only submission worker with persistence before/after external transport. In-flight state restored after process loss becomes `NeedsReconciliation` instead of being automatically retried.
- Explicit retry and reconciliation workflows prevent silent duplicate external filings after ambiguous transport failures; provider success without a durable reference also requires reconciliation.
- Added Operations Center NERIS submission UI plus JSON export, provider validation, queue/retry/reconcile API/client surfaces and realtime change broadcasts.
- Added PostgreSQL migration 015 and checkpoint/PostgreSQL/export persistence for NERIS submissions and attempts.
- Runtime smoke now forces one provider failure and one successful retry, then proves immutable attempt history and accepted submission state survive restart and staged backup restore.
- No claim is made that this bridge payload is an officially certified production NERIS schema. Live provider/schema acceptance remains required.

## Pass 20 — Workforce daily staffing / roster depth

- Workforce now has persisted qualification/minimum staffing rules and effective station/unit assignments with operational assignment states including acting, detail, overtime, training, open vacancy, call-out and relief.
- Staffing evaluation flags deficiencies without autonomously filling vacancies.
- Call-out creates an explicit open slot; individual relief is atomic at an effective time.
- Posted roster snapshots are immutable, revisioned and hashed; current effective staffing remains distinct from the last posted schedule.
- Structured same-day schedule changes preserve actor/time and feed Current Schedule + Changes printing.
- Operations Center exposes roster management, staffing rules, call-out/relief, posted revisions and provider-backed print/preview while preserving existing leave/trade/rotation/overtime/timesheet workflows.
- Portable checkpoint state and PostgreSQL schema version 16 include normalized workforce staffing rules, assignments, schedule-change events and roster snapshots.
- Runtime smoke proves a workforce call-out deficiency, explicit overtime coverage, roster revision history and change history persist through restart and staged restore.
- Remaining Workforce depth includes employee/mobile self-service authorization/UX, leave balances/projections, recurring-rotation materialization/master schedule, whole-crew relief, assignment-driven temporary security scope, real-time employee notifications, advanced agency/labor/rest/seniority rules, branded PDF output and production payroll/printer qualification.

## Pass 21 — DMS / Forms / Approvals operational depth

- DISRoute now has a canonical DMS revision workflow with SHA-256 content identity, immutable published/superseded revisions and approval-gated publication.
- DMS distribution supports Agency, Station, Role and User scopes; required-read tracking is version-specific with durable read/acknowledgement receipts and overdue evaluation. Until directory/group expansion is connected, RequiredRead on non-User scopes requires explicit recipients.
- Draft revisions may carry external asset/storage references with metadata and optional SHA-256. Once a revision is published, that revision's asset package is locked.
- Documents can be linked to operational/records entities including incidents, RMS reports, units/apparatus, stations, forms and personnel references.
- Form templates/instances now enforce stronger agency/JSON rules and durable form signatures preserve signer, meaning, template version and values SHA-256. These records are audited evidence, not a claim of qualified cryptographic/legal e-signature certification.
- Operations Center exposes DMS revision/approval/publication, distribution/required-read, asset/link, form/signature and approval-step workflows.
- Portable state schema and PostgreSQL projection are now version 17 with migration `017_dms_forms_depth.sql`.
- Full Release build, SelfTest, source verifier, restart/backup/staged-restore smoke and all four Avalonia startup smokes pass. Live PostgreSQL target execution, automatic AD/Entra recipient expansion, binary object storage, branded PDF/production printing and records-retention/legal-hold enforcement remain pending production depth.

## Pass 22 — Internal Chat / Communications

- General internal communications now exist alongside the pre-existing incident operational-message stream.
- Persisted channels support Direct, Station, Group, Supervisor, Agency and explicit MutualAid scopes.
- Direct/MutualAid membership is explicit; MutualAid rooms also carry participating-agency lists. Cross-agency chat is never implicitly visible to an entire agency.
- Chat messages are immutable records with priority/classification, optional incident link, mentions and external attachment/storage references.
- Per-user read cursors drive unread counts. Accessible message history is searchable without exposing inaccessible channels.
- Archived channels are read-only and remain searchable/history-preserving.
- SignalR has authorization-checked chat groups plus per-user groups for direct channel/mention notifications; Operations Center provides the current desktop chat workspace and online presence view.
- Portable state/PostgreSQL schema version is 18 (`018_internal_chat.sql`).
- SelfTest, runtime restart/backup/restore, source verification and all four Avalonia startup smokes pass.
- Production boundaries still include binary attachment/object storage, directory-backed dynamic membership expansion, mobile push/offline notification delivery, retention/legal-hold controls and external chat federation.

## Pass 22.1 — Internal Chat realtime and authorization hardening

- Operations Center now consumes internal-chat changes from the SignalR operations hub and joins/leaves the selected `chat:{channel}` group; API reads remain the durable snapshot/history path while incoming messages, read-state/channel changes and presence update live.
- Server-resolved identity and agency are authoritative for chat and presence. Unknown/disabled/revoked users fail closed, and client-supplied agency IDs cannot grant access to another agency's channel list/search, agency SignalR group or presence scope.
- Station chat access dynamically follows effective Workforce assignments at the station. Group chat access dynamically follows a bound active incident operational group and its explicit member-user list/leader.
- Operational-group membership has an audited update API/client surface and is persisted in portable state/PostgreSQL together with the Group channel binding.
- Chat attachment SHA-256 metadata is format-validated when supplied.
- State/PostgreSQL schema is 19 (`019_chat_membership_hardening.sql`).
- Verified source metrics: 111 hand-written C# files, 17 projects, 296 API routes and 19 PostgreSQL migrations.
- Verification is green in the artifact environment: 17-project Release build at 0 warnings/0 errors, SelfTest, source verifier, a process-level SignalR live-delivery + restart/backup/restore smoke, and startup smoke for CAD, Station, Operations Center and Station Display.
- This pass does not claim production AD/Entra dynamic membership, binary chat object storage, mobile push/offline notification delivery, retention/legal-hold certification, external federation or live PostgreSQL infrastructure qualification.


## Pass 24 integrations / gateways

- Added normalized, replay-safe 911, radio/status and AIS gateway events plus durable provider-operation/attempt history.
- Added provider-backed routing, recording/Eventide-style lookup, ONVIF discovery/import and radio emergency-clear paths with simulation providers for deterministic qualification.
- Ambiguous in-flight provider operations restore as `NeedsReconciliation`; DISRoute does not blindly replay possible duplicate external side effects.
- The provider-operation `Submitting` boundary is durably checkpointed/projected before the external call, including under the PostgreSQL authoritative command gate.
- Added Operations Center gateway/provider health, event, operation, attempt, recording and reconciliation visibility/control.
- PostgreSQL/portable state schema is 20 with `020_integration_gateways.sql`; normalized projection persists integration events, operations, attempts and recording references.
- Current verified source metrics: 118 hand-written C# files, 18 projects, 311 API routes and 20 migrations.
- Full Release build, SelfTest, source verifier, integration runtime restart/backup/restore regression and five-client startup validation pass in the artifact environment.
- Live vendor conformance/credentials/production acceptance for specific 911/radio/Eventide/routing/FileGDB/AIS/ONVIF systems remains outside this checkpoint.


## Pass 25 — PostgreSQL / HA production hardening and qualification harness

- Added an immutable PostgreSQL migration ledger (`disroute_schema_migrations`) with version, filename and SHA-256 identity. Applied-history hash/name mismatches or unexpected ledger rows fail startup and instruct operators to add a new migration rather than edit history; each migration SQL body and its ledger identity now commit atomically.
- Normalized the pre-ledger migration chain so migrations 001–020 deterministically advance `schema_info` to their own version; source verification now enforces that invariant.
- Concurrent writable-node startup serializes migration/ledger establishment with a dedicated PostgreSQL advisory lock. A physical/read-only replica never attempts DDL; it may start in HA mode only when the fully replicated ledger and schema are current and hash-valid.
- Promotion eligibility now proves that the candidate's local PostgreSQL endpoint is not in recovery, is not transaction/default read-only and can actually perform a rolled-back write probe. The database is re-probed after the application lease is acquired to close the eligibility/lease TOCTOU window.
- A promoted standby reloads the latest hash-validated PostgreSQL runtime checkpoint after the new lease epoch is durably recorded, then re-pings the lease/witness session and re-proves local database writability before `AcceptingWrites` becomes true, preventing stale long-running standby memory from overwriting newer authoritative state or opening a write window after a mid-reload lease/database failure.
- Every normal authoritative PostgreSQL checkpoint/projection transaction now holds a scoped HA commit lease through commit, locks and verifies the durable HA fencing token (`active_node_id`, `active_instance_id`, `lease_epoch`), and revalidates the live lease/witness session immediately before commit. A local drain cannot race underneath an in-flight checkpoint, and an old primary whose token is superseded cannot make a late authoritative commit.
- Command durability now distinguishes leadership loss before commit from an ambiguous response after commit. A command that loses leadership before its checkpoint returns 503 and restores its in-memory batch; a transaction already committed under a valid fencing token is not rolled back.
- The active advisory/witness lease session is verified with real database I/O each HA cycle; a dead session is fenced instead of being trusted from connection-state flags alone.
- Added development-only qualification crash points for `after-durable-commit` and `integration-after-submitting`. Production startup rejects crash injection.
- Added `scripts/postgresql-ha-qualification.sh`, an explicitly destructive disposable-database harness covering concurrent migration startup, exact source-to-ledger SHA-256 verification, exactly-one-writer election, crash-after-commit/idempotent retry, observable stale-standby reload before retry, provider `Submitting` crash recovery to `NeedsReconciliation`, old-primary return, planned drain/resume, migration-ledger backfill/tamper rejection, and optional read-only replica plus logical backup/restore checks.
- Persistence status now reports supported schema version, database role (`WritablePrimary`, `ReadOnlyReplica`, or not applicable) and whether migration integrity was verified.
- Local artifact verification is green: 18-project Release build at 0 warnings/0 errors, SelfTest (`audit=145`, `alerts=3`), source verifier (120 C# files / 311 routes / 20 migrations), full FileCheckpoint runtime restart/backup/restore regression, and all five Avalonia startup smokes.
- **Pass 25 live qualification:** the destructive application-HA harness passed against disposable PostgreSQL 18.6, including exact 20-migration hash verification, exactly-one-writer election, crash-after-commit failover, observable promotion reload, provider `Submitting` reconciliation, old-primary fencing, planned drain/resume, migration backfill/tamper rejection, an actual physical streaming read-only hot standby, and logical backup/restore. Final local measured application failover was 528 ms; evidence is preserved under `docs/ops/evidence/`. Production physical-replica promotion, witness-loss/network-partition, STONITH/quorum, PITR/physical-backup recovery and Dispatch/HQ RPO/RTO remain target-topology acceptance work and are not claimed here.

## Pass 26 physical PostgreSQL HA qualification

The cumulative implementation now has an explicit PostgreSQL topology contract. `SharedPrimary` remains the default. `PhysicalFailover` requires an independent application witness plus declared external database fencing and fails startup otherwise. Real PostgreSQL physical streaming promotion, network partition fail-closed behavior, old-primary witness fencing, bidirectional rewind/failback, and physical-base-backup/WAL-archive PITR were qualification-tested in Pass 26. This does not select or certify a production database STONITH/quorum product.


## Pass 27 — Production deployment, security and operations hardening

- Production startup now validates PostgreSQL/authentication/HTTPS/MFA plus the Pass 26 physical-HA witness/fencing contract.
- External configuration and protected environment/file secret references cover database, witness, provider, LDAP, TLS and MFA material.
- Direct Kestrel TLS and explicitly trusted reverse-proxy TLS are supported with certificate/file validation, HSTS/security headers, rate limiting and safe readiness/error surfaces.
- Deployment-readiness and upgrade-preflight APIs expose nonsecret operational gates for authoritative upgrades.
- Linux has hardened systemd installation; Windows has native SCM hosting and service install/update/uninstall automation.
- Production publishing covers both server targets, Windows operational clients/Mobile field harness, Linux ARM64 Station Display and LegacyBridge, with SHA-256 deployment packaging.
- Live Production runtime qualification against PostgreSQL 18.6-compatible infrastructure passed; Pass 25 and Pass 26 destructive PostgreSQL HA/DR/PITR regressions also pass on the Pass 27 source.
- Live target Windows SCM/Raspberry Pi acceptance, native mobile distribution, signed installers, selected DB STONITH/quorum, vendor conformance, load/security/accessibility and agency acceptance remain external release qualification boundaries.
