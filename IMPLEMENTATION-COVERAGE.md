# DISRoute direct-build implementation coverage

This ledger deliberately separates source behavior implemented in this archive from behavior that still needs a real compiler, deployment infrastructure, or external vendor systems before it can be certified.

## Implemented in source

- Server-authoritative incidents, units, status, immutable narrative activity, collaborative shared narrative, close/reopen/correction and optimistic version checks.
- Call records, duplicate-candidate scoring, versioned response plans/recommendations, additional alarms, dispatch cancellation/update delivery semantics and AVL observations/advisories.
- Incident relationships, merge/split preservation, command assignments, groups/divisions, mutual aid, Mayday, Fire Watch and relocations.
- Dispatch Commit with durable, payload-bound idempotency and independent delivery records; command receipts survive restart/failover checkpoints and background delivery reconciliation remains independent for Station Display/run-sheet/Mobile paths and configurable provider-backed station alerting.
- SignalR agency/incident groups, client presence and field presence, collaborative narrative broadcasts, PostgreSQL-backed cross-node durable event relay, and PostgreSQL advisory-lock write fencing.
- Per-agency module toggles with dependency enforcement and API/background-worker fail-closed behavior when disabled.
- Premises, preplans, CRG metadata, hydrants, dispatcher road restrictions, shared map objects, fallback routing and AIS/vessel observations.
- Alerts with acknowledge/claim/escalate/resolve semantics; Mayday creates a critical correlated alert; background progressive escalation for unacknowledged alerts; supervisor bulletins.
- Persistent operational messaging with direct/channel/incident scope, priority/classification, queued/delivered/read/acknowledged receipts, offline receipt recovery, reference metadata, and promotion to the official incident narrative with provenance.
- Full Station snapshot/delivery acknowledgement/run-sheet tracking plus provider-backed print/station-alert actions.
- Dedicated Raspberry Pi/Linux ARM64 Station Display with last-received continuity cache.
- Windows Station workstation and Windows CAD clients using the operational API.
- Shared desktop/display HTTP transport with Windows integrated credentials by default, persistent secure-cookie support for DISRoute MFA sessions, mutation guard headers and validated server URLs.
- CAD and Station Avalonia data templates use compile-time typed bindings; Station delivery/run-sheet rows render explicit operational display text rather than record debug formatting.
- Operations Center client covering platform health, incidents, alerts, clients/providers, expanded Workforce, Documents/forms/workflows, expanded RMS, security, audit and HA/DR views.
- Workforce shifts, rotations, leave, overtime, trades, staffing, timesheets, payroll export and daily roster/change history.
- Documents/versioning/publication/acknowledgements, forms/signatures and sequential/parallel approval workflows.
- Canonical CAD-derived RMS incident reports with field-level provenance, immutable report versions, Draft/InReview/Finalized review lifecycle, reasoned finalized amendments, attachment references and CAD refresh that preserves Records-owned overrides. Finalized RMS versions can now produce a version-bound DISRoute NERIS bridge projection with deterministic SHA-256 payload identity, mapping diagnostics, durable queue state, immutable provider attempts, explicit retry/reconciliation semantics and provider-neutral transport; inspections, permits, violations, hazmat, investigations, maintenance, personnel qualifications and the older manual NERIS compatibility surface remain available.
- Generic configurable integration providers for HTTP station alerting, HTTP fire reporting/NERIS transport, file-spool printing and development simulators, plus provider-neutral contracts for routing, radio, 9-1-1, recording, AVL and AIS.
- Device inventory/heartbeat/revocation and role-permission enforcement with AD/group-to-role claim transformation.
- TOTP enrollment/verification, one-time recovery codes, AES-GCM-protected MFA secrets, signed short-lived MFA session cookies, production MFA enforcement, and signed short-lived device-bound offline authorization leases.
- Provider secrets accept only secret references rather than plaintext credentials.
- Tamper-evident SHA-256 audit chaining with whole-chain verification.
- Roaming workspace layouts and built-in/extensible theme package surfaces.
- Mobile core sync plus an authenticated-encryption offline queue for explicitly permitted operations.
- Durable runtime checkpoint/restore: atomic JSON file checkpoints for zero-setup development and PostgreSQL for production, with SHA-256 payload integrity, durable command-idempotency receipts and automatic ordered PostgreSQL migrations.
- Durable mutation journal and PostgreSQL outbox source path around authoritative mutations, with per-command durability-batch correlation, an authoritative checkpoint/projection publication barrier, background outbox publication only after that barrier, and PostgreSQL LISTEN/NOTIFY relay for committed operational events.
- PostgreSQL normalized live projections for agencies, stations, incidents, units, activity, messages, operational messaging/receipts, Workforce shifts, Documents/versions, RMS records, collaborative narratives and payload-bound idempotency receipts, synchronized in the same database transaction as durable checkpoint writes.
- PostgreSQL schema migrations through schema version 20, including durable journal/outbox/HA coordination, operational messaging, collaborative narrative operation history, HA topology/fencing metadata, historical replay state tables, durable idempotency request fingerprints/result typing, authoritative mutation-batch/outbox publication metadata, RMS/NERIS, Workforce, DMS/Forms, internal chat and Integration/Gateway normalized state.
- HA coordination: server-node/site heartbeat, non-reentrant PostgreSQL advisory-lock application leadership, monotonic lease epochs, planned node drain/resume, stale-node detection, live replication lag inspection, primary-write fencing, standby realtime relay, primary-only background side effects/checkpointing/journaling, and optional external witness enforcement for geographic automatic-promotion safety.
- Incident historical replay reconstructs a timestamped operational snapshot from retained incident/unit/dispatch/delivery/station/alert histories and AVL/AIS observations, explicitly classifying KNOWN, OBSERVED and CORRECTED LATER facts and reporting limitations rather than silently using current state.
- After-action incident ZIP packages, records export packages, and redacted administrator support bundles.
- Portable state backups with SHA-256 manifest validation, retention, listing/verification API, and safe FileCheckpoint restore staging that is applied only on server restart while preserving a pre-restore rollback checkpoint.
- Runtime smoke coverage now proves create -> assign -> narrative -> close -> canonical RMS report create/review/finalize -> NERIS bridge projection -> forced provider failure -> safe retry/acceptance with immutable attempt history -> durable checkpoint/journal -> restart verification -> backup verification -> staged restore -> restart -> rollback semantics.
- Full 18-project solution restore and Release build verified with .NET 10: 0 warnings, 0 errors, including CAD, Station, Operations Center, Station Display and Mobile.
- Linux/Xvfb UI startup smoke for all five Avalonia applications against a live development Server.
- Cross-platform LDAP/AD group resolution over LDAPS/StartTLS with bounded timeouts, caching, filter escaping, secret-file bind credentials, and fail-closed required-provider behavior.
- LegacyBridge executable for deterministic canonical staging validation/import with provenance, checksum and idempotency handling.
- Windows/Linux/ARM64 publish scripts and Linux systemd/nginx deployment templates.
- Production HTTPS/authentication safeguards, primary-write fencing, MFA enforcement, mutation guard header, no-store API responses, secure response headers and protected request-size defaults.
- Executable end-to-end self-test source covering representative CAD -> dispatch -> station -> GIS -> advanced CAD -> operational messaging -> Workforce/DMS/RMS -> persistence -> security/audit paths.
- Repository static verifier covering project/XML/JSON structure, ProjectReference targets, API route collisions, contiguous migration/schema versions, unfinished markers and legacy-branding guard.

