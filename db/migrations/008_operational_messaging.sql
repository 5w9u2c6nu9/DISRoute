BEGIN;
CREATE TABLE IF NOT EXISTS operational_messages (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL REFERENCES agencies(id),
    incident_id uuid NULL REFERENCES incidents(id),
    channel text NOT NULL,
    sender text NOT NULL,
    recipient text NULL,
    priority text NOT NULL,
    classification text NOT NULL,
    body text NOT NULL,
    reference_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    state text NOT NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_operational_messages_incident_time ON operational_messages(incident_id, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_operational_messages_channel_time ON operational_messages(agency_id, channel, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS message_receipts (
    id uuid PRIMARY KEY,
    message_id uuid NOT NULL REFERENCES operational_messages(id) ON DELETE CASCADE,
    recipient text NOT NULL,
    device_id text NULL,
    state text NOT NULL,
    queued_at_utc timestamptz NOT NULL,
    delivered_at_utc timestamptz NULL,
    read_at_utc timestamptz NULL,
    acknowledged_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_message_receipts_recipient_state ON message_receipts(recipient, state, queued_at_utc);
UPDATE schema_info SET version = 8, applied_at_utc = now() WHERE singleton = true;
COMMIT;
