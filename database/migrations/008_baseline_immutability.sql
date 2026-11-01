-- PROJEX 008: enforce SRS FR-BASE-004 in the database. An approved baseline (and its dates) can no longer be edited
-- directly; the only allowed change to an approved baseline is Approved -> Superseded. Run as projex_owner.
CREATE FUNCTION planning.guard_baselines() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'Draft' THEN RAISE EXCEPTION 'only a draft baseline can be deleted' USING ERRCODE = '23000'; END IF;
        RETURN OLD;
    END IF;
    IF OLD.status IN ('Approved','Superseded') THEN
        IF NOT (OLD.status = 'Approved' AND NEW.status = 'Superseded'
                AND to_jsonb(NEW) - 'status' - 'updated_at' = to_jsonb(OLD) - 'status' - 'updated_at') THEN
            RAISE EXCEPTION 'an approved baseline is immutable; create a new baseline version' USING ERRCODE = '23000';
        END IF;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_baselines_guard BEFORE UPDATE OR DELETE ON planning.baselines
    FOR EACH ROW EXECUTE FUNCTION planning.guard_baselines();

CREATE FUNCTION planning.guard_baseline_dates() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE b uuid; s text;
BEGIN
    IF TG_OP = 'DELETE' THEN b := OLD.baseline_id; ELSE b := NEW.baseline_id; END IF;
    SELECT status INTO s FROM planning.baselines WHERE id = b;
    -- A missing parent means the baseline itself is being removed by cascade (only possible for drafts).
    IF s IS NOT NULL AND s <> 'Draft' THEN
        RAISE EXCEPTION 'baseline dates can change only while the baseline is Draft' USING ERRCODE = '23000';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER trg_baseline_dates_guard BEFORE INSERT OR UPDATE OR DELETE ON planning.baseline_activity_dates
    FOR EACH ROW EXECUTE FUNCTION planning.guard_baseline_dates();
