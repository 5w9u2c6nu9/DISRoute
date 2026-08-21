BEGIN;


CREATE TABLE IF NOT EXISTS agencies (
  id uuid PRIMARY KEY,
  code text NOT NULL UNIQUE,
  name text NOT NULL,
  created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS stations (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  code text NOT NULL,
  name text NOT NULL,
  address text NOT NULL,
  UNIQUE(agency_id, code)
);

CREATE TABLE IF NOT EXISTS incidents (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  station_id uuid NOT NULL REFERENCES stations(id),
  display_number text NOT NULL,
  nature text NOT NULL,
  priority text NOT NULL,
  address text NOT NULL,
  cross_streets text NOT NULL DEFAULT '',
  municipality text NOT NULL DEFAULT '',
  caller_name text NOT NULL DEFAULT '',
  caller_phone text NOT NULL DEFAULT '',
  notes text NOT NULL DEFAULT '',
  lifecycle_state text NOT NULL,
  version bigint NOT NULL DEFAULT 1,
  received_at_utc timestamptz NOT NULL,
  updated_at_utc timestamptz NOT NULL,
  closed_at_utc timestamptz NULL,
  UNIQUE(agency_id, display_number)
);
CREATE INDEX IF NOT EXISTS ix_incidents_agency_state ON incidents(agency_id, lifecycle_state, received_at_utc DESC);

CREATE TABLE IF NOT EXISTS operational_units (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  station_id uuid NOT NULL REFERENCES stations(id),
  call_sign text NOT NULL,
  discipline text NOT NULL,
  unit_type text NOT NULL,
  status text NOT NULL,
  incident_id uuid NULL REFERENCES incidents(id),
  updated_at_utc timestamptz NOT NULL,
  UNIQUE(agency_id, call_sign)
);

CREATE TABLE IF NOT EXISTS call_records (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  source text NOT NULL,
  provider_call_id text NOT NULL,
  caller_name text NOT NULL DEFAULT '',
  callback_number text NOT NULL DEFAULT '',
  supplied_location text NOT NULL DEFAULT '',
  nature text NOT NULL DEFAULT '',
  disposition text NOT NULL,
  incident_id uuid NULL REFERENCES incidents(id),
  received_at_utc timestamptz NOT NULL,
  answered_at_utc timestamptz NULL,
  ended_at_utc timestamptz NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_call_provider_id ON call_records(agency_id, source, provider_call_id) WHERE provider_call_id <> '';

CREATE TABLE IF NOT EXISTS incident_activity (
  id uuid PRIMARY KEY,
  incident_id uuid NOT NULL REFERENCES incidents(id),
  category text NOT NULL,
  message text NOT NULL,
  actor text NOT NULL,
  occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_activity_incident_time ON incident_activity(incident_id, occurred_at_utc);

CREATE TABLE IF NOT EXISTS dispatch_actions (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  incident_id uuid NOT NULL REFERENCES incidents(id),
  state text NOT NULL,
  committed_at_utc timestamptz NOT NULL,
  actor text NOT NULL
);
CREATE TABLE IF NOT EXISTS dispatch_action_units (
  dispatch_action_id uuid NOT NULL REFERENCES dispatch_actions(id),
  unit_id uuid NOT NULL REFERENCES operational_units(id),
  PRIMARY KEY(dispatch_action_id, unit_id)
);
CREATE TABLE IF NOT EXISTS dispatch_deliveries (
  id uuid PRIMARY KEY,
  dispatch_action_id uuid NOT NULL REFERENCES dispatch_actions(id),
  provider text NOT NULL,
  destination text NOT NULL,
  state text NOT NULL,
  attempts integer NOT NULL DEFAULT 0,
  last_error text NULL,
  updated_at_utc timestamptz NOT NULL,
  UNIQUE(dispatch_action_id, provider, destination)
);

CREATE TABLE IF NOT EXISTS premises (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  name text NOT NULL,
  address text NOT NULL,
  occupancy text NOT NULL DEFAULT '',
  access_information text NOT NULL DEFAULT '',
  fire_protection text NOT NULL DEFAULT '',
  utility_shutoffs text NOT NULL DEFAULT '',
  hazards text NOT NULL DEFAULT '',
  classification text NOT NULL DEFAULT 'Operational',
  updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_premises_agency_address ON premises(agency_id, address);

CREATE TABLE IF NOT EXISTS preplans (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  premise_id uuid NOT NULL REFERENCES premises(id),
  version integer NOT NULL,
  status text NOT NULL,
  title text NOT NULL,
  operational_content text NOT NULL,
  author text NOT NULL,
  effective_at_utc timestamptz NOT NULL,
  updated_at_utc timestamptz NOT NULL,
  UNIQUE(premise_id, version)
);

CREATE TABLE IF NOT EXISTS crg_assets (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  premise_id uuid NOT NULL REFERENCES premises(id),
  file_name text NOT NULL,
  sha256 text NOT NULL,
  floor text NULL,
  overlay_type text NULL,
  revision integer NULL,
  rotation double precision NULL,
  scale double precision NULL,
  opacity double precision NULL,
  source text NOT NULL,
  uploaded_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS hydrants (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  asset_id text NOT NULL,
  address text NOT NULL,
  latitude double precision NOT NULL,
  longitude double precision NOT NULL,
  main_size_inches integer NULL,
  static_psi integer NULL,
  residual_psi integer NULL,
  flow_gpm integer NULL,
  status text NOT NULL,
  updated_at_utc timestamptz NOT NULL,
  UNIQUE(agency_id, asset_id)
);

CREATE TABLE IF NOT EXISTS map_objects (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  incident_id uuid NULL REFERENCES incidents(id),
  kind text NOT NULL,
  name text NOT NULL,
  geometry_geojson jsonb NOT NULL,
  visibility_scope text NOT NULL,
  starts_at_utc timestamptz NOT NULL,
  ends_at_utc timestamptz NULL,
  actor text NOT NULL,
  version bigint NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS messages (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  incident_id uuid NULL REFERENCES incidents(id),
  channel text NOT NULL,
  sender text NOT NULL,
  body text NOT NULL,
  sent_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS alerts (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  incident_id uuid NULL REFERENCES incidents(id),
  type text NOT NULL,
  severity text NOT NULL,
  correlation_key text NOT NULL,
  affected_entity text NOT NULL,
  state text NOT NULL,
  summary text NOT NULL,
  claimed_by text NULL,
  created_at_utc timestamptz NOT NULL,
  updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_alerts_open ON alerts(agency_id, state, created_at_utc DESC);

CREATE TABLE IF NOT EXISTS workforce_shifts (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  work_date date NOT NULL,
  shift_name text NOT NULL,
  station_id uuid NOT NULL REFERENCES stations(id),
  position text NOT NULL,
  employee_name text NOT NULL,
  qualification text NOT NULL,
  status text NOT NULL
);

CREATE TABLE IF NOT EXISTS documents (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  title text NOT NULL,
  classification text NOT NULL,
  current_version integer NOT NULL,
  status text NOT NULL,
  created_by text NOT NULL,
  created_at_utc timestamptz NOT NULL,
  updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS document_versions (
  id uuid PRIMARY KEY,
  document_id uuid NOT NULL REFERENCES documents(id),
  version integer NOT NULL,
  content text NOT NULL,
  status text NOT NULL,
  author text NOT NULL,
  created_at_utc timestamptz NOT NULL,
  UNIQUE(document_id, version)
);

CREATE TABLE IF NOT EXISTS rms_records (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL REFERENCES agencies(id),
  record_type text NOT NULL,
  display_number text NOT NULL,
  title text NOT NULL,
  status text NOT NULL,
  summary text NOT NULL,
  incident_id uuid NULL REFERENCES incidents(id),
  created_at_utc timestamptz NOT NULL,
  updated_at_utc timestamptz NOT NULL,
  UNIQUE(agency_id, display_number)
);

CREATE TABLE IF NOT EXISTS audit_records (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL,
  actor text NOT NULL,
  action text NOT NULL,
  entity_type text NOT NULL,
  entity_id text NOT NULL,
  summary text NOT NULL,
  source text NOT NULL,
  occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_audit_agency_time ON audit_records(agency_id, occurred_at_utc DESC);

CREATE TABLE IF NOT EXISTS idempotency_records (
  operation_key text PRIMARY KEY,
  operation_kind text NOT NULL,
  result_json jsonb NOT NULL,
  created_at_utc timestamptz NOT NULL,
  expires_at_utc timestamptz NULL
);

CREATE TABLE IF NOT EXISTS outbox_events (
  id uuid PRIMARY KEY,
  agency_id uuid NOT NULL,
  aggregate_type text NOT NULL,
  aggregate_id text NOT NULL,
  event_type text NOT NULL,
  payload jsonb NOT NULL,
  occurred_at_utc timestamptz NOT NULL,
  published_at_utc timestamptz NULL,
  attempts integer NOT NULL DEFAULT 0,
  last_error text NULL
);
CREATE INDEX IF NOT EXISTS ix_outbox_unpublished ON outbox_events(occurred_at_utc) WHERE published_at_utc IS NULL;

CREATE TABLE IF NOT EXISTS schema_info (
  singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton),
  version integer NOT NULL,
  applied_at_utc timestamptz NOT NULL
);
INSERT INTO schema_info(singleton, version, applied_at_utc) VALUES (true, 1, now())
ON CONFLICT(singleton) DO UPDATE SET version = EXCLUDED.version, applied_at_utc = EXCLUDED.applied_at_utc;

COMMIT;
