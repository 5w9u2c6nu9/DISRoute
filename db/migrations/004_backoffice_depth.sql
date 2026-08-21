BEGIN;

CREATE TABLE IF NOT EXISTS rotations (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    name text NOT NULL,
    pattern text NOT NULL,
    effective_date date NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    updated_by text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_rotations_agency ON rotations(agency_id, enabled);

CREATE TABLE IF NOT EXISTS overtime_offers (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    work_date date NOT NULL,
    shift_name text NOT NULL,
    station_id uuid NOT NULL,
    position text NOT NULL,
    qualification text NOT NULL,
    slots integer NOT NULL CHECK (slots > 0),
    state text NOT NULL,
    accepted_employees_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_overtime_offers_agency_date ON overtime_offers(agency_id, work_date);

CREATE TABLE IF NOT EXISTS timesheet_entries (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    employee_name text NOT NULL,
    work_date date NOT NULL,
    hours numeric(6,2) NOT NULL CHECK (hours > 0 AND hours <= 24),
    time_category text NOT NULL,
    station_id uuid NULL,
    incident_id uuid NULL,
    status text NOT NULL,
    approved_by text NULL,
    notes text NOT NULL DEFAULT '',
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_timesheets_agency_date ON timesheet_entries(agency_id, work_date, status);

CREATE TABLE IF NOT EXISTS approval_workflows (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    entity_type text NOT NULL,
    entity_id uuid NOT NULL,
    name text NOT NULL,
    mode text NOT NULL,
    state text NOT NULL,
    current_sequence integer NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS approval_steps (
    id uuid PRIMARY KEY,
    workflow_id uuid NOT NULL REFERENCES approval_workflows(id) ON DELETE CASCADE,
    sequence integer NOT NULL,
    approver text NOT NULL,
    required boolean NOT NULL DEFAULT true,
    state text NOT NULL,
    comment text NULL,
    decided_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_approval_steps_workflow ON approval_steps(workflow_id, sequence);

CREATE TABLE IF NOT EXISTS violations (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    premise_id uuid NOT NULL,
    inspection_id uuid NULL,
    code text NOT NULL,
    description text NOT NULL,
    severity text NOT NULL,
    state text NOT NULL,
    issued_date date NOT NULL,
    due_date date NULL,
    resolved_date date NULL
);
CREATE INDEX IF NOT EXISTS ix_violations_premise ON violations(premise_id, state);

CREATE TABLE IF NOT EXISTS hazmat_records (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    premise_id uuid NOT NULL,
    material text NOT NULL,
    un_number text NOT NULL,
    quantity text NOT NULL,
    storage_location text NOT NULL,
    hazards text NOT NULL,
    state text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_hazmat_premise ON hazmat_records(premise_id, state);

CREATE TABLE IF NOT EXISTS investigations (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    incident_id uuid NULL,
    premise_id uuid NULL,
    title text NOT NULL,
    lead_investigator text NOT NULL,
    state text NOT NULL,
    summary text NOT NULL DEFAULT '',
    opened_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    closed_at_utc timestamptz NULL
);
CREATE INDEX IF NOT EXISTS ix_investigations_agency_state ON investigations(agency_id, state);

CREATE TABLE IF NOT EXISTS personnel_qualifications (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    person_id text NOT NULL,
    person_name text NOT NULL,
    qualification text NOT NULL,
    effective_date date NOT NULL,
    expires_date date NULL,
    state text NOT NULL,
    source text NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_qualifications_person ON personnel_qualifications(agency_id, person_id, state);

UPDATE schema_info SET version = 4, applied_at_utc = now() WHERE singleton = true;
COMMIT;
