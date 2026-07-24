-- resync_sequences.sql — advance each DBO id sequence to MAX(id)+1 of the table it feeds.
--
-- WHY: after a Data Pump refresh of the non-prod DB (.230) from prod (.9), the transactional
-- sequences land BEHIND their imported table data — the export is not sequence-consistent, so
-- NEXTVAL returns a value that already exists and every id-minting INSERT fails with ORA-00001
-- (unique constraint violated). Confirmed 2026-07-24: 13 of 18 sequences behind, COIL_ABC_NUM_SEQ
-- by 877,220. Low-churn config sequences (customer/carrier/die/sketch) drift slowly and were fine.
--
-- This is the mandatory post-refresh step. It is idempotent (a sequence already ahead is skipped)
-- and safe to re-run. Oracle 11g-compatible: it uses the INCREMENT BY jump rather than 12c RESTART.
--
-- RUN AS DBO on the target DB:  sqlplus dbo/<pw>@//192.168.1.230:1521/abc11 @tools/resync_sequences.sql
-- READ-ONLY DRY RUN: set p_apply := FALSE below to only report the gaps without altering anything.

SET SERVEROUTPUT ON SIZE UNLIMITED
DECLARE
  p_apply CONSTANT BOOLEAN := TRUE;   -- FALSE = report only, change nothing

  -- (sequence, table, id column) for every sequence the modern app mints ids from. Keep in step
  -- with AbisRepository.NextIdAsync usages + Database:Sequences overrides in appsettings.json.
  TYPE t_map IS TABLE OF VARCHAR2(128);
  seqs  t_map := t_map(
    'AB_JOB_NUM_SEQ','CARRIER_ID_SEQ','COIL_ABC_NUM_SEQ','CUSTOMER_ID_SEQ','CUSTOMER_CONTACT_ID_SEQ',
    'ORDER_ABC_NUM_SEQ','DIE_ID_SEQ','DT_INSTANCE_SEQ','ERROR_EVT_SEQ','EDI_FILE_ID_SEQ',
    'PART_NUM_ID_SEQ','RECEIVING_BOL_ID_SEQ','RETURN_SCRAP_ITEM_ID_SEQ','SCAN_LOG_SEQ',
    'SCRAP_SKID_NUM_SEQ','SHEET_SKID_NUM_SEQ','SHEET_SKID_DIMENSION_CHECK_SEQ','SHIFT_NUM_SEQ',
    'SKETCH_ID_SEQ','PACKING_LIST_NUM_SEQ','COIL_OWNER_TRANSFER_CERTIF_SEQ');
  tbls  t_map := t_map(
    'AB_JOB','CARRIER','COIL','CUSTOMER','CUSTOMER_CONTACT',
    'CUSTOMER_ORDER','DIE','DT_INSTANCE','ERROR_EVT','OUTBOUND_EDI_TRANSACTION',
    'PART_NUM','RECEIVING_BOL','RETURN_SCRAP_ITEM','SCAN_LOG',
    'SCRAP_SKID','SHEET_SKID','SHEET_SKID_DIMENSION_CHECK','SHIFT',
    'SKETCH','SHIPMENT','COIL_OWNERSHIP_TRANSFER');
  cols  t_map := t_map(
    'AB_JOB_NUM','CARRIER_ID','COIL_ABC_NUM','CUSTOMER_ID','CONTACT_ID',
    'ORDER_ABC_NUM','DIE_ID','INSTANCE_NUM','ERROR_EVT_ID','EDI_FILE_ID',
    'PART_NUM_ID','RECEIVING_BOL_ID','RETURN_SCRAP_ITEM_NUM','SCAN_ID',
    'SCRAP_SKID_NUM','SHEET_SKID_NUM','DIMENSION_CHECK_NUM','SHIFT_NUM',
    'SKETCH_ID','PACKING_LIST','CERTIFICATE_NUM');

  v_max NUMBER; v_cur NUMBER; v_gap NUMBER; v_bumped PLS_INTEGER := 0;
BEGIN
  FOR i IN 1 .. seqs.COUNT LOOP
    BEGIN
      EXECUTE IMMEDIATE 'SELECT NVL(MAX('||cols(i)||'),0) FROM '||tbls(i) INTO v_max;
      EXECUTE IMMEDIATE 'SELECT '||seqs(i)||'.NEXTVAL FROM dual' INTO v_cur;   -- consumes one; the jump accounts for it
      v_gap := v_max - v_cur;   -- >= 0 means the just-consumed value is <= an existing id: behind
      IF v_gap >= 0 THEN
        DBMS_OUTPUT.PUT_LINE(RPAD(seqs(i),34)||' BEHIND: next was '||(v_cur)||', table max '||v_max||
                             CASE WHEN p_apply THEN '  -> bumped to '||(v_max+1) ELSE '  (dry run)' END);
        IF p_apply THEN
          -- jump the sequence to table max + 1, then restore INCREMENT BY 1
          EXECUTE IMMEDIATE 'ALTER SEQUENCE '||seqs(i)||' INCREMENT BY '||(v_gap+1);
          EXECUTE IMMEDIATE 'SELECT '||seqs(i)||'.NEXTVAL FROM dual' INTO v_cur;
          EXECUTE IMMEDIATE 'ALTER SEQUENCE '||seqs(i)||' INCREMENT BY 1';
          v_bumped := v_bumped + 1;
        END IF;
      END IF;
    EXCEPTION WHEN OTHERS THEN
      DBMS_OUTPUT.PUT_LINE(RPAD(seqs(i),34)||' SKIPPED: '||SQLERRM);
    END;
  END LOOP;
  DBMS_OUTPUT.PUT_LINE('--- '||CASE WHEN p_apply THEN v_bumped||' sequence(s) re-synced.' ELSE 'dry run — nothing changed.' END);
END;
/
