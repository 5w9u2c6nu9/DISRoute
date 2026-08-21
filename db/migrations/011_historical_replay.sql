BEGIN;

CREATE TABLE IF NOT EXISTS incident_state_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    incident_id uuid NOT NULL,
    version bigint NOT NULL,
    cause text NOT NULL,
    actor text NOT NULL,
    state_json jsonb NOT NULL,
    recorded_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_incident_state_history_version ON incident_state_history(incident_id, version);
CREATE INDEX IF NOT EXISTS ix_incident_state_history_time ON incident_state_history(incident_id, recorded_at_utc);

CREATE TABLE IF NOT EXISTS unit_state_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    unit_id uuid NOT NULL,
    incident_id uuid NULL,
    cause text NOT NULL,
    actor text NOT NULL,
    state_json jsonb NOT NULL,
    recorded_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_unit_state_history_time ON unit_state_history(unit_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS ix_unit_state_history_incident_time ON unit_state_history(incident_id, recorded_at_utc) WHERE incident_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS dispatch_action_state_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    dispatch_action_id uuid NOT NULL,
    incident_id uuid NOT NULL,
    cause text NOT NULL,
    actor text NOT NULL,
    state_json jsonb NOT NULL,
    recorded_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_dispatch_action_history_incident_time ON dispatch_action_state_history(incident_id, recorded_at_utc);

CREATE TABLE IF NOT EXISTS dispatch_delivery_state_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    delivery_id uuid NOT NULL,
    incident_id uuid NOT NULL,
    cause text NOT NULL,
    actor text NOT NULL,
    state_json jsonb NOT NULL,
    recorded_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_dispatch_delivery_history_incident_time ON dispatch_delivery_state_history(incident_id, recorded_at_utc);

CREATE TABLE IF NOT EXISTS station_delivery_state_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    delivery_id uuid NOT NULL,
    incident_id uuid NOT NULL,
    cause text NOT NULL,
    actor text NOT NULL,
    state_json jsonb NOT NULL,
    recorded_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_station_delivery_history_incident_time ON station_delivery_state_history(incident_id, recorded_at_utc);

CREATE TABLE IF NOT EXISTS alert_state_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    alert_id uuid NOT NULL,
    incident_id uuid NULL,
    cause text NOT NULL,
    actor text NOT NULL,
    state_json jsonb NOT NULL,
    recorded_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_alert_state_history_incident_time ON alert_state_history(incident_id, recorded_at_utc) WHERE incident_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS vessel_observation_history (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    mmsi text NOT NULL,
    observed_at_utc timestamptz NOT NULL,
    state_json jsonb NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_vessel_observation_history_time ON vessel_observation_history(agency_id, observed_at_utc);
CREATE INDEX IF NOT EXISTS ix_vessel_observation_history_mmsi_time ON vessel_observation_history(mmsi, observed_at_utc);

UPDATE schema_info SET version = 11, applied_at_utc = now() WHERE singleton = true;
COMMIT;
