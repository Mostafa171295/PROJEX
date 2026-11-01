-- PROJEX 009: append-only progress history (SRS FR-EVM-002). Lets EVM use progress as of a data date. Run as projex_owner.
CREATE TABLE progress.progress_updates (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    activity_id uuid NOT NULL,
    data_date date NOT NULL,
    measurement_method varchar(30) NOT NULL DEFAULT 'PhysicalPercent',
    percent_complete numeric(5,2) NOT NULL,
    status varchar(20) NOT NULL,
    submitted_by uuid NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_progress_activity FOREIGN KEY (tenant_id, project_id, activity_id)
        REFERENCES planning.activities (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_progress_percent CHECK (percent_complete BETWEEN 0 AND 100),
    CONSTRAINT ck_progress_method CHECK (measurement_method IN ('PhysicalPercent','UnitsBased','DurationBased','WeightedMilestones'))
);
CREATE INDEX ix_progress_activity_date ON progress.progress_updates(project_id, activity_id, data_date DESC, created_at DESC);
REVOKE UPDATE, DELETE ON progress.progress_updates FROM projex_app;
CREATE TRIGGER trg_progress_no_change BEFORE UPDATE OR DELETE ON progress.progress_updates
    FOR EACH ROW EXECUTE FUNCTION progress.forbid_change();
ALTER TABLE progress.progress_updates ENABLE ROW LEVEL SECURITY;
ALTER TABLE progress.progress_updates FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_progress_progress_updates ON progress.progress_updates
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
