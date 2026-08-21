BEGIN;

CREATE TABLE IF NOT EXISTS workforce_staffing_rules (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    station_id uuid NULL,
    shift_name text NOT NULL,
    position text NOT NULL,
    minimum_count integer NOT NULL CHECK (minimum_count > 0),
    required_qualification text NOT NULL DEFAULT '',
    enabled boolean NOT NULL DEFAULT true,
    updated_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_workforce_staffing_rules_scope ON workforce_staffing_rules(agency_id,station_id,shift_name,enabled);

CREATE TABLE IF NOT EXISTS workforce_assignments (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    work_date date NOT NULL,
    shift_name text NOT NULL,
    station_id uuid NOT NULL,
    unit_id uuid NULL,
    position text NOT NULL,
    employee_id text NULL,
    employee_name text NOT NULL,
    qualification text NOT NULL DEFAULT '',
    assignment_type text NOT NULL,
    state text NOT NULL,
    effective_at_utc timestamptz NOT NULL,
    ends_at_utc timestamptz NULL,
    source text NOT NULL,
    updated_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_workforce_assignments_roster ON workforce_assignments(agency_id,work_date,shift_name,station_id,state);

CREATE TABLE IF NOT EXISTS workforce_schedule_changes (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    work_date date NOT NULL,
    assignment_id uuid NULL,
    kind text NOT NULL,
    summary text NOT NULL,
    actor text NOT NULL,
    occurred_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_workforce_schedule_changes_date ON workforce_schedule_changes(agency_id,work_date,occurred_at_utc);

CREATE TABLE IF NOT EXISTS workforce_roster_snapshots (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    work_date date NOT NULL,
    revision integer NOT NULL CHECK (revision > 0),
    state text NOT NULL,
    posted_at_utc timestamptz NOT NULL,
    posted_by text NOT NULL,
    notes text NOT NULL DEFAULT '',
    content_sha256 text NOT NULL,
    snapshot_json jsonb NOT NULL,
    UNIQUE(agency_id,work_date,revision)
);
CREATE INDEX IF NOT EXISTS ix_workforce_roster_snapshots_date ON workforce_roster_snapshots(agency_id,work_date,revision DESC);

UPDATE schema_info SET version = 16, applied_at_utc = now() WHERE singleton = true;
COMMIT;
