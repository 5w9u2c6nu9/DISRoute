BEGIN;

CREATE TABLE IF NOT EXISTS command_journal (
    sequence bigserial PRIMARY KEY,
    event_id uuid NOT NULL UNIQUE,
    agency_id uuid NOT NULL,
    aggregate_type text NOT NULL,
    aggregate_id text NOT NULL,
    event_type text NOT NULL,
    actor text NOT NULL,
    summary text NOT NULL,
    payload_json jsonb NOT NULL,
    occurred_at_utc timestamptz NOT NULL,
    persisted_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_command_journal_agency_sequence ON command_journal(agency_id, sequence);
CREATE INDEX IF NOT EXISTS ix_command_journal_aggregate ON command_journal(aggregate_type, aggregate_id, sequence);

CREATE TABLE IF NOT EXISTS server_nodes (
    node_id text PRIMARY KEY,
    instance_id uuid NOT NULL,
    role text NOT NULL,
    state text NOT NULL,
    base_address text NOT NULL DEFAULT '',
    started_at_utc timestamptz NOT NULL,
    last_heartbeat_utc timestamptz NOT NULL,
    accepting_writes boolean NOT NULL DEFAULT false,
    version text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS ix_server_nodes_heartbeat ON server_nodes(last_heartbeat_utc DESC);

CREATE TABLE IF NOT EXISTS cluster_events (
    id uuid PRIMARY KEY,
    node_id text NOT NULL,
    event_type text NOT NULL,
    details_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_cluster_events_time ON cluster_events(occurred_at_utc DESC);

UPDATE schema_info SET version = 7, applied_at_utc = now() WHERE singleton = true;
COMMIT;
