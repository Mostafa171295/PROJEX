-- PROJEX 011: identity hardening (Step 1a). Run as projex_owner.
-- 1) Tenant-safe composite foreign keys: a membership/session can only reference a user of the SAME tenant.
-- 2) RLS on the ASP.NET Identity child tables that carry no tenant_id of their own.

-- Composite parent key (SQL needs a unique constraint to reference (tenant_id, id)).
ALTER TABLE identity.users ADD CONSTRAINT uq_users_tenant_id UNIQUE (tenant_id, id);

-- user_memberships -> users
ALTER TABLE identity.user_memberships DROP CONSTRAINT IF EXISTS user_memberships_user_id_fkey;
ALTER TABLE identity.user_memberships ADD CONSTRAINT fk_user_membership_user
    FOREIGN KEY (tenant_id, user_id) REFERENCES identity.users (tenant_id, id);

-- user_sessions -> users (sessions disappear with their user, as before)
ALTER TABLE identity.user_sessions DROP CONSTRAINT IF EXISTS user_sessions_user_id_fkey;
ALTER TABLE identity.user_sessions ADD CONSTRAINT fk_user_sessions_user
    FOREIGN KEY (tenant_id, user_id) REFERENCES identity.users (tenant_id, id) ON DELETE CASCADE;

-- project_memberships (004 had no FK on user_id at all, and a non-composite FK on project_id)
ALTER TABLE identity.project_memberships DROP CONSTRAINT IF EXISTS fk_project_membership_project;
ALTER TABLE identity.project_memberships ADD CONSTRAINT fk_project_membership_project
    FOREIGN KEY (tenant_id, project_id) REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE;
ALTER TABLE identity.project_memberships ADD CONSTRAINT fk_project_membership_user
    FOREIGN KEY (tenant_id, user_id) REFERENCES identity.users (tenant_id, id);
ALTER TABLE identity.project_memberships ADD CONSTRAINT fk_project_membership_tenant
    FOREIGN KEY (tenant_id) REFERENCES organization.tenants (id);

-- Identity child tables: no tenant_id column, so the policy follows the owning user.
-- The sub-select on identity.users is itself filtered by users' RLS, i.e. by app.tenant_id.
DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY['user_roles','user_claims','user_logins','user_tokens'] LOOP
        EXECUTE format('ALTER TABLE identity.%I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE identity.%I FORCE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation_%I ON identity.%I', t, t);
        EXECUTE format($f$CREATE POLICY tenant_isolation_%I ON identity.%I
            USING (EXISTS (SELECT 1 FROM identity.users u WHERE u.id = %I.user_id))
            WITH CHECK (EXISTS (SELECT 1 FROM identity.users u WHERE u.id = %I.user_id))$f$, t, t, t, t);
    END LOOP;
END $$;
