-- PROJEX 013: RBAC (Step 1c, ERD 4.2). Run as projex_owner.
-- * permissions get a module and become a read-only catalog for the application role
-- * roles may be SYSTEM roles (tenant_id NULL, visible to everyone, immutable) or tenant custom roles
-- * user_role_assignments: a user holds a role tenant-wide, or scoped to a company, a branch or a project
-- Seed data below is mirrored by Projex.Application.Security.RoleCatalog; an integration test keeps them identical.

ALTER TABLE identity.permissions ADD COLUMN module varchar(60) NOT NULL DEFAULT 'general';
REVOKE INSERT, UPDATE, DELETE ON identity.permissions FROM projex_app;

ALTER TABLE identity.roles ALTER COLUMN tenant_id DROP NOT NULL;
ALTER TABLE identity.roles ADD CONSTRAINT ck_roles_system_scope CHECK ((tenant_id IS NULL) = is_system);
CREATE UNIQUE INDEX ux_roles_system_code ON identity.roles(code) WHERE tenant_id IS NULL;
-- Everyone can read system roles; the existing tenant_isolation_roles policy still governs every write,
-- so a tenant can neither create, change nor delete a system role.
CREATE POLICY roles_read_system ON identity.roles FOR SELECT USING (tenant_id IS NULL);

-- A custom role may not reuse a system role code (project memberships store role codes).
CREATE FUNCTION identity.roles_no_system_code_clash() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.tenant_id IS NOT NULL AND EXISTS (SELECT 1 FROM identity.roles s WHERE s.tenant_id IS NULL AND s.code = NEW.code) THEN
        RAISE EXCEPTION 'role code % is reserved for a system role', NEW.code USING ERRCODE = '23505';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_roles_no_system_code_clash BEFORE INSERT OR UPDATE OF code ON identity.roles
    FOR EACH ROW EXECUTE FUNCTION identity.roles_no_system_code_clash();

-- ---- seed (the owner is subject to FORCE RLS, so lift it for the seed only) ----
ALTER TABLE identity.roles NO FORCE ROW LEVEL SECURITY;
INSERT INTO identity.permissions(id, code, module, description, created_at, updated_at) VALUES
    (gen_random_uuid(), 'projects.read', 'projects', 'View projects the user is a member of', now(), now()),
    (gen_random_uuid(), 'progress.update', 'progress', 'Record physical progress on activities', now(), now()),
    (gen_random_uuid(), 'planning.manage', 'planning', 'Edit WBS, activities, relationships, calendars, run schedules, manage baselines', now(), now()),
    (gen_random_uuid(), 'costing.read', 'costing', 'View cost codes, BOQ, budgets, transactions and EVM', now(), now()),
    (gen_random_uuid(), 'costing.transactions.enter', 'costing', 'Enter and submit draft cost transactions', now(), now()),
    (gen_random_uuid(), 'costing.manage', 'costing', 'Manage cost codes, BOQ and draft budgets; capture performance snapshots', now(), now()),
    (gen_random_uuid(), 'costing.approve', 'costing', 'Approve budgets and cost transactions; post reversals', now(), now()),
    (gen_random_uuid(), 'reports.read', 'reports', 'View reports', now(), now()),
    (gen_random_uuid(), 'reports.export', 'reports', 'Export reports', now(), now()),
    (gen_random_uuid(), 'audit.read', 'audit', 'View the tenant audit trail', now(), now()),
    (gen_random_uuid(), 'organization.manage', 'organization', 'Manage companies and branches', now(), now()),
    (gen_random_uuid(), 'identity.manage', 'identity', 'Manage users, roles and role assignments', now(), now());

