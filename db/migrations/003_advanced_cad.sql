BEGIN;
CREATE TABLE IF NOT EXISTS additional_alarms (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  alarm_number integer NOT NULL, dispatch_action_id uuid NOT NULL REFERENCES dispatch_actions(id),
  unit_ids_json jsonb NOT NULL, actor text NOT NULL, committed_at_utc timestamptz NOT NULL,
  UNIQUE(incident_id, alarm_number)
);
CREATE TABLE IF NOT EXISTS avl_observations (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), unit_id uuid NOT NULL REFERENCES operational_units(id),
  incident_id uuid NULL REFERENCES incidents(id), latitude double precision NOT NULL, longitude double precision NOT NULL,
  speed_kph double precision NOT NULL, incident_distance_meters double precision NULL, source text NOT NULL, observed_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_avl_unit_time ON avl_observations(unit_id, observed_at_utc DESC);
CREATE TABLE IF NOT EXISTS avl_advisories (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), unit_id uuid NOT NULL REFERENCES operational_units(id),
  incident_id uuid NULL REFERENCES incidents(id), severity text NOT NULL, kind text NOT NULL, summary text NOT NULL,
  suggested_status text NOT NULL, state text NOT NULL, created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS incident_corrections (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  field_name text NOT NULL, original_value text NOT NULL, corrected_value text NOT NULL, reason text NOT NULL,
  actor text NOT NULL, corrected_at_utc timestamptz NOT NULL, incident_version bigint NOT NULL
);
UPDATE schema_info SET version = 3, applied_at_utc = now() WHERE singleton = true;
COMMIT;
