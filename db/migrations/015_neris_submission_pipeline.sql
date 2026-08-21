BEGIN;

CREATE TABLE IF NOT EXISTS neris_submissions (
    id uuid PRIMARY KEY,
    rms_report_id uuid NOT NULL REFERENCES rms_incident_reports(id) ON DELETE CASCADE,
    agency_id uuid NOT NULL,
    incident_id uuid NOT NULL,
    rms_report_version integer NOT NULL CHECK (rms_report_version > 0),
    profile_version text NOT NULL,
    state text NOT NULL,
    payload_json jsonb NOT NULL,
    payload_sha256 text NOT NULL,
    mapping_errors_json jsonb NOT NULL,
    mapping_warnings_json jsonb NOT NULL,
    provider_key text NULL,
    provider_reference text NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    last_attempt_at_utc timestamptz NULL,
    last_error text NULL,
    created_by text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    UNIQUE(rms_report_id,rms_report_version,profile_version)
);
CREATE INDEX IF NOT EXISTS ix_neris_submissions_queue ON neris_submissions(state,updated_at_utc);
CREATE INDEX IF NOT EXISTS ix_neris_submissions_report ON neris_submissions(rms_report_id,rms_report_version);

CREATE TABLE IF NOT EXISTS neris_submission_attempts (
    id uuid PRIMARY KEY,
    submission_id uuid NOT NULL REFERENCES neris_submissions(id) ON DELETE CASCADE,
    attempt_number integer NOT NULL CHECK (attempt_number > 0),
    operation_id uuid NOT NULL,
    provider_key text NOT NULL,
    state text NOT NULL,
    actor text NOT NULL,
    validation_errors_json jsonb NOT NULL,
    validation_warnings_json jsonb NOT NULL,
    provider_reference text NULL,
    error text NULL,
    requested_at_utc timestamptz NOT NULL,
    completed_at_utc timestamptz NULL,
    UNIQUE(submission_id,attempt_number),
    UNIQUE(operation_id)
);
CREATE INDEX IF NOT EXISTS ix_neris_submission_attempts_submission ON neris_submission_attempts(submission_id,attempt_number);

UPDATE schema_info SET version = 15, applied_at_utc = now() WHERE singleton = true;
COMMIT;