INSERT INTO identity.roles(id, tenant_id, code, name, is_system, created_at, updated_at) VALUES
    (gen_random_uuid(), NULL, 'COMPANYADMIN', 'Company Administrator', true, now(), now()),
    (gen_random_uuid(), NULL, 'PROJECTMANAGER', 'Project Manager', true, now(), now()),
    (gen_random_uuid(), NULL, 'PLANNINGENGINEER', 'Planning Engineer', true, now(), now()),
    (gen_random_uuid(), NULL, 'COSTCONTROLENGINEER', 'Cost Control Engineer', true, now(), now()),
    (gen_random_uuid(), NULL, 'ACCOUNTANT', 'Accountant', true, now(), now()),
    (gen_random_uuid(), NULL, 'SITEENGINEER', 'Site Engineer', true, now(), now()),
    (gen_random_uuid(), NULL, 'VIEWER', 'Viewer', true, now(), now()),
    (gen_random_uuid(), NULL, 'AUDITOR', 'Auditor', true, now(), now());

INSERT INTO identity.role_permissions(role_id, permission_id)
SELECT r.id, p.id
FROM (VALUES
    ('COMPANYADMIN', 'projects.read'),
    ('COMPANYADMIN', 'progress.update'),
    ('COMPANYADMIN', 'planning.manage'),
    ('COMPANYADMIN', 'costing.read'),
    ('COMPANYADMIN', 'costing.transactions.enter'),
    ('COMPANYADMIN', 'costing.manage'),
    ('COMPANYADMIN', 'costing.approve'),
    ('COMPANYADMIN', 'reports.read'),
    ('COMPANYADMIN', 'reports.export'),
    ('COMPANYADMIN', 'audit.read'),
    ('COMPANYADMIN', 'organization.manage'),
    ('COMPANYADMIN', 'identity.manage'),
    ('PROJECTMANAGER', 'projects.read'),
    ('PROJECTMANAGER', 'progress.update'),
    ('PROJECTMANAGER', 'planning.manage'),
    ('PROJECTMANAGER', 'costing.read'),
    ('PROJECTMANAGER', 'costing.transactions.enter'),
    ('PROJECTMANAGER', 'costing.manage'),
    ('PROJECTMANAGER', 'costing.approve'),
    ('PROJECTMANAGER', 'reports.read'),
    ('PROJECTMANAGER', 'reports.export'),
    ('PLANNINGENGINEER', 'projects.read'),
    ('PLANNINGENGINEER', 'progress.update'),
    ('PLANNINGENGINEER', 'planning.manage'),
    ('PLANNINGENGINEER', 'reports.read'),
    ('COSTCONTROLENGINEER', 'projects.read'),
    ('COSTCONTROLENGINEER', 'costing.read'),
    ('COSTCONTROLENGINEER', 'costing.transactions.enter'),
    ('COSTCONTROLENGINEER', 'costing.manage'),
    ('COSTCONTROLENGINEER', 'reports.read'),
    ('COSTCONTROLENGINEER', 'reports.export'),
    ('ACCOUNTANT', 'projects.read'),
    ('ACCOUNTANT', 'costing.read'),
    ('ACCOUNTANT', 'costing.transactions.enter'),
    ('ACCOUNTANT', 'reports.read'),
    ('SITEENGINEER', 'projects.read'),
    ('SITEENGINEER', 'progress.update'),
    ('VIEWER', 'projects.read'),
    ('VIEWER', 'costing.read'),
    ('VIEWER', 'reports.read'),
    ('AUDITOR', 'projects.read'),
    ('AUDITOR', 'costing.read'),
    ('AUDITOR', 'audit.read'),
    ('AUDITOR', 'reports.read'),
    ('AUDITOR', 'reports.export')
    ) AS m(role_code, permission_code)
JOIN identity.roles r ON r.tenant_id IS NULL AND r.code = m.role_code
JOIN identity.permissions p ON p.code = m.permission_code;
ALTER TABLE identity.roles FORCE ROW LEVEL SECURITY;

-- role_permissions has no tenant_id: it follows the visibility of its role. Only custom (tenant) roles can be edited.
ALTER TABLE identity.role_permissions ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.role_permissions FORCE ROW LEVEL SECURITY;
CREATE POLICY role_permissions_read ON identity.role_permissions FOR SELECT
    USING (EXISTS (SELECT 1 FROM identity.roles r WHERE r.id = role_permissions.role_id));
