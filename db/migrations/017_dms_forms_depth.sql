-- DISRoute schema 017: DMS/forms operational depth
CREATE TABLE IF NOT EXISTS dms_document_revisions (
    id uuid PRIMARY KEY,
    document_id uuid NOT NULL REFERENCES documents(id),
    version integer NOT NULL,
    content text NOT NULL,
    state text NOT NULL,
    author text NOT NULL,
    content_sha256 text NOT NULL,
    created_at_utc timestamptz NOT NULL,
    published_by text NULL,
    published_at_utc timestamptz NULL,
    UNIQUE(document_id, version)
);
CREATE INDEX IF NOT EXISTS ix_dms_document_revisions_document ON dms_document_revisions(document_id, version DESC);

CREATE TABLE IF NOT EXISTS dms_distributions (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL REFERENCES agencies(id),
    document_id uuid NOT NULL REFERENCES documents(id),
    document_version integer NOT NULL,
    scope_type text NOT NULL,
    scope_value text NOT NULL,
    required_read boolean NOT NULL,
    due_at_utc timestamptz NULL,
    state text NOT NULL,
    assigned_by text NOT NULL,
    assigned_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_dms_distributions_document ON dms_distributions(document_id, document_version, state);

CREATE TABLE IF NOT EXISTS dms_read_receipts (
    id uuid PRIMARY KEY,
    distribution_id uuid NOT NULL REFERENCES dms_distributions(id) ON DELETE CASCADE,
    document_id uuid NOT NULL REFERENCES documents(id),
    document_version integer NOT NULL,
    recipient text NOT NULL,
    state text NOT NULL,
    assigned_at_utc timestamptz NOT NULL,
    read_at_utc timestamptz NULL,
    acknowledged_at_utc timestamptz NULL,
    acknowledged_by text NULL
);
CREATE INDEX IF NOT EXISTS ix_dms_read_receipts_document ON dms_read_receipts(document_id, document_version, recipient);
CREATE UNIQUE INDEX IF NOT EXISTS ux_dms_read_receipts_distribution_recipient ON dms_read_receipts(distribution_id, lower(recipient));

CREATE TABLE IF NOT EXISTS dms_asset_references (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL REFERENCES agencies(id),
    document_id uuid NOT NULL REFERENCES documents(id),
    document_version integer NOT NULL,
    file_name text NOT NULL,
    content_type text NOT NULL,
    storage_reference text NOT NULL,
    content_sha256 text NULL,
    classification text NOT NULL,
    added_by text NOT NULL,
    added_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_dms_asset_references_document ON dms_asset_references(document_id, document_version);

CREATE TABLE IF NOT EXISTS dms_document_links (
    id uuid PRIMARY KEY,
    agency_id uuid NOT NULL REFERENCES agencies(id),
    document_id uuid NOT NULL REFERENCES documents(id),
    document_version integer NOT NULL,
    entity_type text NOT NULL,
    entity_key text NOT NULL,
    relationship text NOT NULL,
    linked_by text NOT NULL,
    linked_at_utc timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_dms_document_links_document ON dms_document_links(document_id, document_version);
CREATE INDEX IF NOT EXISTS ix_dms_document_links_entity ON dms_document_links(entity_type, entity_key);

CREATE TABLE IF NOT EXISTS form_signatures (
    id uuid PRIMARY KEY,
    form_id uuid NOT NULL REFERENCES form_instances(id),
    template_id uuid NOT NULL REFERENCES form_templates(id),
    template_version integer NOT NULL,
    signer text NOT NULL,
    meaning text NOT NULL,
    values_sha256 text NOT NULL,
    source text NOT NULL,
    signed_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_form_signatures_form ON form_signatures(form_id);

UPDATE schema_info SET version = 17, applied_at_utc = now() WHERE singleton = true;
