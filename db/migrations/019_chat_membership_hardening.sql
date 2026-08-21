-- DISRoute schema 019: chat identity/membership hardening
ALTER TABLE operational_groups
    ADD COLUMN IF NOT EXISTS member_users_json jsonb NOT NULL DEFAULT '[]'::jsonb;

ALTER TABLE chat_channels
    ADD COLUMN IF NOT EXISTS operational_group_id uuid NULL REFERENCES operational_groups(id);

CREATE INDEX IF NOT EXISTS ix_chat_channels_operational_group
    ON chat_channels(operational_group_id)
    WHERE operational_group_id IS NOT NULL;

UPDATE schema_info SET version = 19, applied_at_utc = now() WHERE singleton = true;
