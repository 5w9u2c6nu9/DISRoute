BEGIN;

ALTER TABLE server_nodes ADD COLUMN IF NOT EXISTS site text NOT NULL DEFAULT 'Dispatch';
ALTER TABLE server_nodes ADD COLUMN IF NOT EXISTS configured_role text NOT NULL DEFAULT 'LocalStandby';
ALTER TABLE server_nodes ADD COLUMN IF NOT EXISTS lease_epoch bigint NOT NULL DEFAULT 0;
ALTER TABLE server_nodes ADD COLUMN IF NOT EXISTS drained_until_utc timestamptz NULL;
ALTER TABLE server_nodes ADD COLUMN IF NOT EXISTS last_error text NULL;

CREATE INDEX IF NOT EXISTS ix_server_nodes_site_heartbeat ON server_nodes(site, last_heartbeat_utc DESC);
CREATE INDEX IF NOT EXISTS ix_server_nodes_writer_state ON server_nodes(accepting_writes, last_heartbeat_utc DESC);

CREATE TABLE IF NOT EXISTS ha_cluster_state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    lease_epoch bigint NOT NULL DEFAULT 0,
    active_node_id text NOT NULL DEFAULT '',
    active_instance_id uuid NULL,
    active_site text NOT NULL DEFAULT '',
    acquired_at_utc timestamptz NULL,
    last_transition_utc timestamptz NULL,
    transition_reason text NOT NULL DEFAULT ''
);

INSERT INTO ha_cluster_state(singleton)
VALUES (true)
ON CONFLICT(singleton) DO NOTHING;

UPDATE schema_info SET version = 10, applied_at_utc = now() WHERE singleton = true;
COMMIT;
