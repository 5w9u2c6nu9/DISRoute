BEGIN;
CREATE TABLE IF NOT EXISTS mfa_credentials (
    user_id text PRIMARY KEY,
    encrypted_secret text NOT NULL,
    pending boolean NOT NULL,
    recovery_code_hashes_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS security_events (
    id uuid PRIMARY KEY,
    agency_id uuid NULL,
    user_id text NULL,
    device_id text NULL,
    event_type text NOT NULL,
    details_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_security_events_user_time ON security_events(user_id, occurred_at_utc DESC);
UPDATE schema_info SET version = 5, applied_at_utc = now() WHERE singleton = true;
COMMIT;
