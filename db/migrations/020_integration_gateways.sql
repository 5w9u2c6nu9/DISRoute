-- Pass 24: durable integration/gateway normalized event and provider operation boundary.
CREATE TABLE IF NOT EXISTS integration_events (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL,
  provider_key text NOT NULL,
  category text NOT NULL,
  native_event_id text NOT NULL,
  event_type text NOT NULL,
  state text NOT NULL,
  incident_id uuid NULL,
  unit_id uuid NULL,
  resource_key text NULL,
  source_timestamp timestamptz NOT NULL,
  received_at timestamptz NOT NULL,
  payload_sha256 text NOT NULL,
  payload_json jsonb NOT NULL,
  correlation_key text NOT NULL UNIQUE,
  error text NULL
);
CREATE INDEX IF NOT EXISTS ix_integration_events_agency_received ON integration_events(agency_id, received_at DESC);
CREATE INDEX IF NOT EXISTS ix_integration_events_incident ON integration_events(incident_id, received_at DESC) WHERE incident_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS integration_operations (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL,
  provider_key text NOT NULL,
  kind text NOT NULL,
  state text NOT NULL,
  incident_id uuid NULL,
  unit_id uuid NULL,
  request_json jsonb NOT NULL,
  request_sha256 text NOT NULL,
  attempt_count integer NOT NULL,
  provider_reference text NULL,
  error text NULL,
  possible_duplicate boolean NOT NULL DEFAULT false,
  created_at timestamptz NOT NULL,
  updated_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_integration_operations_agency_updated ON integration_operations(agency_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS integration_attempts (
  id uuid PRIMARY KEY,
  operation_id uuid NOT NULL REFERENCES integration_operations(id),
  attempt_number integer NOT NULL,
  provider_operation_id uuid NOT NULL,
  state text NOT NULL,
  provider_reference text NULL,
  error text NULL,
  started_at timestamptz NOT NULL,
  completed_at timestamptz NULL,
  UNIQUE(operation_id, attempt_number)
);

CREATE TABLE IF NOT EXISTS recording_references (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL,
  incident_id uuid NULL,
  provider_key text NOT NULL,
  provider_reference text NOT NULL,
  media_type text NOT NULL,
  start_utc timestamptz NOT NULL,
  end_utc timestamptz NOT NULL,
  description text NOT NULL,
  playback_uri text NULL,
  linked_at_utc timestamptz NOT NULL,
  UNIQUE(provider_key, provider_reference)
);

UPDATE schema_info SET version = 20, applied_at_utc = now() WHERE singleton = true;