## Still not certifiable in this artifact environment

- **Avalonia client qualification:** all five Avalonia applications restore and compile under .NET 10 with warnings-as-errors, and Linux/Xvfb startup smoke proves each process can initialize against a live DISRoute Server without an immediate crash. Interactive workflow, Windows multi-monitor/docking, accessibility, native mobile packaging and Raspberry Pi graphics qualification still require target-hardware testing.
- **Fully normalized per-command PostgreSQL model:** API commands run behind a node-level command/checkpoint gate and their journal/outbox rows are batch-correlated; outbox publication is blocked until the checkpoint/normalized-projection transaction marks the batch authoritative. Primary-only delivery/escalation workers use the same gate. Pass 25 executed this barrier against real PostgreSQL under crash/failover qualification, but the in-process domain stores/checkpoint remain the primary command model rather than every CAD command executing directly as one normalized relational transaction.
- **Production multi-node HA/DR qualification:** Pass 25 live-tested application-level fencing, lease epochs, exactly-one-writer behavior, planned local switchover, crash-after-commit recovery, stale-standby reload, old-primary return, physical streaming read-only replica startup and logical backup/restore against disposable PostgreSQL 18.6. Production database promotion, external witness loss, network partitions, geographic DR, STONITH/quorum, PITR/physical-backup recovery and full Dispatch/HQ failover drills still require the selected production topology and are not certified by this artifact run.
- **Identity integrations requiring third parties:** Windows/AD role mapping, cross-platform LDAPS/StartTLS group resolution, TOTP and signed MFA sessions are implemented, but OIDC/Entra, passkey/WebAuthn/FIDO2, PKI/device-certificate issuance and external identity binding require the selected identity/PKI systems and protocol qualification.
- **Vendor/provider adapters:** configurable generic station-alert, fire-reporting and printing providers are implemented, but target-specific 9-1-1, radio, recording/Eventide, NERIS production transport, traffic/routing, ONVIF/RTSP, AVL, AIS, FileGDB and other adapters require actual vendor systems, credentials/protocols and acceptance testing.
- **Collaborative text qualification:** deterministic operation-transform rebasing is implemented for non-overlapping concurrent insert/delete edits with conflict rejection and durable operation history. Broader randomized/concurrency soak testing is still required before production certification.
- **Docking framework:** workspace persistence/themes exist, but the final third-party/native docking/floating multi-monitor framework remains an explicit product decision and therefore cannot be truthfully finalized without choosing/qualifying it.
- **Certification:** load, penetration, accessibility, failure-mode, upgrade/rollback, offline/reconnect, multi-dispatcher race testing and public-safety operational validation require compiled/deployed binaries and real infrastructure.

