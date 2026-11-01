-- PROJEX 012: platform schema (Step 1b, ERD 4.1). Run as projex_owner.
-- Moves tenants out of "organization" into "platform", aligns it with the ERD (code, default_locale),
-- and adds subscriptions, platform administrators and an append-only platform audit trail.
-- Platform administrators are NOT tenant users: they have no tenant_id, so RLS gives them no access to tenant business data.
CREATE SCHEMA IF NOT EXISTS platform;

ALTER TABLE organization.tenants SET SCHEMA platform;
ALTER TABLE platform.tenants RENAME COLUMN key TO code;
ALTER INDEX platform.ux_tenants_key RENAME TO ux_tenants_code;
ALTER TABLE platform.tenants ADD COLUMN default_locale varchar(10) NOT NULL DEFAULT 'en';

-- Tenants are closed, never deleted (audit rows and business data reference them).
GRANT USAGE ON SCHEMA platform TO projex_app;
REVOKE DELETE ON platform.tenants FROM projex_app;

CREATE TABLE platform.subscriptions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES platform.tenants(id),
    plan_code varchar(50) NOT NULL,
    status varchar(20) NOT NULL,
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NULL,
    seat_limit integer NOT NULL,
    module_entitlements jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT ck_subscription_status CHECK (status IN ('Trial','Active','PastDue','Suspended','Cancelled')),
    CONSTRAINT ck_subscription_seats CHECK (seat_limit > 0),
    CONSTRAINT ck_subscription_dates CHECK (ends_at IS NULL OR ends_at > starts_at)
);
-- At most one live subscription per tenant; history of cancelled ones is kept.
CREATE UNIQUE INDEX ux_subscriptions_live_per_tenant ON platform.subscriptions(tenant_id)
    WHERE status IN ('Trial','Active','PastDue');
GRANT SELECT, INSERT, UPDATE ON platform.subscriptions TO projex_app;

CREATE TABLE platform.platform_admins (
    id uuid PRIMARY KEY,
    email varchar(256) NOT NULL,
    normalized_email varchar(256) NOT NULL,
    display_name varchar(200) NOT NULL,
    password_hash text NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    access_failed_count integer NOT NULL DEFAULT 0,
    lockout_end timestamptz NULL,
    last_login_at timestamptz NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX ux_platform_admins_email ON platform.platform_admins(normalized_email);
GRANT SELECT, INSERT, UPDATE ON platform.platform_admins TO projex_app;

CREATE FUNCTION platform.forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'platform.% is append-only', TG_TABLE_NAME USING ERRCODE = '42501';
END $$;

CREATE TABLE platform.audit_events (
    id uuid PRIMARY KEY,
    occurred_at timestamptz NOT NULL,
    admin_id uuid NULL REFERENCES platform.platform_admins(id),
    action varchar(100) NOT NULL,
    target_tenant_id uuid NULL REFERENCES platform.tenants(id),
    details jsonb NULL,
    ip_address varchar(64) NULL
);
CREATE INDEX ix_platform_audit_occurred ON platform.audit_events(occurred_at DESC);
CREATE INDEX ix_platform_audit_tenant ON platform.audit_events(target_tenant_id, occurred_at DESC);
GRANT SELECT, INSERT ON platform.audit_events TO projex_app;
CREATE TRIGGER trg_platform_audit_no_change BEFORE UPDATE OR DELETE ON platform.audit_events
    FOR EACH ROW EXECUTE FUNCTION platform.forbid_change();
