-- Pass 11: correlate mutation journal/outbox rows to an authoritative command durability batch.
-- Outbox publication is blocked until the matching state checkpoint/projection transaction marks
-- the rows authoritative. Existing rows predate the barrier and are therefore backfilled ready.
ALTER TABLE command_journal
  ADD COLUMN IF NOT EXISTS durability_batch_id uuid NULL,
  ADD COLUMN IF NOT EXISTS authoritative_committed_at_utc timestamptz NULL;

ALTER TABLE outbox_events
  ADD COLUMN IF NOT EXISTS durability_batch_id uuid NULL,
  ADD COLUMN IF NOT EXISTS authoritative_committed_at_utc timestamptz NULL;

UPDATE command_journal
SET authoritative_committed_at_utc = COALESCE(authoritative_committed_at_utc, persisted_at_utc)
WHERE authoritative_committed_at_utc IS NULL AND durability_batch_id IS NULL;

UPDATE outbox_events
SET authoritative_committed_at_utc = COALESCE(authoritative_committed_at_utc, published_at_utc, occurred_at_utc)
WHERE authoritative_committed_at_utc IS NULL AND durability_batch_id IS NULL;

CREATE INDEX IF NOT EXISTS ix_outbox_authoritative_unpublished
  ON outbox_events(occurred_at_utc)
  WHERE published_at_utc IS NULL AND authoritative_committed_at_utc IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_outbox_uncommitted_batch
  ON outbox_events(durability_batch_id)
  WHERE authoritative_committed_at_utc IS NULL AND durability_batch_id IS NOT NULL;

UPDATE schema_info SET version = 13, applied_at_utc = now() WHERE singleton = true;