No item in the second section should be represented as production-certified merely because contracts, simulators, schemas or source logic exist.

## Pass 12 Windows CAD incident-workspace depth

- Replaced whole-document collaborative narrative saves with minimal patch submission so the server can rebase non-overlapping dispatcher edits.
- Overlapping narrative conflicts now preserve local unsaved text; discarding it requires an explicit operator action.
- Added a tested shared `NarrativePatchBuilder` for insert/delete/replace/no-change transformations.
- Expanded manual call intake with callback capture, duplicate-candidate checking and linked manual call-record creation.
- Added multi-unit assignment/status operations, response-plan recommendations/warnings, dispatch history/delivery summary, additional alarms and dispatch cancellation.
- Added Fire Watch lifecycle, relocation lifecycle and actionable alert acknowledgement/claim/escalation/resolution to the Windows CAD workspace.
- Extended the typed client API layer for those existing server endpoints without altering server-authoritative behavior or HA/DR topology.
- SelfTest now includes client patch-generation round trips plus relocation and dispatcher alert-action regression coverage.
- Full Release build, SelfTest, source verifier, runtime persistence/restart/backup/restore/replay smoke and all-four-client Xvfb startup smoke pass.

## Pass 13 Windows CAD command/supervisor operations and messaging

- Added typed Windows-client access to operational command assignments, groups/divisions, mutual-aid workflows and incident operational messaging.
- CAD now exposes command-role assignment, group/division creation, mutual-aid request/state transitions and incident-scoped message history/send.
- Directed operational messages can request delivery/read/acknowledgement receipts through the existing server workflow.
- A selected operational message can be promoted directly into the immutable incident narrative through the existing audited server path.
- Existing SelfTest coverage for command/groups/mutual aid/messaging remains green; full Release build, source verifier, runtime restart/restore/replay smoke and all-four-client Xvfb startup smoke pass.

## Pass 14 Windows CAD site intelligence / GIS operational context

- Added a server-authoritative incident site-intelligence projection joining matched premises, published preplans, CRG/TIFF floor assets, hydrants, road restrictions and map objects.
- The projection explicitly distinguishes known location data from inferred proximity; it warns when no incident coordinates exist instead of presenting unverified "nearby" results.
- Incident Point map objects establish scene coordinates; hydrants and road restrictions are then distance-ranked for the incident workspace.
- CAD now exposes premise hazards/access/fire-protection/utilities, published preplans, hydrant/water information and active road restrictions directly inside the incident workspace.
- Added the required CRG floor selector with registered overlay/revision/rotation/scale/opacity/source metadata.
- Dispatchers can create incident map points and time-bounded road restrictions, then explicitly end selected restrictions.
- Historical replay reconstructs a road restriction as active for an as-of timestamp within its operational interval even if it was ended later.
- SelfTest covers site matching, preplans, CRGs, coordinate extraction, hydrant/restriction distance, restriction completion and the historical restriction-state regression.
- Full Release build, SelfTest, source verifier, runtime persistence/restart/backup/restore/replay smoke and all-four-client Xvfb startup smoke pass.

## Pass 15 Windows CAD incident correction / relationships / merge-split

- CAD now exposes audited incident-field corrections with mandatory reasons and visible correction history.
- CAD now exposes incident relationships, including inbound/outbound context and other-incident identity.
- Merge and split workflows are available through typed client APIs while preserving optimistic versions and durable idempotency.
- Merge requires an explicit typed source-incident-number confirmation and limits UI targets to open same-agency incidents.
- Relationship and merge server workflows now fail closed on agency mismatches/cross-agency targets.
- Correction, relationship, merge and split server endpoints now emit the missing realtime incident/unit/relationship notifications needed by other dispatcher clients.
- SelfTest covers relationship agency rejection, split preservation, merge close/unit transfer/relationship behavior and durable merge retry.
- Full Release build, SelfTest, source verifier, runtime persistence/restart/backup/restore/replay smoke and all-four-client Xvfb startup smoke pass.

## Pass 16 Windows Station delivery and run-sheet operations

