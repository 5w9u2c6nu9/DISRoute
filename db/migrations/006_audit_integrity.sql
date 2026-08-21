BEGIN;
CREATE TABLE IF NOT EXISTS audit_integrity (
    audit_record_id uuid PRIMARY KEY,
    sequence bigint NOT NULL UNIQUE,
    previous_hash text NOT NULL,
    hash text NOT NULL,
    sealed_at_utc timestamptz NOT NULL
);
UPDATE schema_info SET version = 6, applied_at_utc = now() WHERE singleton = true;
COMMIT;
