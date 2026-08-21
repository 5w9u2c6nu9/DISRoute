-- Pass 10: make command idempotency receipts durable and payload-bound across restart/failover.
ALTER TABLE idempotency_records
  ADD COLUMN IF NOT EXISTS request_sha256 text NULL,
  ADD COLUMN IF NOT EXISTS result_type text NULL;

CREATE INDEX IF NOT EXISTS ix_idempotency_records_created
  ON idempotency_records(created_at_utc DESC);

UPDATE schema_info SET version = 12, applied_at_utc = now() WHERE singleton = true;