- Added station-scoped provider delivery-path status with attempt/error visibility for Station Display, Station Alert and Run Sheet paths.
- Windows Station now provides safe failed-path retry, typed acknowledgement, provider-backed printing, manual print confirmation and full selected run-sheet content.
- Delivery retry is server-hardened to `Failed` state only so confirmed/pending delivery work cannot be accidentally duplicated.
- Station acknowledgement and manual print-state transitions are idempotent, while provider print results retain explicit state/reference/error detail.
- Auto-refresh preserves operator selections where possible instead of repeatedly resetting the active incident workflow.
- Realtime broadcasts now carry station acknowledgement, delivery retry and run-sheet state changes to connected operational clients.
- Pass 16.1 regression hardening creates a real second-station dispatch and proves station delivery-path projections exclude the other station in both directions.
- Release build, SelfTest, source verification, runtime persistence/restore/replay smoke and all-four-client startup smoke remain green.

## Pass 17 Supervisor and Mayday/radio-emergency operations

- Mayday now has an explicit persisted lifecycle: Active, Acknowledged, Assigned, Escalated and Resolved.
- Declarations validate incident-agency and optional unit-agency consistency and continue to raise a correlated Critical alert.
- Authenticated supervisor actions acknowledge, claim/assign, escalate and resolve the emergency, with linked critical-alert state/ownership synchronized without losing an existing owner during escalation.
- Operations Center now exposes active Maydays/radio emergencies with explicit textual state, person, location, channel, owner and escalation level plus Acknowledge/Assign/Escalate/Resolve controls.
- Realtime Mayday updates publish to agency and incident groups.
- SelfTest proves lifecycle behavior, linked-alert ownership, state export/import survivability and post-restore resolution.
- Full Release build, SelfTest, source verifier, runtime restart/backup/restore/replay smoke and all-four-client startup smoke pass.


## Pass 18 — RMS incident-report foundation and CAD handoff

- Added a canonical typed RMS incident-report domain derived from CAD incidents without retyping core dispatch facts.
- CAD handoff snapshots incident identity/version, location/caller fields, timestamps, participating units from retained unit history, incident activity, command assignments, mutual-aid state and collaborative/official narrative.
- Added field-level provenance distinguishing `CAD`, `RecordsUser` and `RmsAmendment` ownership. Draft CAD refresh updates only CAD-owned scalar facts while preserving explicit Records-user overrides.
- Added Draft -> InReview -> Finalized records lifecycle, supervisor return/finalize decisions, optimistic report-version checks, durable idempotency on mutation operations and destructive-edit rejection after finalization.
- Finalized changes require a reasoned amendment that actually changes report content; amendments remain finalized and append provenance/version history rather than rewriting prior versions.
- Added immutable RMS version snapshots plus attachment/document references that point to DMS/storage instead of embedding arbitrary files in the report record.
- Added canonical readiness validation separately from provider-specific NERIS validation. Provider-specific NERIS validation/submission remains pending the existing neutral adapter boundary and never blocks CAD.
- Added RMS report APIs/client methods and realtime report/attachment broadcasts. The Operations Center RMS tab now creates reports from CAD, displays validation/provenance/version/attachment history, refreshes CAD-owned facts, edits Draft summaries, routes review, finalizes/returns reports, amends finalized records and adds attachment references. Existing prevention/NERIS/hazmat/investigation/personnel modules remain available.
- Added PostgreSQL migration 014 and normalized projection tables for report heads, immutable report versions and attachment references; portable checkpoint state and records/after-action exports include the same report/version/attachment data.
- Tightened the legacy generic RMS path so a linked incident must belong to the requested agency.
- SelfTest covers CAD-vs-Records field ownership, closed-incident refresh/readiness, unit/command/mutual-aid snapshots, review return/finalization, final-edit/no-op-amendment rejection, reasoned amendment provenance, attachment references, canonical single-report behavior and export/import persistence.
- Runtime smoke now creates and finalizes an RMS report through the HTTP API before backup, verifies it and its version history after a real process restart, then verifies it again after staged restore.
- Full Release build, SelfTest, source verifier, restart/backup/restore/replay/RMS runtime smoke and all-four-client Xvfb startup smoke pass in this artifact environment. Live PostgreSQL execution and provider-specific production NERIS qualification remain pending target infrastructure.


## Pass 19 RMS-linked NERIS submission pipeline

