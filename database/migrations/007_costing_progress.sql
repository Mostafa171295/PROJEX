-- PROJEX 007: cost control and performance snapshots (SRS 4.10-4.12, ERD sections 7-8). Run as projex_owner.
-- Deviations from ERD 1.0 are listed in docs/PROJEX_0.8.0_COST_CONTROL_EVM.md (e.g. reversal modelled as is_reversal on the original type).
CREATE SCHEMA IF NOT EXISTS costing;
CREATE SCHEMA IF NOT EXISTS progress;
GRANT USAGE ON SCHEMA costing, progress TO projex_app;
ALTER DEFAULT PRIVILEGES FOR ROLE projex_owner IN SCHEMA costing, progress
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO projex_app;

CREATE TABLE costing.cost_codes (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    parent_id uuid NULL,
    code varchar(80) NOT NULL,
    name varchar(200) NOT NULL,
    category varchar(30) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_cost_code_tenant_project_id UNIQUE (tenant_id, project_id, id),
    CONSTRAINT fk_cost_code_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_cost_code_parent FOREIGN KEY (tenant_id, project_id, parent_id)
        REFERENCES costing.cost_codes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_cost_code_not_self_parent CHECK (parent_id IS NULL OR parent_id <> id),
    CONSTRAINT ck_cost_code_category CHECK (category IN ('Labor','Material','Equipment','Subcontract','Overhead','Other'))
);
CREATE UNIQUE INDEX ux_cost_code_project_code ON costing.cost_codes(project_id, code);

CREATE TABLE costing.boq_items (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    cost_code_id uuid NULL,
    wbs_node_id uuid NULL,
    item_code varchar(80) NOT NULL,
    description text NOT NULL,
    unit varchar(30) NOT NULL,
    quantity numeric(20,4) NOT NULL,
    unit_rate numeric(20,4) NOT NULL,
    currency_code char(3) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_boq_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_boq_cost_code FOREIGN KEY (tenant_id, project_id, cost_code_id)
        REFERENCES costing.cost_codes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_boq_wbs FOREIGN KEY (tenant_id, project_id, wbs_node_id)
        REFERENCES planning.wbs_nodes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_boq_non_negative CHECK (quantity >= 0 AND unit_rate >= 0),
    CONSTRAINT ck_boq_currency CHECK (currency_code ~ '^[A-Z]{3}$')
);
CREATE UNIQUE INDEX ux_boq_project_item ON costing.boq_items(project_id, item_code);

CREATE TABLE costing.budget_versions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    version_number integer NOT NULL,
    type varchar(20) NOT NULL,
    status varchar(20) NOT NULL,
    effective_date date NOT NULL,
    created_by uuid NOT NULL,
    approved_by uuid NULL,
    approved_at timestamptz NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_budget_version_tenant_id UNIQUE (tenant_id, id),
    CONSTRAINT fk_budget_version_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_budget_version_number CHECK (version_number > 0),
    CONSTRAINT ck_budget_type CHECK (type IN ('Original','Revised','Forecast')),
    CONSTRAINT ck_budget_status CHECK (status IN ('Draft','InReview','Approved','Superseded')),
    CONSTRAINT ck_budget_approval CHECK ((status IN ('Approved','Superseded')) = (approved_by IS NOT NULL AND approved_at IS NOT NULL))
);
CREATE UNIQUE INDEX ux_budget_project_version ON costing.budget_versions(project_id, version_number);
-- At most one approved (current) budget per project; approving a new one first supersedes the old one.
CREATE UNIQUE INDEX ux_budget_one_approved_per_project ON costing.budget_versions(project_id) WHERE status = 'Approved';

