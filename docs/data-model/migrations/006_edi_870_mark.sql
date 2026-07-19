-- 006_edi_870_mark.sql — the ABIS-owned 870 "sent" markers.
--
-- The legacy edi_aleris_870 proc records that an item/job has been reported on an 870 by stamping
-- production_sheet_item.prod_item_edi870_date / edi_file_id_870 and ab_job.scrap_870_date. Those are legacy
-- columns the modernization does NOT alter, so the modern 870 engine tracks the same state here instead:
-- one row per (mark_type, ref_id) — mark_type 'ITEM' → prod_item_num, 'SCRAP' → ab_job_num. The 870 batch
-- selection excludes anything already marked, so each item/job is reported at most once.
--
-- The app provisions this automatically at startup (AbisSchema.EnsureOwnedTablesAsync); this script is the
-- canonical DDL for manual/DBA use. Idempotent by intent. GENERATION ONLY — nothing here transmits.

CREATE TABLE abis_edi_870_mark (
  mark_type     VARCHAR2(8)   NOT NULL,   -- 'ITEM' (prod_item_num) or 'SCRAP' (ab_job_num)
  ref_id        NUMBER(12)    NOT NULL,
  edi_file_id   NUMBER(12),
  customer_id   NUMBER(10),
  sent_utc      DATE,
  CONSTRAINT pk_abis_edi_870_mark PRIMARY KEY (mark_type, ref_id));