- Finalized RMS reports are the authoritative source for the new NERIS bridge path; manual JSON NERIS records remain only as a compatibility surface.
- Added the versioned `DISRoute-NERIS-Bridge-2026.1` projection with deterministic JSON payload and SHA-256 identity tied to a specific finalized RMS report version.
- Added mapping errors/warnings separately from provider validation. Unsupported bridge-profile versions fail closed rather than silently generating a different payload.
- Added durable submission states (`Ready`, `Queued`, `Submitting`, `Accepted`/`Confirmed`, `Failed`, `NeedsReconciliation`, `Superseded`) and immutable per-attempt operation IDs/history.
- Submission worker is authoritative-writer-only, persists the `Submitting` boundary before calling the external provider, and persists provider results afterward. PostgreSQL mode uses the existing command/checkpoint gate and mutation durability barrier.
- A restored in-flight attempt becomes `NeedsReconciliation`/`UnknownAfterRestore`; DISRoute does not automatically retry an ambiguous external filing. Retry requires explicit duplicate-risk acknowledgement unless the operator first reconciles the provider outcome.
- Provider success without a durable provider reference is treated as `NeedsReconciliation`, not silently complete.
- Added provider-validation preview, queue, retry, reconciliation, direct JSON export, realtime change broadcasts and Operations Center workflow/attempt visibility.
- Legacy manual NERIS linkage now rejects incident/agency mismatch and the legacy submit route uses the authenticated server actor rather than trusting client-supplied actor text.
- Added migration 015 with normalized NERIS submission and attempt tables; portable checkpoint, PostgreSQL projection, after-action and records exports include the new state.
- SelfTest covers finalized RMS projection/hash, unsupported profile rejection, one projection per RMS version/profile, durable queue/in-flight state, restore-to-reconciliation behavior, unsafe-retry rejection and safe reconciled retry.
- Runtime smoke forces an unreachable HTTP provider failure, retries through the simulation provider, and proves both attempts plus the accepted provider reference survive process restart and staged backup restore.
- This remains a provider-neutral DISRoute bridge profile. Official production NERIS schema certification, credentials, endpoint acceptance and conformance testing still require the selected live NERIS/provider environment.

## Pass 20 — Workforce daily staffing / roster operations

- Added persisted staffing rules scoped by agency/station/shift/position with minimum headcount and qualification requirements.
- Added effective station/apparatus workforce assignments with acting/detail/overtime/training/open/call-out/relieved states and explicit effective-time semantics.
- Call-outs create real open vacancies; vacancies never satisfy minimum staffing and DISRoute does not autonomously assign personnel.
- Added qualification-aware staffing evaluation, atomic individual relief, immutable posted roster revisions, structured same-day schedule-change history and current-vs-posted roster projection.
- Added point-in-time Current Schedule / Current Schedule + Changes print content with SHA-256 identity and provider-backed printing through `IPrintProvider`.
- Operations Center now provides operational Workforce controls for assignments/open shifts, call-outs, relief, staffing rules, posted revisions, change history and print/preview while retaining leave/trade/rotation/overtime/timesheet views.
- Added schema/migration 016 plus checkpoint/PostgreSQL projection persistence for staffing rules, assignments, change events and roster snapshots.
- Runtime smoke proves deficiency -> explicit overtime coverage -> roster revision 2 survives process restart and staged backup restore, including immutable roster hashes and change history.
- Full 17-project Release build, SelfTest, source verifier, runtime smoke and all-four-client startup smoke pass.
- Employee/mobile self-service authorization/UX, leave balances, master-schedule rotation expansion, whole-crew relief, duty-driven temporary authorization, advanced labor/rest/seniority rules, branded PDF and production payroll/printing qualification remain future Workforce depth and are not represented as complete here.

## Pass 21 — DMS / Forms / Approvals operational depth

- Added canonical immutable DMS revisions with SHA-256 identity, Draft/Published/Superseded state and approval-gated publication.
- Added station/role/user/agency distribution scopes, version-specific required-read receipts, read/acknowledgement state and overdue tracking.
- Added draft-only external asset references with storage metadata/optional SHA-256 and post-publication asset locking.
- Added document links to incidents, RMS reports, units/apparatus, stations, forms and personnel references with same-agency validation where stable entity identity exists.
- Hardened form templates/instances and added immutable hash-backed form signature records with template version, signer/meaning/source and values SHA-256.
- Hardened approval/document/form actions to use authenticated server actor attribution and known-target agency validation.
- Operations Center now has operational DMS, Forms and Approvals workspaces rather than list-only document/form views.
- Portable checkpoint state and PostgreSQL schema version 17 include DMS revisions, distributions, read receipts, external asset references, entity links and form signatures; migration `017_dms_forms_depth.sql` added.
- Runtime smoke proves approved two-version policy history, required-read acknowledgement, external asset reference, incident linkage and strong form signature survive process restart and staged backup restore.
- Explicit non-claims: automatic AD/Entra group expansion for required reads, binary object storage, qualified cryptographic e-signature, branded PDF/production printing, retention/legal-hold/disposition and live PostgreSQL qualification remain future work.

## Pass 22 — Internal Chat / Communications

