# DISRoute Pass 22.1 — Internal Chat Authorization / Realtime Hardening

Date: 2026-08-20
Base checkpoint: Pass 22
Base SHA-256: `b28aeb6ade6fdbbbbd03d95e6eea6e8de580f079028cec84e6ccf4f5713b37e7`

## Why this hardening pass exists

A post-package audit of Pass 22 confirmed the chat implementation was present and reproducible, but identified four areas that were weaker than the feature summary implied: the Operations Center conversation was not yet consuming SignalR events live, presence was filtered client-side, unknown actors inherited the default agency, and Station/Group channel types did not yet resolve their operational memberships dynamically. Pass 22.1 closes those gaps and also removes adjacent agency-spoofing paths found while doing the audit.

## Implemented

- Added `OperationsRealtimeClient`, a reconnecting SignalR JSON/WebSocket client in `DISRoute.Client.Common` using the existing operations hub protocol without a new external package dependency.
- Operations Center now joins the signed-in agency and selected authorized `chat:{channel}` group, re-joins after reconnect and handles live `chatMessage`, `chatChannelChanged`, `chatReadState`, `presenceChanged` and `presenceLeft` events.
- Initial history/search remain API-backed durable reads; live inbound chat is appended from the SignalR event rather than polling.
- `/api/presence` is server-filtered to the signed-in actor's agency.
- Channel-list/search agency IDs are checked against the actor's server-resolved agency.
- SignalR `JoinAgency` and `SetPresence` reject a requested agency that does not match the signed-in user's provisioned agency.
- `ResolveActorAgency` now fails closed for unknown, disabled or revoked users. The previous default-agency fallback was removed.
- Station chat membership is derived dynamically from a currently effective, filled Workforce assignment at the channel's station.
- Group chat channels bind to a concrete incident operational-group ID. Operational groups now persist explicit provisioned member-user IDs; access follows the active group leader/member list.
- Added an audited operational-group membership update route/client method and simple CAD member-entry support at group creation.
- Direct and MutualAid channels remain explicit-membership only; MutualAid requires at least two participating agencies with at least one explicit member representing each.
- Chat attachment SHA-256 metadata, when supplied, must be exactly 64 hexadecimal characters.
- Added PostgreSQL migration `019_chat_membership_hardening.sql`; state schema is now 19.

## Regression coverage

SelfTest now proves:

- provisioned actor agency resolution and unknown-actor failure;
- unknown actors cannot inherit default-agency chat access;
- Station channel access appears with an effective Workforce assignment and disappears after call-out;
- Group channel access follows operational-group membership and disappears when membership is removed;
- Direct-message membership remains exactly two provisioned users;
- invalid attachment SHA-256 metadata is rejected;
- foreign client-supplied agency channel queries are rejected;
- explicit MutualAid cross-agency access works while nonmembers remain excluded;
- archive/read-only, unread/read, mentions, incident links, attachments and scoped search remain green;
- new operational-group membership/channel binding survives portable state export/import.

Runtime smoke additionally performs a real SignalR negotiate and WebSocket connection, joins the authorized agency and chat groups, publishes presence, posts a chat message through HTTP and asserts the exact message is received as a live `chatMessage` hub event. The same chat state then survives actual process restart and staged backup restore.

## Verification

- Full .NET 10 Release build: PASS — 17 projects, 0 warnings, 0 errors.
- SelfTest: PASS — `incidents=7, units=6, audit=128, alerts=2`.
- Source verifier: PASS — 111 C# files, 17 projects, 296 API routes, 19 migrations.
- Runtime persistence/restart/backup/staged-restore/replay smoke: PASS, including real SignalR live chat delivery, server-scoped presence and foreign-agency query rejection.
- Avalonia startup smoke: PASS — CAD, Station, Operations Center, Station Display.
- Live PostgreSQL target execution: not run in this artifact environment.

## Remaining boundaries

- AD/Entra/directory groups are not yet automatically expanded into operational-group or chat membership.
- Attachment handling is still reference/metadata based; binary object storage remains future work.
- Mobile push/offline notification delivery, retention/legal-hold enforcement and external Teams/Slack-style federation remain future work.
- Live PostgreSQL HA/DR infrastructure qualification is still required on target deployment infrastructure.
