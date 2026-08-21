# DISRoute Pass 22 — Internal Chat / Communications

Date: 2026-08-20
Base checkpoint: Pass 21
Base SHA-256: `f7bc09a000c5c608802f6b722e6607879f4eccf43b037585109185af0468d1b1`

## Implemented

- New persisted general internal-chat subsystem while preserving Pass 13 incident-scoped operational messaging and promotion-to-narrative behavior.
- Channel types: Direct, Station, Group, Supervisor, Agency and MutualAid.
- Explicit Direct and MutualAid membership, participating-agency lists for MutualAid, optional station/incident context and role-based access for applicable non-direct channel types.
- Immutable chat messages with priority, classification, optional incident link, mentions and external attachment references (file name/type/storage reference/classification/optional SHA-256).
- Per-user read cursors and computed unread counts.
- Search across only channels accessible to the authenticated actor.
- Archive/restore lifecycle; archived channels reject new messages.
- Realtime user groups, authorization-checked channel groups and mention notifications.
- Operations Center Chat / Communications workspace for channel creation/history, send/read/archive, presence, search, incident links, mentions and attachment references.
- State schema 18 and PostgreSQL migration `018_internal_chat.sql`.
- Runtime smoke coverage proving chat channel/message/read state survive real process restart and staged backup restore.

## Verification

- Full .NET 10 Release build: PASS — 17 projects, 0 warnings, 0 errors.
- SelfTest: PASS — incidents=7, units=6, audit=122, alerts=2.
- Source verifier before packaging: PASS — 110 C# files, 17 projects, 295 API routes, 18 migrations.
- Runtime persistence/restart/backup/staged-restore/replay smoke: PASS, including Pass 22 chat durability.
- Avalonia startup smoke: PASS — CAD, Station, Operations Center, Station Display.
- Live PostgreSQL target execution: not run in this artifact environment.

## Safety / scope boundaries

- MutualAid/cross-agency channels require explicit participating agencies and explicit members; there is no implicit agency-wide cross-agency room exposure.
- Chat attachment support is reference/metadata based; DISRoute does not claim to provide binary object storage in this pass.
- Realtime presence is intentionally ephemeral; message/channel/read state is durable.
- No claim is made for external Teams/Slack federation, mobile push delivery, legal-hold/retention certification or production directory-driven dynamic channel membership yet.
