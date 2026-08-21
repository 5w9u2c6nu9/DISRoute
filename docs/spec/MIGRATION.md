# Legacy Data Migration Plan

## Goal

Migrate historical/operational source data into canonical DISRoute models without carrying legacy runtime dependencies into the modern production platform.

## Neutral architecture

Use a Windows-only, isolated read-only `DISRoute.LegacyBridge` when needed to access historical ASNA/DataGate COM/ActiveX APIs or a cloned source database. Modern DISRoute runtime does not depend on those components.

## Source safety

- Never test extraction against the sole/live source copy.
- Use a dedicated read-only migration account.
- Keep credentials in protected secret storage.
- Prefer clone/staging environment for discovery and rehearsal.
- Do not assume historical laptop/local profile names represent production.
- Confirm actual current Windows-server source profile/config before final connector work.

## Pipeline

1. Connect/read-only capability test.
2. Schema/catalog inventory.
3. Extract to staging with raw provenance.
4. Map to canonical DISRoute entities.
5. Validate field/type/code translations.
6. Dry run.
7. Manual review of warnings/ambiguous mappings.
8. Import with deterministic idempotency keys.
9. Reconciliation counts/checksums/spot checks.
10. Cutover rehearsal.
11. Final migration/cutover.
12. Immutable migration report.

## Provenance

Preserve source system/profile/database, logical file/member/table, source key/RRN where useful, original values where required, migration rule version, import batch, timestamp, warning/manual-resolution record.

## Rerun/resume

Migration must resume/re-run without duplicating records. Mapping rule versions and import batches remain traceable.

## Reporting translation

Historical fire/report records may require explicit source -> canonical -> current-provider mappings. Do not silently infer a value when no defensible mapping exists; create a warning/manual-review state.
