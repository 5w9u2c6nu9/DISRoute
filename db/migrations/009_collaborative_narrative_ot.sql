BEGIN;
CREATE TABLE IF NOT EXISTS collaborative_narrative_operations (
    id uuid PRIMARY KEY,
    incident_id uuid NOT NULL REFERENCES incidents(id) ON DELETE CASCADE,
    client_base_version bigint NOT NULL,
    applied_base_version bigint NOT NULL,
    result_version bigint NOT NULL,
    start_position integer NOT NULL,
    delete_count integer NOT NULL,
    insert_text text NOT NULL,
    actor text NOT NULL,
    rebased boolean NOT NULL DEFAULT false,
    applied_at_utc timestamptz NOT NULL,
    UNIQUE(incident_id, result_version)
);
CREATE INDEX IF NOT EXISTS ix_collaborative_narrative_operations_incident_version
    ON collaborative_narrative_operations(incident_id, result_version);
UPDATE schema_info SET version = 9, applied_at_utc = now() WHERE singleton = true;
COMMIT;
