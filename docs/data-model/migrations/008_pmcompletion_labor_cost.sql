-- Migration 008 — PM completion labour hours + cost
-- ================================================================
-- Apply once on the ABIS Oracle schema (DBO on 192.168.1.230/abc11, and later the cutover
-- target) BEFORE importing KeepTrak completion history.
--
-- ⚠️  THIS IS THE FIRST MIGRATION THAT ALTERS A **LEGACY** TABLE.
--     Migrations 001–007 only CREATED new ABIS-owned tables (abis_*, sales_*), which is
--     inherently safe. This one adds columns to PMCOMPLETIONS, which the legacy PowerBuilder
--     application also reads. Adding NULLable columns is backward-compatible — legacy
--     SELECTs name their columns and DataWindows bind by name, so they are unaffected — but
--     it is a change to shared ground and deserves the extra care:
--       * run it on the NON-PROD sandbox (.230) first and exercise the PM pages,
--       * never run it against live prod (.9) as part of a test,
--       * it is additive only: no column is dropped, renamed, retyped, or reordered.
--
-- WHY
--   KeepTrak (the Access maintenance system ABIS is replacing — see docs/KEEPTRAK_MIGRATION.md)
--   records labour hours and a cost on EVERY completion: t_PM_Completions.fld_LaborHours and
--   .fc_Cost, across 13,648 rows going back to 2021. ABIS's PMCOMPLETIONS had nowhere to put
--   either, so importing without these columns would permanently discard real maintenance-cost
--   history. These two columns preserve it and keep it queryable (summable per PM, per asset,
--   per period) rather than stranded in free text.
--
-- SHAPE
--   labor_hours  NUMBER — hours worked on that completion (KeepTrak fld_LaborHours, a Double).
--   comp_cost    NUMBER — cost of that completion (KeepTrak fc_Cost, a Currency).
--   Both NULLable: every pre-existing ABIS row, and any completion recorded without them,
--   simply has no value. NULL means "not recorded" — deliberately distinct from 0 ("free").
--
--   Named comp_cost rather than cost: COST is not reserved in Oracle, but the surrounding
--   schema already uses prefixed names (pm_cost, prob_cost, scrap_cost), and an unprefixed
--   COST reads ambiguously in a join against PM.PM_COST (the ESTIMATE) — these are different
--   numbers and should not be confusable at a glance.
--
-- IDEMPOTENCY
--   Re-running is safe: each ADD is guarded by a check against USER_TAB_COLS, so a second run
--   is a no-op instead of ORA-01430 (column being added already exists).

DECLARE
  n PLS_INTEGER;
BEGIN
  SELECT COUNT(*) INTO n FROM user_tab_cols
   WHERE table_name = 'PMCOMPLETIONS' AND column_name = 'LABOR_HOURS';
  IF n = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE pmcompletions ADD (labor_hours NUMBER)';
    DBMS_OUTPUT.PUT_LINE('added PMCOMPLETIONS.LABOR_HOURS');
  ELSE
    DBMS_OUTPUT.PUT_LINE('PMCOMPLETIONS.LABOR_HOURS already present - skipped');
  END IF;

  SELECT COUNT(*) INTO n FROM user_tab_cols
   WHERE table_name = 'PMCOMPLETIONS' AND column_name = 'COMP_COST';
  IF n = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE pmcompletions ADD (comp_cost NUMBER)';
    DBMS_OUTPUT.PUT_LINE('added PMCOMPLETIONS.COMP_COST');
  ELSE
    DBMS_OUTPUT.PUT_LINE('PMCOMPLETIONS.COMP_COST already present - skipped');
  END IF;
END;
/

-- Verify:
--   SELECT column_name, data_type, nullable FROM user_tab_cols
--    WHERE table_name = 'PMCOMPLETIONS' AND column_name IN ('LABOR_HOURS','COMP_COST');