CREATE TABLE costing.budget_lines (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    budget_version_id uuid NOT NULL,
    cost_code_id uuid NULL,
    wbs_node_id uuid NULL,
    activity_id uuid NULL,
    description varchar(250) NOT NULL,
    quantity numeric(20,4) NULL,
    unit_rate numeric(20,4) NULL,
    amount numeric(20,4) NOT NULL,
    currency_code char(3) NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_budget_line_version FOREIGN KEY (tenant_id, budget_version_id)
        REFERENCES costing.budget_versions (tenant_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_budget_line_amount CHECK (amount >= 0),
    CONSTRAINT ck_budget_line_currency CHECK (currency_code ~ '^[A-Z]{3}$')
);
CREATE INDEX ix_budget_line_version ON costing.budget_lines(budget_version_id);
CREATE INDEX ix_budget_line_activity ON costing.budget_lines(activity_id) WHERE activity_id IS NOT NULL;
-- Budget lines carry no project_id (ERD 7). Same-project checks for cost code / WBS / activity (ERD rule 5) are enforced
-- transactionally in the API and covered by integration tests.

CREATE TABLE costing.cost_transactions (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    cost_code_id uuid NULL,
    wbs_node_id uuid NULL,
    activity_id uuid NULL,
    transaction_type varchar(20) NOT NULL,
    is_reversal boolean NOT NULL DEFAULT false,
    reverses_transaction_id uuid NULL REFERENCES costing.cost_transactions(id) ON DELETE RESTRICT,
    document_reference varchar(100) NULL,
    description text NOT NULL,
    transaction_date date NOT NULL,
    amount numeric(20,4) NOT NULL,
    currency_code char(3) NOT NULL,
    status varchar(20) NOT NULL,
    idempotency_key varchar(100) NULL,
    rejection_reason text NULL,
    reversal_reason text NULL,
    created_by uuid NOT NULL,
    approved_by uuid NULL,
    approved_at timestamptz NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_cost_tx_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_cost_tx_cost_code FOREIGN KEY (tenant_id, project_id, cost_code_id)
        REFERENCES costing.cost_codes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_cost_tx_wbs FOREIGN KEY (tenant_id, project_id, wbs_node_id)
        REFERENCES planning.wbs_nodes (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_cost_tx_activity FOREIGN KEY (tenant_id, project_id, activity_id)
        REFERENCES planning.activities (tenant_id, project_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_cost_tx_type CHECK (transaction_type IN ('Actual','Commitment','Accrual')),
    CONSTRAINT ck_cost_tx_status CHECK (status IN ('Draft','Submitted','Approved','Rejected','Reversed')),
    CONSTRAINT ck_cost_tx_currency CHECK (currency_code ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_cost_tx_sign CHECK ((is_reversal AND amount < 0 AND reverses_transaction_id IS NOT NULL)
                                   OR (NOT is_reversal AND amount > 0 AND reverses_transaction_id IS NULL)),
    CONSTRAINT ck_cost_tx_approval CHECK ((status IN ('Approved','Reversed')) = (approved_by IS NOT NULL AND approved_at IS NOT NULL))
);
CREATE INDEX ix_cost_tx_project_date_status ON costing.cost_transactions(project_id, transaction_date, status);
CREATE UNIQUE INDEX ux_cost_tx_idempotency ON costing.cost_transactions(project_id, idempotency_key) WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX ux_cost_tx_one_reversal ON costing.cost_transactions(reverses_transaction_id) WHERE reverses_transaction_id IS NOT NULL;

CREATE TABLE progress.performance_snapshots (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    project_id uuid NOT NULL,
    data_date date NOT NULL,
    baseline_id uuid NULL,
    budget_version_id uuid NULL,
    pv numeric(20,4) NOT NULL,
    ev numeric(20,4) NOT NULL,
    ac numeric(20,4) NOT NULL,
    bac numeric(20,4) NOT NULL,
    eac numeric(20,4) NULL,
    etc numeric(20,4) NULL,
    vac numeric(20,4) NULL,
    cv numeric(20,4) NOT NULL,
    sv numeric(20,4) NOT NULL,
    cpi numeric(12,6) NULL,
    spi numeric(12,6) NULL,
    eac_method varchar(40) NOT NULL,
    budget_coverage numeric(7,6) NOT NULL,
    calculated_by uuid NOT NULL,
    calculated_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT fk_snapshot_project FOREIGN KEY (tenant_id, project_id)
        REFERENCES projects.projects (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_snapshot_coverage CHECK (budget_coverage BETWEEN 0 AND 1)
);
CREATE INDEX ix_snapshot_project_date ON progress.performance_snapshots(project_id, data_date DESC);
-- ERD: unique (project, data_date, baseline) with nullable baseline handled explicitly.
CREATE UNIQUE INDEX ux_snapshot_project_date_baseline
    ON progress.performance_snapshots(project_id, data_date, COALESCE(baseline_id, '00000000-0000-0000-0000-000000000000'::uuid));
-- Snapshots are append-only for the application role.
REVOKE UPDATE, DELETE ON progress.performance_snapshots FROM projex_app;

-- ---------- Immutability guards (defence in depth; the domain model enforces the same rules) ----------
CREATE FUNCTION costing.guard_budget_lines() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v uuid; s text;
BEGIN
    IF TG_OP = 'DELETE' THEN v := OLD.budget_version_id; ELSE v := NEW.budget_version_id; END IF;
    SELECT status INTO s FROM costing.budget_versions WHERE id = v;
    IF s IS DISTINCT FROM 'Draft' THEN
        RAISE EXCEPTION 'budget lines can change only while the budget version is Draft' USING ERRCODE = '23000';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_budget_lines_guard BEFORE INSERT OR UPDATE OR DELETE ON costing.budget_lines
    FOR EACH ROW EXECUTE FUNCTION costing.guard_budget_lines();

CREATE FUNCTION costing.guard_budget_versions() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'Draft' THEN RAISE EXCEPTION 'only a draft budget version can be deleted' USING ERRCODE = '23000'; END IF;
        RETURN OLD;
    END IF;
    IF OLD.status IN ('Approved','Superseded') THEN
        IF NOT (OLD.status = 'Approved' AND NEW.status = 'Superseded'
                AND to_jsonb(NEW) - 'status' - 'updated_at' - 'row_version' = to_jsonb(OLD) - 'status' - 'updated_at' - 'row_version') THEN
            RAISE EXCEPTION 'an approved budget version is immutable; only Approved -> Superseded is allowed' USING ERRCODE = '23000';
        END IF;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_budget_versions_guard BEFORE UPDATE OR DELETE ON costing.budget_versions
    FOR EACH ROW EXECUTE FUNCTION costing.guard_budget_versions();

CREATE FUNCTION costing.guard_cost_transactions() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'Draft' THEN RAISE EXCEPTION 'only a draft cost transaction can be deleted; post a reversal instead' USING ERRCODE = '23000'; END IF;
        RETURN OLD;
    END IF;
    IF OLD.status IN ('Approved','Reversed','Rejected') THEN
        IF NOT (OLD.status = 'Approved' AND NEW.status = 'Reversed'
                AND to_jsonb(NEW) - 'status' - 'reversal_reason' - 'updated_at' - 'row_version'
                  = to_jsonb(OLD) - 'status' - 'reversal_reason' - 'updated_at' - 'row_version') THEN
            RAISE EXCEPTION 'an approved, reversed or rejected cost transaction is immutable; only Approved -> Reversed is allowed' USING ERRCODE = '23000';
        END IF;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_cost_transactions_guard BEFORE UPDATE OR DELETE ON costing.cost_transactions
    FOR EACH ROW EXECUTE FUNCTION costing.guard_cost_transactions();

CREATE FUNCTION progress.forbid_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'performance snapshots are append-only' USING ERRCODE = '42501';
END $$;
CREATE TRIGGER trg_snapshot_no_change BEFORE UPDATE OR DELETE ON progress.performance_snapshots
    FOR EACH ROW EXECUTE FUNCTION progress.forbid_change();

-- ---------- Tenant isolation (same policy shape as 002/005) ----------
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY['costing.cost_codes','costing.boq_items','costing.budget_versions','costing.budget_lines',
                           'costing.cost_transactions','progress.performance_snapshots']
  LOOP
    EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY', t);
    EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY', t);
    EXECUTE format('CREATE POLICY %I ON %s USING (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'', true), '''')::uuid)',
                   'tenant_isolation_' || replace(t, '.', '_'), t);
  END LOOP;
END $$;