- Added a persisted internal chat domain separate from, but compatible with, incident-scoped operational messaging.
- Channel types now include Direct, Station, Group, Supervisor, Agency and MutualAid.
- Direct and MutualAid rooms require explicit membership. MutualAid rooms additionally require explicit participating-agency lists and are never exposed through agency-wide realtime groups.
- Added immutable chat messages with priority/classification, optional incident linkage, normalized mentions and external attachment/storage references with optional SHA-256 identity.
- Added per-user read cursors and unread counts, accessible-history search, archive/read-only behavior and presence display through the existing realtime presence projection.
- SignalR now creates per-user notification groups and authorization-checked `chat:{channel}` groups; unauthorized clients cannot join a channel group.
- Operations Center now exposes channel creation, channel history, send/read/archive controls, presence, search, mentions, incident links and attachment references.
- Portable state schema and PostgreSQL projection are now version 18 with migration `018_internal_chat.sql` for channels, messages and read cursors.
- SelfTest covers direct membership, unread/read state, scoped search, attachment/mention/incident linkage, explicit MutualAid participation, nonmember denial, archived-channel write rejection and export/import persistence.
- Runtime smoke creates an incident-linked agency chat message with attachment reference and proves it survives a real server restart and staged backup restore.
- Full 17-project Release build, SelfTest, source verifier, runtime persistence/restore/replay smoke and all-four-client startup smoke pass.
- Binary attachment storage, directory-driven dynamic channel membership, mobile push notifications, message retention/legal-hold policy and federation with external chat platforms remain future production depth.

## Pass 22.1 — Internal Chat authorization / realtime hardening

- Operations Center chat now uses a real SignalR client path instead of relying on API refresh for visible conversation updates. The client negotiates the operations hub, joins the authorized agency and selected `chat:{channel}` group, re-joins after reconnect, publishes presence and appends `chatMessage` events live.
- Chat/presence agency context is now server-authoritative. Channel list/search and presence cannot be widened by supplying another agency identifier, and SignalR `JoinAgency`/`SetPresence` reject agency spoofing.
- Chat identity now fails closed: unknown, disabled or revoked users do not inherit the default agency and cannot gain channel access or connect to operational presence/chat groups.
- Station channel membership is dynamic from the current effective Workforce station assignment. A call-out/ended assignment removes that implicit access without editing the chat channel.
- Group channels now bind to a concrete incident operational-group ID. Operational groups carry explicit provisioned member user IDs, expose an audited membership-update route, and Group chat access follows the active group leader/member list.
- Direct and MutualAid rooms remain explicit-membership only. MutualAid still requires participating agencies plus at least one explicit member from each participating agency.
- Chat attachment SHA-256 metadata is validated as a real 64-character hexadecimal digest when supplied.
- Portable state/PostgreSQL schema version is now 19 with migration `019_chat_membership_hardening.sql`, persisting operational-group member IDs and the chat-channel operational-group binding.
- SelfTest proves fail-closed identity, foreign-agency query denial, live Workforce-driven Station membership changes, operational-group-driven Group membership changes, attachment-hash rejection and persistence of the new relationship data.
- Runtime smoke now performs a real SignalR negotiate/WebSocket handshake, joins the authorized agency/channel groups, publishes presence, posts a message through HTTP and proves that exact `chatMessage` is received live over the hub before proceeding through restart and staged backup restore.
- Full Release build, SelfTest, source verifier, restart/backup/restore/replay smoke and all-four-client Avalonia startup smoke pass. Production AD/Entra membership expansion, binary attachment storage, mobile push/offline notifications, retention/legal-hold controls, external federation and live PostgreSQL target qualification remain future production work.

## Pass 23 — Mobile / offline field operations foundation

- Added `DISRoute.Mobile` as the 18th solution project: a touch-first field companion UI with a desktop-hosted Avalonia harness for deterministic build/startup testing.
- Replaced the thin agency-wide Mobile sync projection with an active-device and actor-authorized operational package driven by effective Workforce unit assignment (with broader agency-unit scope only for authorized Dispatcher/Supervisor/Administrator users).
- Mobile package now includes selected unit/current incident, current crew, preplan/CRG/hydrant/road scene intelligence, active Maydays, incident operational messages, accessible internal chat/history, published incident/required-read DMS content and authorized listen-only radio streams.
- Added encrypted operational snapshot caching alongside the encrypted offline queue, with explicit Online/Offline/Stale presentation.
- Realtime incident/unit/Mayday/message/chat changes trigger read-only refresh only; queued writes are never silently reconciled in a background refresh.
- Offline Narrative, operational Message and ChatMessage envelopes now perform the actual authoritative mutation instead of being acknowledged without effect.
- Offline UnitStatus is restricted by a signed user/device/agency permission lease and an exact observed-status/`UpdatedAtUtc` stale-state check; conflicts remain queued for operator review rather than using last-write-wins.
- Reconciliation envelope and underlying command use distinct durable idempotency identities. Accepted/duplicate operations are removed; conflicts/rejections remain reviewable and can be explicitly discarded.
- Mobile client exposes large EnRoute/OnScene/Clearing/Available/Command/Code55 controls, crew/Mayday, incident messages/notes, queue review, scene/preplan data, chat/DMS and authorized listen-only radio feeds with no PTT control.
- Runtime smoke now issues and verifies a real offline lease through HTTP, reconciles Mobile narrative/chat/status operations, proves duplicate replay, and verifies Mobile state across process restart and staged backup restore.
- UI smoke now validates five Avalonia applications, including `DISRoute.Mobile`.
- Full Release build, SelfTest, source verifier, runtime restart/restore smoke and all-five-client startup smoke pass. Native Android/iOS packaging, target-OS secure-store/MDM qualification, production map/audio engines, push delivery and real-device acceptance remain future production work.

