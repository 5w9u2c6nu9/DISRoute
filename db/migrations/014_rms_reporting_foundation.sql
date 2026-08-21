BEGIN;

CREATE TABLE IF NOT EXISTS rms_incident_reports (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL,
    incident_id uuid NOT NULL,
    display_number text NOT NULL,
    report_type text NOT NULL,
    status text NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    source_incident_version bigint NOT NULL,
    report_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    updated_at_utc timestamptz NOT NULL,
    finalized_at_utc timestamptz NULL,
    UNIQUE (agency_id, incident_id, report_type)
);
CREATE INDEX IF NOT EXISTS ix_rms_incident_reports_incident ON rms_incident_reports(incident_id);
CREATE INDEX IF NOT EXISTS ix_rms_incident_reports_status ON rms_incident_reports(agency_id,status);

CREATE TABLE IF NOT EXISTS rms_report_versions (
    id uuid PRIMARY KEY,
    report_id uuid NOT NULL REFERENCES rms_incident_reports(id) ON DELETE CASCADE,
    version integer NOT NULL,
    status text NOT NULL,
    change_kind text NOT NULL,
    actor text NOT NULL,
    reason text NOT NULL,
    snapshot_json jsonb NOT NULL,
    created_at_utc timestamptz NOT NULL,
    UNIQUE(report_id,version)
);

CREATE TABLE IF NOT EXISTS rms_attachment_references (
    id uuid PRIMARY KEY,
    report_id uuid NOT NULL REFERENCES rms_incident_reports(id) ON DELETE CASCADE,
    agency_id uuid NOT NULL,
    file_name text NOT NULL,
    classification text NOT NULL,
    storage_reference text NOT NULL,
    description text NULL,
    added_by text NOT NULL,
    added_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_rms_attachment_references_report ON rms_attachment_references(report_id);

UPDATE schema_info SET version = 14, applied_at_utc = now() WHERE singleton = true;
COMMIT;