CREATE POLICY role_permissions_write ON identity.role_permissions FOR ALL
    USING (EXISTS (SELECT 1 FROM identity.roles r WHERE r.id = role_permissions.role_id AND r.tenant_id IS NOT NULL))
    WITH CHECK (EXISTS (SELECT 1 FROM identity.roles r WHERE r.id = role_permissions.role_id AND r.tenant_id IS NOT NULL));

-- ---- scoped role assignments ----
CREATE TABLE identity.user_role_assignments (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    user_id uuid NOT NULL,
    role_id uuid NOT NULL REFERENCES identity.roles(id),
    company_id uuid NULL,
    branch_id uuid NULL,
    project_id uuid NULL,
    assigned_by uuid NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_ura_user FOREIGN KEY (tenant_id, user_id) REFERENCES identity.users (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_ura_company FOREIGN KEY (tenant_id, company_id) REFERENCES organization.companies (tenant_id, id),
    CONSTRAINT fk_ura_branch FOREIGN KEY (tenant_id, company_id, branch_id) REFERENCES organization.branches (tenant_id, company_id, id),
    CONSTRAINT fk_ura_project FOREIGN KEY (tenant_id, project_id) REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    -- One scope level: tenant-wide (all null), company, branch (company + branch) or project.
    CONSTRAINT ck_ura_scope CHECK (project_id IS NULL OR (company_id IS NULL AND branch_id IS NULL)),
    CONSTRAINT ck_ura_branch_needs_company CHECK (branch_id IS NULL OR company_id IS NOT NULL)
);
CREATE UNIQUE INDEX ux_ura_assignment ON identity.user_role_assignments (tenant_id, user_id, role_id, company_id, branch_id, project_id) NULLS NOT DISTINCT;
CREATE INDEX ix_ura_user ON identity.user_role_assignments (tenant_id, user_id);

-- The role must be a system role or one of this tenant's own roles. Runs with the caller's rights, so another tenant's role is invisible (RLS) and rejected.
CREATE FUNCTION identity.check_assignment_role() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM identity.roles r WHERE r.id = NEW.role_id AND (r.tenant_id IS NULL OR r.tenant_id = NEW.tenant_id)) THEN
        RAISE EXCEPTION 'role % is not available to tenant %', NEW.role_id, NEW.tenant_id USING ERRCODE = '23503';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_ura_role BEFORE INSERT OR UPDATE OF role_id, tenant_id ON identity.user_role_assignments
    FOR EACH ROW EXECUTE FUNCTION identity.check_assignment_role();

ALTER TABLE identity.user_role_assignments ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.user_role_assignments FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_user_role_assignments ON identity.user_role_assignments
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- Project memberships can now also carry the Accountant and Auditor roles.
ALTER TABLE identity.project_memberships DROP CONSTRAINT ck_project_membership_role;
ALTER TABLE identity.project_memberships ADD CONSTRAINT ck_project_membership_role CHECK (role_code IN
    ('PROJECTMANAGER','PLANNINGENGINEER','COSTCONTROLENGINEER','ACCOUNTANT','SITEENGINEER','VIEWER','AUDITOR'));

-- Existing company administrators (ASP.NET Identity role) become tenant-wide CompanyAdmin assignments.
DO $$
DECLARE t record;
BEGIN
    FOR t IN SELECT id FROM platform.tenants LOOP
        PERFORM set_config('app.tenant_id', t.id::text, true);
        INSERT INTO identity.user_role_assignments(id, tenant_id, user_id, role_id, created_at, updated_at)
        SELECT gen_random_uuid(), u.tenant_id, u.id, r.id, now(), now()
        FROM identity.user_roles ur
        JOIN identity.identity_roles ir ON ir.id = ur.role_id AND ir.normalized_name = 'COMPANYADMIN'
        JOIN identity.users u ON u.id = ur.user_id
        JOIN identity.roles r ON r.tenant_id IS NULL AND r.code = 'COMPANYADMIN'
        ON CONFLICT DO NOTHING;
    END LOOP;
    PERFORM set_config('app.tenant_id', '', true);
END $$;
