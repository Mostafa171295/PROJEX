-- Apply after the EF Core schema has been created.
-- The API sets app.tenant_id from a validated JWT claim on every DB connection.
-- The login endpoint sets it only after resolving the submitted tenant key.

ALTER TABLE organization.companies ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization.companies FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_companies ON organization.companies;
CREATE POLICY tenant_isolation_companies ON organization.companies
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE organization.branches ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization.branches FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_branches ON organization.branches;
CREATE POLICY tenant_isolation_branches ON organization.branches
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE projects.projects ENABLE ROW LEVEL SECURITY;
ALTER TABLE projects.projects FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_projects ON projects.projects;
CREATE POLICY tenant_isolation_projects ON projects.projects
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE identity.users ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.users FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_users ON identity.users;
CREATE POLICY tenant_isolation_users ON identity.users
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE identity.roles ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.roles FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_roles ON identity.roles;
CREATE POLICY tenant_isolation_roles ON identity.roles
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE identity.user_memberships ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.user_memberships FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation_user_memberships ON identity.user_memberships;
CREATE POLICY tenant_isolation_user_memberships ON identity.user_memberships
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
