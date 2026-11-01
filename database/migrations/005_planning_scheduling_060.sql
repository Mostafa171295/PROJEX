-- PROJEX ENTERPRISE 0.6.0: persistent planning and scheduling.
-- Apply after the Starter 0.5.0 schema. Review against the deployed baseline before production.
CREATE SCHEMA IF NOT EXISTS planning;

CREATE TABLE planning.wbs_nodes (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    parent_id uuid NULL,
    code varchar(80) NOT NULL,
    name varchar(200) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_wbs_tenant_project_id UNIQUE (tenant_id, project_id, id),
    CONSTRAINT fk_wbs_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_wbs_parent FOREIGN KEY (tenant_id, project_id, parent_id)
        REFERENCES planning.wbs_nodes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_wbs_not_self_parent CHECK (parent_id IS NULL OR parent_id <> id)
);
CREATE UNIQUE INDEX ux_wbs_project_code ON planning.wbs_nodes(project_id, code);
CREATE INDEX ix_wbs_project_parent_order ON planning.wbs_nodes(project_id, parent_id, sort_order);

CREATE TABLE planning.project_calendars (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    name varchar(120) NOT NULL,
    time_zone varchar(80) NOT NULL,
    working_week jsonb NOT NULL DEFAULT '{}'::jsonb,
    is_default boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_calendar_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT uq_calendar_tenant_project_id UNIQUE (tenant_id, project_id, id),
    CONSTRAINT fk_calendar_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX ux_calendar_project_name ON planning.project_calendars(project_id, name);
CREATE UNIQUE INDEX ux_calendar_one_default_per_project ON planning.project_calendars(project_id) WHERE is_default;

CREATE TABLE planning.activities (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    wbs_node_id uuid NOT NULL,
    calendar_id uuid NULL,
    activity_code varchar(50) NOT NULL,
    name varchar(250) NOT NULL,
    type varchar(20) NOT NULL,
    duration_minutes integer NOT NULL,
    planned_start timestamptz NULL,
    planned_finish timestamptz NULL,
    actual_start timestamptz NULL,
    actual_finish timestamptz NULL,
    percent_complete numeric(5,2) NOT NULL DEFAULT 0,
    status varchar(20) NOT NULL DEFAULT 'NotStarted',
    is_critical boolean NOT NULL DEFAULT false,
    total_float_minutes numeric(20,4) NOT NULL DEFAULT 0,
    early_start_working_days numeric(20,6) NULL,
    early_finish_working_days numeric(20,6) NULL,
    late_start_working_days numeric(20,6) NULL,
    late_finish_working_days numeric(20,6) NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_activity_tenant_project_id UNIQUE (tenant_id, project_id, id),
    CONSTRAINT fk_activity_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_activity_wbs FOREIGN KEY (tenant_id, project_id, wbs_node_id)
        REFERENCES planning.wbs_nodes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_activity_calendar FOREIGN KEY (tenant_id, project_id, calendar_id)
        REFERENCES planning.project_calendars (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_activity_duration CHECK (duration_minutes >= 0),
    CONSTRAINT ck_activity_milestone_duration CHECK (type <> 'Milestone' OR duration_minutes = 0),
    CONSTRAINT ck_activity_percent CHECK (percent_complete BETWEEN 0 AND 100)
);
CREATE UNIQUE INDEX ux_activity_project_code ON planning.activities(project_id, activity_code);
CREATE INDEX ix_activity_project_wbs ON planning.activities(project_id, wbs_node_id);
CREATE INDEX ix_activity_project_dates ON planning.activities(project_id, planned_start, planned_finish);

CREATE TABLE planning.activity_relationships (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    predecessor_activity_id uuid NOT NULL,
    successor_activity_id uuid NOT NULL,
    type varchar(2) NOT NULL,
    lag_minutes integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_relationship_predecessor FOREIGN KEY (tenant_id, project_id, predecessor_activity_id)
        REFERENCES planning.activities(tenant_id, project_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_relationship_successor FOREIGN KEY (tenant_id, project_id, successor_activity_id)
        REFERENCES planning.activities(tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_relationship_type CHECK (type IN ('FS','SS','FF','SF')),
    CONSTRAINT ck_relationship_not_self CHECK (predecessor_activity_id <> successor_activity_id),
    CONSTRAINT uq_activity_relationship UNIQUE (predecessor_activity_id, successor_activity_id, type)
);
CREATE INDEX ix_relationship_project_predecessor ON planning.activity_relationships(project_id, predecessor_activity_id);
CREATE INDEX ix_relationship_project_successor ON planning.activity_relationships(project_id, successor_activity_id);

CREATE TABLE planning.calendar_exceptions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    calendar_id uuid NOT NULL,
    exception_date date NOT NULL,
    is_working_day boolean NOT NULL,
    start_time time NULL,
    end_time time NULL,
    description varchar(250) NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_calendar_exception_calendar FOREIGN KEY (tenant_id, calendar_id)
        REFERENCES planning.project_calendars(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT uq_calendar_exception_date UNIQUE (calendar_id, exception_date),
    CONSTRAINT ck_calendar_exception_times CHECK ((start_time IS NULL AND end_time IS NULL) OR (start_time IS NOT NULL AND end_time > start_time))
);

CREATE TABLE planning.schedule_runs (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    calculated_by uuid NOT NULL,
    engine_version varchar(40) NOT NULL,
    result_json jsonb NOT NULL,
    calculated_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_schedule_run_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects(tenant_id, id) ON DELETE CASCADE
);
CREATE INDEX ix_schedule_run_project_date ON planning.schedule_runs(project_id, calculated_at DESC);

CREATE TABLE planning.baselines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    name varchar(150) NOT NULL,
    version integer NOT NULL,
    status varchar(20) NOT NULL,
    captured_at timestamptz NOT NULL,
    approved_at timestamptz NULL,
    approved_by uuid NULL,
    change_reason text NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_baseline_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_baseline_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_baseline_version CHECK (version > 0),
    CONSTRAINT ck_baseline_status CHECK (status IN ('Draft','Approved','Superseded'))
);
CREATE UNIQUE INDEX ux_baseline_project_version ON planning.baselines(project_id, version);

CREATE TABLE planning.baseline_activity_dates (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    baseline_id uuid NOT NULL,
    activity_id uuid NOT NULL,
    planned_start timestamptz NULL,
    planned_finish timestamptz NULL,
    duration_minutes integer NOT NULL,
    percent_complete numeric(5,2) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_baseline_date_baseline FOREIGN KEY (tenant_id, baseline_id)
        REFERENCES planning.baselines(tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT uq_baseline_activity UNIQUE (baseline_id, activity_id),
    CONSTRAINT ck_baseline_duration CHECK (duration_minutes >= 0),
    CONSTRAINT ck_baseline_percent CHECK (percent_complete BETWEEN 0 AND 100)
);

-- Tenant isolation for all tenant-owned planning records.
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY['wbs_nodes','project_calendars','activities','activity_relationships','calendar_exceptions','schedule_runs','baselines','baseline_activity_dates']
  LOOP
    EXECUTE format('ALTER TABLE planning.%I ENABLE ROW LEVEL SECURITY', t);
    EXECUTE format('ALTER TABLE planning.%I FORCE ROW LEVEL SECURITY', t);
    EXECUTE format('CREATE POLICY %I ON planning.%I USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)', 'tenant_isolation_' || t, t);
  END LOOP;
END $$;