## Pass 24 — Integrations / Gateways operational depth

- Added durable normalized inbound gateway events for 911, radio/status and AIS with native IDs, payload hashes, source timestamps and idempotent replay handling.
- 911 lifecycle events update one canonical call record; duplicate radio emergencies do not duplicate the critical alert; AIS ingest updates retained vessel state.
- Added durable outbound provider operations and immutable attempts with explicit `Submitting`, `Confirmed`, `Failed` and `NeedsReconciliation` semantics. Restored in-flight provider work is never blindly resent.
- The `Submitting` boundary is durably checkpointed/projected before the external provider call, including under the PostgreSQL authoritative command gate.
- Added provider-backed routing, recording/Eventide-style lookup, ONVIF discovery/import and radio emergency-clear paths plus simulation implementations of the same production contracts.
- Added durable incident recording references with provider-reference deduplication.
- Operations Center now includes an Integrations / Gateways workspace for provider health, gateway events, provider operations/attempts, recordings, 911/radio/AIS simulation, routing, camera discovery/import and retry/reconciliation.
- Portable/PostgreSQL schema version is 20 with migration `020_integration_gateways.sql`; normalized projections actively persist events, operations, attempts and recording references.
- SelfTest proves gateway deduplication/lifecycle semantics, provider-operation ambiguity recovery and persistence. Runtime smoke exercises 911, AIS, routing, recording, ONVIF and radio provider paths across real restart and staged restore.
- Full 18-project Release build, SelfTest, source verifier, process-level restart/restore regression and five-client startup validation pass in the artifact environment.
- This does not claim live vendor certification/conformance for 911, radio, Eventide, ONVIF fleets, traffic/routing, FileGDB/Esri or AIS providers; those require selected production systems and credentials.


## Pass 25 — Production PostgreSQL + HA/DR qualification

- Added immutable migration identity with exact version/filename/SHA-256 validation, serialized migration establishment and atomic migration-SQL-plus-ledger transactions.
- Read-only replicas validate the fully replicated schema/ledger without DDL; promotion candidates must prove local PostgreSQL is writable and not in recovery/read-only mode.
- Promotion reloads the latest committed checkpoint after lease acquisition and revalidates both the lease/witness session and database writability before `AcceptingWrites=true`.
- Authoritative PostgreSQL commits hold the HA lease gate, row-lock and verify node/instance/epoch fencing state, and ping the live advisory/witness session immediately before commit.
- Qualification crash points cover post-commit/pre-HTTP acknowledgement and durable provider `Submitting`/pre-provider invocation; Production rejects enabling qualification crash injection.
- Live qualification on disposable PostgreSQL 18.6 discovered and fixed two defects before the final pass: checkpoint SHA-256 was computed before PostgreSQL `jsonb` canonicalization, and HA drain/resume returned a nested `Task<IResult>` from the endpoint wrapper.
- Final destructive harness PASS covered concurrent migrations, exact 20-hash ledger identity, exactly one writer, crash-after-commit/idempotent recovery, observable stale-standby reload, provider reconciliation, old-primary fencing, planned drain/resume, pre-ledger backfill, migration tamper rejection, an actual physical streaming read-only hot standby and logical backup/restore.
- Final local measured application crash failover was 528 ms; this is evidence for the artifact topology, not a production RTO guarantee. Exact evidence is preserved at `docs/ops/evidence/PASS-25-POSTGRESQL-HA-EVIDENCE.json`.
- Production PostgreSQL physical-replica promotion, external witness loss, network partitions, STONITH/quorum, PITR/physical-backup recovery and real Dispatch/HQ RPO/RTO remain deployment-specific acceptance work.

## Pass 26 — PostgreSQL Physical HA + Dispatch/HQ DR Qualification

Implemented and live-qualified on PostgreSQL 18.6-compatible server binaries:

- physical streaming standby startup/read-only application behavior;
- independent witness requirement for all physical-failover application writers;
- explicit external database-fencing declaration and fail-closed startup contract;
- witness-path and database-path partition fencing;
- Dispatch database primary → HQ physical standby promotion;
- Dispatch application authority → HQ application authority transfer;
- unsafe un-rewound old-primary application fencing;
- lease-epoch-aware topology/split-brain reporting;
- archive-assisted `pg_rewind` return-to-service in both directions;
- planned physical failback to Dispatch;
- physical base backup + WAL archive PITR with included/excluded transaction boundary;
- passive HA heartbeat/realtime behavior on physical hot standbys.

