-- DISRoute schema 018: internal chat / communications depth
CREATE TABLE IF NOT EXISTS chat_channels (
    id uuid PRIMARY KEY,
    owner_agency_id uuid NOT NULL REFERENCES agencies(id),
    type text NOT NULL,
    name text NOT NULL,
    topic text NOT NULL,
    station_id uuid NULL REFERENCES stations(id),
    incident_id uuid NULL REFERENCES incidents(id),
    participating_agencies_json jsonb NOT NULL,
    member_users_json jsonb NOT NULL,
    allowed_roles_json jsonb NOT NULL,
    archived boolean NOT NULL DEFAULT false,
    created_by text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_chat_channels_owner ON chat_channels(owner_agency_id, archived, updated_at_utc DESC);

CREATE TABLE IF NOT EXISTS chat_messages (
    id uuid PRIMARY KEY,
    channel_id uuid NOT NULL REFERENCES chat_channels(id) ON DELETE CASCADE,
    owner_agency_id uuid NOT NULL REFERENCES agencies(id),
    sender text NOT NULL,
    body text NOT NULL,
    priority text NOT NULL,
    classification text NOT NULL,
    linked_incident_id uuid NULL REFERENCES incidents(id),
    mentions_json jsonb NOT NULL,
    attachments_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_chat_messages_channel_time ON chat_messages(channel_id, created_at_utc DESC);
CREATE INDEX IF NOT EXISTS ix_chat_messages_incident ON chat_messages(linked_incident_id) WHERE linked_incident_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS chat_read_states (
    channel_id uuid NOT NULL REFERENCES chat_channels(id) ON DELETE CASCADE,
    user_id text NOT NULL,
    last_read_message_id uuid NULL REFERENCES chat_messages(id),
    last_read_at_utc timestamptz NULL,
    PRIMARY KEY(channel_id, user_id)
);

UPDATE schema_info SET version = 18, applied_at_utc = now() WHERE singleton = true;
