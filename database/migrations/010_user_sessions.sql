-- PROJEX 010: sessions with rotating refresh tokens and last-login tracking (SRS FR-AUTH-002/003/006, FR-IAM-006). Run as projex_owner.
ALTER TABLE identity.users ADD COLUMN last_login_at timestamptz NULL;

CREATE TABLE identity.user_sessions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organization.tenants(id),
    user_id uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    refresh_token_hash varchar(64) NOT NULL,
    previous_refresh_token_hash varchar(64) NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    revoked_reason varchar(40) NULL,
    ip_address varchar(64) NULL,
    user_agent varchar(400) NULL,
    last_refreshed_at timestamptz NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
CREATE UNIQUE INDEX ux_user_sessions_refresh_hash ON identity.user_sessions(refresh_token_hash);
CREATE INDEX ix_user_sessions_previous_hash ON identity.user_sessions(previous_refresh_token_hash) WHERE previous_refresh_token_hash IS NOT NULL;
CREATE INDEX ix_user_sessions_user_active ON identity.user_sessions(user_id) WHERE revoked_at IS NULL;

ALTER TABLE identity.user_sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity.user_sessions FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_user_sessions ON identity.user_sessions
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
