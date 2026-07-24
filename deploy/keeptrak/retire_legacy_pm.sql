-- Retire the pre-KeepTrak (2010-era) PM definitions.
-- ================================================================
-- Run AFTER the KeepTrak import (tools/keeptrak-import.ps1), on the same schema.
--
-- WHY: ABIS's own PM module was abandoned in August 2010 but its 77 definitions still carry
-- nextduedate values from then. They are not status 0, so the due board treats them as ACTIVE and
-- they appear overdue by ~5,800 days — swamping the real KeepTrak PMs and making the board useless.
--
-- WHAT: sets pm_status = 0 (retired) on PM rows BELOW the import id offset. Nothing is deleted:
-- the definitions stay browsable and their 2,051 completions are untouched. The due board already
-- excludes status 0, so no application change is needed.
--
-- REVERSIBLE: the legacy rows did NOT all share one status (8x'1', 46x'2', 23x'3'), so a blanket
-- revert would lose that distinction. undo_retire_legacy_pm.sql restores each row's original value
-- individually — regenerate it BEFORE running this if the data has changed since:
--   SELECT 'UPDATE pm SET pm_status = '||pm_status||' WHERE pm_id = '||pm_id||';'
--     FROM pm WHERE pm_id < 100000 ORDER BY pm_id;

UPDATE pm SET pm_status = 0 WHERE pm_id < 100000 AND (pm_status IS NULL OR pm_status <> 0);
COMMIT;

-- Verify: this should return only KeepTrak rows.
--   SELECT CASE WHEN pm_id >= 100000 THEN 'KeepTrak' ELSE 'legacy' END, COUNT(*)
--     FROM pm WHERE (pm_status IS NULL OR pm_status <> 0) AND nextduedate IS NOT NULL
--    GROUP BY CASE WHEN pm_id >= 100000 THEN 'KeepTrak' ELSE 'legacy' END;
