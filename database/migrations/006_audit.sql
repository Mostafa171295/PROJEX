-- PROJEX 006: append-only audit trail (SRS FR-AUD-001..003, ERD audit.audit_logs). Run as projex_owner.
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE audit.audit_logs (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization.tenants(id),
    actor_user_id uuid NULL,
    action varchar(100) NOT NULL,
    entity_type varchar(100) NOT NULL,
    entity_id uuid NULL,
    occurred_at timestamptz NOT NULL,
    correlation_id uuid NULL,
    ip_address inet NULL,
    user_agent text NULL,
    before_data jsonb NULL,
    after_data jsonb NULL
);
CREATE INDEX ix_audit_tenant_occurred ON audit.audit_logs(tenant_id, occurred_at DESC);
CREATE INDEX ix_audit_tenant_entity ON audit.audit_logs(tenant_id, entity_type, entity_id);

ALTER TABLE audit.audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit.audit_logs FORCE ROW LEVEL SECURITY;
CREATE POLICY audit_select ON audit.audit_logs FOR SELECT
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
CREATE POLICY audit_insert ON audit.audit_logs FOR INSERT
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- The application role may only read and append. No UPDATE/DELETE privilege, and a trigger as a second barrier.
GRANT USAGE ON SCHEMA audit TO projex_app;
GRANT SELECT, INSERT ON audit.audit_logs TO projex_app;
CREATE FUNCTION audit.forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'audit.audit_logs is append-only' USING ERRCODE = '42501';
END $$;
CREATE TRIGGER trg_audit_no_update BEFORE UPDATE OR DELETE ON audit.audit_logs
    FOR EACH ROW EXECUTE FUNCTION audit.forbid_change();
CREATE TRIGGER trg_audit_no_truncate BEFORE TRUNCATE ON audit.audit_logs
    FOR EACH STATEMENT EXECUTE FUNCTION audit.forbid_change();
