-- 0.4.0 starter SQL; validate schema and apply via controlled migration.
CREATE TABLE IF NOT EXISTS identity.project_memberships (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL, project_id uuid NOT NULL, user_id uuid NOT NULL,
 role_code varchar(40) NOT NULL, is_active boolean NOT NULL DEFAULT true,
 created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
 CONSTRAINT uq_project_membership UNIQUE (tenant_id, project_id, user_id),
 CONSTRAINT fk_project_membership_project FOREIGN KEY (project_id) REFERENCES projects.projects(id) ON DELETE CASCADE,
 CONSTRAINT ck_project_membership_role CHECK (role_code IN ('PROJECTMANAGER','PLANNINGENGINEER','COSTCONTROLENGINEER','SITEENGINEER','VIEWER'))
);
CREATE INDEX IF NOT EXISTS ix_project_memberships_project_active ON identity.project_memberships (tenant_id, project_id, is_active);
ALTER TABLE identity.project_memberships ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.project_memberships FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_project_memberships ON identity.project_memberships;
CREATE POLICY tenant_isolation_project_memberships ON identity.project_memberships
 USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
 WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
