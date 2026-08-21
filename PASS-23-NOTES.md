# Pass 23 — Mobile / Offline Operations Foundation

Pass 23 branches from the verified cumulative Pass 22.1 checkpoint and adds a real touch-first field-client foundation while preserving the Windows-first operational workstation baseline. The new Mobile project is a shared Avalonia field UI with a desktop-hosted harness so the implementation can be compiled and smoke-tested in this artifact environment. Native Android/iOS packaging, MDM enrollment and OS secure-store qualification are intentionally not claimed complete in this pass.

## Mobile operational package

- `/api/mobile/sync` is no longer an agency-wide dump. It resolves the signed-in actor and registered device, then builds an assignment-scoped field package.
- Devices must be active and registered with the `Mobile` or `MDT` role.
- Normal field users only receive units from their current effective Workforce assignments. Dispatcher/Supervisor/Administrator roles retain their broader authorized agency-unit scope.
- The selected unit drives the current incident package: crew/accountability, site intelligence, active Maydays, operational messages, accessible internal chat, authorized radio streams and relevant published DMS documents.
- Site intelligence reuses the authoritative CAD/preplan package: published preplans, CRG assets/floors, hydrants/water, road restrictions and scene coordinates/warnings.
- Relevant DMS content is limited to published incident-linked documents plus the signed-in user's required-read documents.
- Radio streams are authorization-filtered and remain listen-only; no transmit/PTT control exists in the Mobile client.

## Offline state and reconciliation

- Added `MobileCacheStore` for an encrypted operational snapshot and expanded the existing encrypted `OfflineOperationStore`.
- Mobile visibly distinguishes `Online`, `Offline` and `Stale` state and retains the last encrypted read-only package when the server cannot be reached.
- Realtime refresh is read-only: SignalR incident/unit/Mayday/operational-message/chat events refresh the cached package but never silently commit queued writes.
- Queued writes are reconciled only when the operator explicitly presses **SYNC**.
- Approved offline operation kinds are Narrative, operational Message, ChatMessage and UnitStatus. Unknown operation kinds return `RequiresOnline`.
- Narrative/message/chat operations are now actually applied to their authoritative stores; the earlier thin Mobile skeleton only acknowledged some queued envelopes.
- Offline unit-status changes require a signed short-lived offline authorization lease scoped to the current user, device, agency and `unit.change-status` permission.
- Unit-status reconciliation compares both the observed source status and exact `UpdatedAtUtc` against authoritative state. A stale queued status returns `Conflict` rather than overwriting newer state.
- Accepted/duplicate operations leave the encrypted queue. Rejected/conflicting operations remain visible for review and can be explicitly discarded without performing an authoritative write.
- Mobile reconciliation-envelope idempotency and the underlying authoritative command use separate durable keys (`mobile-reconcile:*` and `mobile-command:*`), preventing command-identity collisions while retaining duplicate-replay safety.
- Nested queued payload JSON uses the same case-insensitive web semantics as the API, avoiding a client wire-format dependency on C# property casing.

## Touch-first field client

Added the 18th solution project, `DISRoute.Mobile`:

- assigned-unit selector and explicit Online/Offline/Stale/queued-state header;
- current incident identity/address/details;
- large one-tap EnRoute, OnScene, Clearing, Available, Command and Code55 controls;
- current crew/accountability and active Mayday display;
- incident operational messages and queued narrative notes;
- explicit offline queue/conflict review with discard action;
- preplan, CRG floor, hydrant and road-restriction scene package;
- internal chat channels/history with online send or explicit offline queue;
- cached published DMS document content;
- authorized listen-only Icecast feed list with no PTT surface;
- realtime incident/unit/Mayday/message/chat refresh through the shared SignalR client.

The current field UI consumes authoritative scene coordinates/layers but does not yet embed the final production map renderer. The radio action launches the authorized stream through the host handler; an in-app mobile audio engine remains a production packaging task.

## Security / storage boundary

- Server agency and user scope remains authoritative; a request cannot select another agency or another field unit outside current authorization.
- Device revocation immediately blocks new Mobile sync and causes offline-lease verification to fail.
- The AES-GCM queue/cache key can be supplied by deployment configuration. The desktop development harness can create a per-user local key file for testability; this is not a claim of Android Keystore/iOS Keychain/Windows hardware-backed key protection.
- Production mobile packaging must bind cache keys/tokens to the target OS secure store, apply agency MDM/root/jailbreak policy, and validate lost-device remote/session revocation on real devices.

## Regression coverage

SelfTest proves:

- assignment-scoped unit/incident visibility;
- crew, preplan/CRG, Mayday, DMS and authorized-radio packaging;
- real offline narrative, operational message and chat commits;
- leased unit-status reconciliation;
- duplicate replay suppression across all approved write kinds;
- stale-status conflict rejection;
- no-lease unit-status rejection;
- wrong-unit and wrong-device-role denial;
- encrypted queue and operational snapshot storage;
- explicit queued-item discard;
- Mobile package/idempotency persistence across state export/import.

Runtime smoke additionally performs a real HTTP offline-lease issuance/verification, reconciles queued narrative/chat/status work, proves duplicate replay behavior, then verifies the Mobile incident/package/status after an actual server restart and staged backup restore.

The UI smoke now launches five Avalonia applications: CAD, Station, Operations Center, Station Display and Mobile.

## Verification result

- Full solution Release build: PASS — 18 projects, 0 warnings, 0 errors.
- SelfTest: PASS — `incidents=7, units=6, audit=132, alerts=2`.
- Source verifier: PASS — 116 C# files, 18 projects, 296 API routes, 19 migrations.
- Runtime persistence/restart/backup/restore/replay + Mobile signed-lease regression: PASS.
- Avalonia startup smoke: PASS — CAD, Station, Operations Center, Station Display, Mobile.

No new PostgreSQL migration is introduced in Pass 23: server-authoritative Mobile state uses the existing users/devices, Workforce, operational, DMS/chat and durable-idempotency persistence. Mobile snapshot/queue state is deliberately client-local encrypted data. Schema version therefore remains 19.

## Remaining Mobile production work

- native Android/iOS application packaging/signing/store or enterprise distribution;
- OS/hardware-backed secure cache/token storage and biometric/passkey UX;
- MDM compliance plus root/jailbreak/lost-device policy qualification;
- production mobile map rendering and offline map/tile strategy;
- push notification service and platform background-delivery behavior;
- in-app listen-only audio engine and real-device audio routing/interrupt behavior;
- binary attachment download/cache policy and retention controls;
- bandwidth/battery/long-disconnect/reconnect/conflict field testing;
- live PostgreSQL HA/DR and real field-device acceptance testing.
