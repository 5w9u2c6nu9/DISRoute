BEGIN;

CREATE TABLE IF NOT EXISTS collaborative_narratives (
  incident_id uuid PRIMARY KEY REFERENCES incidents(id) ON DELETE CASCADE,
  version bigint NOT NULL,
  body text NOT NULL,
  last_editor text NOT NULL,
  updated_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS fire_watches (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  unit_id uuid NOT NULL REFERENCES operational_units(id), supervisor text NOT NULL, started_at_utc timestamptz NOT NULL,
  expected_end_utc timestamptz NULL, check_interval_minutes integer NOT NULL, status text NOT NULL, notes text NOT NULL
);
CREATE TABLE IF NOT EXISTS fire_watch_checks (
  id uuid PRIMARY KEY, fire_watch_id uuid NOT NULL REFERENCES fire_watches(id) ON DELETE CASCADE,
  actor text NOT NULL, notes text NOT NULL, checked_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS relocations (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), unit_id uuid NOT NULL REFERENCES operational_units(id),
  destination_station_id uuid NOT NULL REFERENCES stations(id), reason text NOT NULL, status text NOT NULL,
  started_at_utc timestamptz NOT NULL, ended_at_utc timestamptz NULL
);
CREATE TABLE IF NOT EXISTS bulletins (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), category text NOT NULL, title text NOT NULL,
  body text NOT NULL, severity text NOT NULL, starts_at_utc timestamptz NOT NULL, ends_at_utc timestamptz NULL, created_by text NOT NULL
);
CREATE TABLE IF NOT EXISTS station_deliveries (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), station_id uuid NOT NULL REFERENCES stations(id),
  incident_id uuid NOT NULL REFERENCES incidents(id), dispatch_action_id uuid NOT NULL REFERENCES dispatch_actions(id),
  display_state text NOT NULL, alert_state text NOT NULL, print_state text NOT NULL, ack_state text NOT NULL,
  attempt integer NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS run_sheets (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), station_id uuid NOT NULL REFERENCES stations(id),
  incident_id uuid NOT NULL REFERENCES incidents(id), revision integer NOT NULL, status text NOT NULL, content text NOT NULL,
  generated_at_utc timestamptz NOT NULL, printed_at_utc timestamptz NULL
);
CREATE TABLE IF NOT EXISTS client_heartbeats (
  device_id text PRIMARY KEY, id uuid NOT NULL, agency_id uuid NOT NULL REFERENCES agencies(id), client_role text NOT NULL,
  platform text NOT NULL, version text NOT NULL, user_name text NOT NULL, state text NOT NULL,
  clock_offset_seconds double precision NULL, last_seen_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS provider_configs (
  provider_key text PRIMARY KEY, display_name text NOT NULL, category text NOT NULL, enabled boolean NOT NULL,
  endpoint text NOT NULL, authentication_mode text NOT NULL, settings_json jsonb NOT NULL, version integer NOT NULL, state text NOT NULL
);
CREATE TABLE IF NOT EXISTS camera_feeds (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), station_id uuid NULL REFERENCES stations(id),
  premise_id uuid NULL REFERENCES premises(id), name text NOT NULL, uri text NOT NULL, provider text NOT NULL,
  state text NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS radio_streams (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), name text NOT NULL, icecast_uri text NOT NULL,
  codec text NOT NULL, authorization_scope text NOT NULL, state text NOT NULL
);
CREATE TABLE IF NOT EXISTS leave_requests (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), employee_name text NOT NULL,
  start_date date NOT NULL, end_date date NOT NULL, leave_type text NOT NULL, reason text NOT NULL,
  status text NOT NULL, reviewed_by text NULL, created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS shift_trades (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), offered_shift_id uuid NOT NULL,
  offered_by text NOT NULL, requested_with text NOT NULL, status text NOT NULL, reviewed_by text NULL,
  created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS document_acknowledgements (
  id uuid PRIMARY KEY, document_id uuid NOT NULL REFERENCES documents(id), recipient text NOT NULL,
  status text NOT NULL, assigned_at_utc timestamptz NOT NULL, acknowledged_at_utc timestamptz NULL
);
CREATE TABLE IF NOT EXISTS form_templates (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), name text NOT NULL, version integer NOT NULL,
  status text NOT NULL, schema_json jsonb NOT NULL, created_by text NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS form_instances (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), template_id uuid NOT NULL REFERENCES form_templates(id),
  incident_id uuid NULL REFERENCES incidents(id), status text NOT NULL, values_json jsonb NOT NULL,
  created_by text NOT NULL, signed_by text NULL, created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS inspections (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), premise_id uuid NOT NULL REFERENCES premises(id),
  inspection_type text NOT NULL, inspection_date date NOT NULL, inspector text NOT NULL, status text NOT NULL,
  findings text NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS permits (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), premise_id uuid NOT NULL REFERENCES premises(id),
  permit_type text NOT NULL, display_number text NOT NULL, status text NOT NULL, issued_date date NOT NULL,
  expires_date date NULL, holder text NOT NULL, notes text NOT NULL, UNIQUE(agency_id, display_number)
);
CREATE TABLE IF NOT EXISTS maintenance_records (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), unit_id uuid NOT NULL REFERENCES operational_units(id),
  category text NOT NULL, summary text NOT NULL, status text NOT NULL, opened_date date NOT NULL,
  completed_date date NULL, cost numeric(14,2) NULL
);
CREATE TABLE IF NOT EXISTS neris_reports (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  status text NOT NULL, payload_json jsonb NOT NULL, validation_state text NOT NULL,
  validation_errors_json jsonb NOT NULL, submission_id text NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS device_identities (
  device_id text PRIMARY KEY, id uuid NOT NULL, agency_id uuid NOT NULL REFERENCES agencies(id), name text NOT NULL,
  role text NOT NULL, platform text NOT NULL, state text NOT NULL, certificate_thumbprint text NULL,
  created_at_utc timestamptz NOT NULL, last_seen_at_utc timestamptz NULL
);
CREATE TABLE IF NOT EXISTS user_security (
  user_id text PRIMARY KEY, display_name text NOT NULL, agency_id uuid NOT NULL REFERENCES agencies(id),
  roles_json jsonb NOT NULL, totp_enrolled boolean NOT NULL, passkey_enrolled boolean NOT NULL,
  break_glass boolean NOT NULL, state text NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS config_versions (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), area text NOT NULL, version integer NOT NULL,
  state text NOT NULL, content_json jsonb NOT NULL, created_by text NOT NULL, created_at_utc timestamptz NOT NULL,
  published_at_utc timestamptz NULL, UNIQUE(agency_id, area, version)
);
CREATE TABLE IF NOT EXISTS incident_relationships (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), source_incident_id uuid NOT NULL REFERENCES incidents(id),
  target_incident_id uuid NOT NULL REFERENCES incidents(id), relationship_type text NOT NULL, actor text NOT NULL,
  created_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS operational_groups (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  name text NOT NULL, group_type text NOT NULL, leader text NOT NULL, channel text NOT NULL, status text NOT NULL,
  created_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS command_assignments (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  command_name text NOT NULL, unit_id uuid NULL REFERENCES operational_units(id), person text NOT NULL, actor text NOT NULL,
  effective_at_utc timestamptz NOT NULL, ended_at_utc timestamptz NULL
);
CREATE TABLE IF NOT EXISTS mutual_aid_requests (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  requested_agency text NOT NULL, resource_type text NOT NULL, quantity integer NOT NULL, state text NOT NULL,
  requested_by text NOT NULL, requested_at_utc timestamptz NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS mayday_events (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), incident_id uuid NOT NULL REFERENCES incidents(id),
  unit_id uuid NULL REFERENCES operational_units(id), person text NOT NULL, location text NOT NULL, channel text NOT NULL,
  state text NOT NULL, declared_by text NOT NULL, declared_at_utc timestamptz NOT NULL, resolved_at_utc timestamptz NULL
);
CREATE TABLE IF NOT EXISTS response_plans (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), name text NOT NULL, nature_pattern text NOT NULL,
  priority text NOT NULL, required_unit_types_json jsonb NOT NULL, required_capabilities_json jsonb NOT NULL,
  enabled boolean NOT NULL, version integer NOT NULL, state text NOT NULL, updated_by text NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS workspace_layouts (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), user_id text NOT NULL, name text NOT NULL,
  role text NOT NULL, workstation_id text NOT NULL, theme text NOT NULL, density text NOT NULL,
  layout_json jsonb NOT NULL, is_default boolean NOT NULL, updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS theme_packages (
  id uuid PRIMARY KEY, agency_id uuid NOT NULL REFERENCES agencies(id), theme_key text NOT NULL, display_name text NOT NULL,
  contract_version integer NOT NULL, state text NOT NULL, resources_json jsonb NOT NULL, updated_at_utc timestamptz NOT NULL,
  UNIQUE(agency_id, theme_key)
);
CREATE TABLE IF NOT EXISTS vessel_observations (
  mmsi text PRIMARY KEY, id uuid NOT NULL, agency_id uuid NOT NULL REFERENCES agencies(id), name text NOT NULL,
  latitude double precision NOT NULL, longitude double precision NOT NULL, speed_knots double precision NOT NULL,
  course_degrees double precision NOT NULL, source text NOT NULL, observed_at_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS runtime_state_checkpoint (
  id integer PRIMARY KEY CHECK (id = 1), schema_version integer NOT NULL, saved_at_utc timestamptz NOT NULL,
  payload_json jsonb NOT NULL, payload_sha256 text NOT NULL
);

UPDATE schema_info SET version = 2, applied_at_utc = now() WHERE singleton = true;
COMMIT;
