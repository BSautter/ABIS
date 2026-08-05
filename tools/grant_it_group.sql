-- Give the IT group Write on every ABIS feature.
--
-- WHY THIS SCRIPT EXISTS
-- The plant's instruction (2026-08-05) is that IT holds full read/write/modify on every component of
-- ABIS. It was applied on .230, but a Data Pump refresh imports every DBO table except the ABIS_*
-- modernization ones — and SECURITY_GROUP_APPLICATION is a DBO table. So the refresh silently reverts
-- it to whatever prod has, exactly like the admin login that Part 3 of the refresh runbook restores.
-- Run this after every refresh, alongside that step.
--
-- WHAT IT DOES
-- Ensures one row per (IT group, feature) at privilege 1 (Write). Idempotent: an existing grant is
-- raised to Write, a missing one is inserted, and nothing else is touched. It never removes a grant
-- and never touches another group.
--
-- SAFETY
-- Non-prod (.230) only. Prod (.9) is read-only by policy — do NOT run this there. Legacy owns the
-- production security tables and changing them is a plant decision made through legacy ABIS.
--
--   sqlplus dbo/<pw>@192.168.1.230:1521/abc11 @tools/grant_it_group.sql

SET SERVEROUTPUT ON

DECLARE
  v_group_id  security_group.user_group_id%TYPE;
  v_raised    PLS_INTEGER := 0;
  v_added     PLS_INTEGER := 0;
  v_held      PLS_INTEGER := 0;
  v_total     PLS_INTEGER := 0;
BEGIN
  -- Resolve the group by NAME, not by a hard-coded id: the id is 10 on .230 today, but after a
  -- refresh it is whatever prod has, and granting the wrong group would be both wrong and quiet.
  BEGIN
    SELECT user_group_id INTO v_group_id
      FROM security_group
     WHERE UPPER(TRIM(group_name)) = 'IT';
  EXCEPTION
    WHEN NO_DATA_FOUND THEN
      RAISE_APPLICATION_ERROR(-20001,
        'No security_group named IT. Create it (or correct the name) before running this.');
    WHEN TOO_MANY_ROWS THEN
      RAISE_APPLICATION_ERROR(-20002,
        'More than one security_group named IT — resolve the duplicate first.');
  END;

  -- Raise anything already granted but below Write.
  UPDATE security_group_application
     SET group_application_privilege = 1
   WHERE user_group_id = v_group_id
     AND (group_application_privilege IS NULL OR group_application_privilege <> 1);
  v_raised := SQL%ROWCOUNT;

  -- Add whatever is missing.
  INSERT INTO security_group_application (user_group_id, application_id, group_application_privilege)
  SELECT v_group_id, a.application_id, 1
    FROM security_application a
   WHERE NOT EXISTS (SELECT 1
                       FROM security_group_application ga
                      WHERE ga.user_group_id = v_group_id
                        AND ga.application_id = a.application_id);
  v_added := SQL%ROWCOUNT;

  COMMIT;

  DBMS_OUTPUT.PUT_LINE('IT group id      : ' || v_group_id);
  DBMS_OUTPUT.PUT_LINE('grants raised    : ' || v_raised);
  DBMS_OUTPUT.PUT_LINE('grants added     : ' || v_added);

  -- Counted into variables rather than one SELECT: 11g rejects a scalar subquery beside an
  -- aggregate with no GROUP BY (ORA-00937), and this script has to run on the plant's 11g.
  SELECT COUNT(*) INTO v_held
    FROM security_group_application
   WHERE user_group_id = v_group_id
     AND group_application_privilege = 1;

  SELECT COUNT(*) INTO v_total FROM security_application;

  DBMS_OUTPUT.PUT_LINE('IT holds at Write: ' || v_held || ' of ' || v_total || ' features');
  IF v_held <> v_total THEN
    RAISE_APPLICATION_ERROR(-20003,
      'IT does not hold every feature at Write after running - investigate before relying on this.');
  END IF;
END;
/