Qualification evidence: `docs/ops/evidence/PASS-26-POSTGRESQL-PHYSICAL-HA-EVIDENCE.json`.

Boundary: DISRoute does not supply or certify a production PostgreSQL STONITH/quorum manager. The live qualification used explicit process fencing; production must provide an external database-fencing implementation appropriate to the deployment platform.


## Pass 27 — Production deployment, security and operations hardening

- Added fail-closed Production startup validation for PostgreSQL/authentication/HTTPS/MFA and preserved the physical-failover independent-witness/external-fencing contract.
- Added external configuration loading plus `secret://env` / `secret://file` resolution for PostgreSQL/witness/provider/LDAP/TLS/MFA secrets, including Unix file-permission enforcement.
- Added direct Kestrel TLS validation, explicit trusted-proxy forwarding, HSTS/security headers, server-header suppression, fixed-window request limiting, sanitized anonymous readiness and production-safe unexpected-error responses.
- Added protected deployment-readiness and upgrade-preflight APIs covering migration integrity, journal drain, HA authority, disk/backup health and ambiguous provider submission state.
- Added hardened Linux `systemd` deployment and native Windows SCM hosting plus Windows install/update/uninstall automation.
- Expanded production publishing to Linux/Windows server, Windows CAD/Station/Operations Center/Mobile harness/LegacyBridge and Linux ARM64 Station Display, with SHA-256 deployment packaging.
- Live Pass 27 Production qualification against PostgreSQL 18.6-compatible infrastructure passed, including direct TLS and reverse-proxy modes; exact evidence is under `docs/ops/evidence`.
- Pass 25 application-HA and Pass 26 physical-HA/PITR destructive regressions both pass on the Pass 27 code.
- Target Windows SCM, Raspberry Pi hardware, native Android/iOS, signed installers, selected production DB STONITH/quorum, vendor conformance, load/penetration/accessibility and agency acceptance remain explicit external qualification boundaries.


## Pass 28 — End-to-End Operational Workflow Qualification

- Added a six-scenario live HTTP operational qualification suite spanning call intake, dispatch, Station acknowledgement, active structure-fire command/mutual-aid/site intelligence/chat/DMS, process restart, Mobile offline reconciliation, corrections/relationships/merge/split, RMS finalization, NERIS degradation/recovery and audit/journal closure.
- Added explicit least-privilege SelfTest assertions for Dispatcher, StationOfficer, RecordsReporting and Administrator role permissions.
- Strengthened application HA smoke so the same dispatched incident crosses authority transfer, receives post-failover activity and closure on the promoted node, remains protected from the drained old writer and retains the complete activity/audit history after authority later cycles back.
- Reran Pass 25 application-HA, Pass 26 physical-HA/PITR and Pass 27 production-deployment/security qualification on the Pass 28 source. All mandatory regressions pass.
- Reran all eight documented publish targets and independently verified the 314-file deployment ZIP against the embedded per-file SHA-256 manifest.
- Explicit remaining boundaries: live vendor conformance, target Windows/Raspberry Pi/native-mobile acceptance, signed distribution, production DB STONITH/quorum product certification, load/soak/penetration/accessibility testing and agency operational acceptance.


## Pass 29 — Load, Resilience, Security & Failure-Stress Qualification

- Added reusable concurrent FileCheckpoint stress/fault qualification and PostgreSQL active-load application-failover qualification.
- Hardened global rate limiting with structured backoff and isolated health probes from client throttling.
- Hardened background-worker cancellation so planned shutdown does not create false operational failure logs or NERIS failure state.
- Reran Pass 25 application HA, Pass 26 physical HA/DR/PITR and Pass 27 production-security regression on the Pass 29 source.
- Preserved 18 projects, 313 API routes and 20 PostgreSQL migrations; Pass 29 is qualification/hardening rather than subsystem expansion.
- Hardened PostgreSQL startup to require UTF8 database encoding; SQL_ASCII is rejected before operational startup, and active-incident HA continuity was requalified on real PostgreSQL 18.6 UTF8.


## Pass 30 — Release candidate / final production readiness

- Versioned the cumulative implementation as `1.0.0-rc.1` with deterministic assembly metadata and an exact .NET 10.0.400 SDK pin.
- Added one NuGet `packages.lock.json` per project and locked-restore qualification across all 18 projects.
- Added CI-only NuGet audit escalation (`NU1901`–`NU1904`) plus GitHub locked restore / Release build / SelfTest / source-verifier workflow.
- Added deterministic `SOURCE_DATE_EPOCH` deployment packaging, byte-for-byte publish-tree comparison, CycloneDX 1.6 SBOM generation and release-candidate manifest generation.
- Added explicit RC acceptance, release checklist, release notes and known-limitations documentation separating implementation qualification from target hardware, signing, vendor, penetration/accessibility and agency production acceptance.
- Pass 30 does not add schema migrations; production schema remains version 20.
