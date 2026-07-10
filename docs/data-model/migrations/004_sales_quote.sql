-- 004_sales_quote.sql — re-provision the sales-quote subsystem.
--
-- These tables (legacy w_sales_main / w_new_quote / w_edit_quote) were RETIRED from the ABIS schema —
-- they exist in no current database — so the modernization re-provisions and owns them. The app does
-- this automatically at startup (AbisSchema.EnsureOwnedTablesAsync); this script is the canonical DDL
-- for manual/DBA use. Idempotent by intent: skip statements for objects that already exist.
--
-- Column names are the authoritative legacy dbnames (d_sales_quote_modify). Ids are assigned MAX(id)+1
-- (Database:MaxIdTables) — no legacy sequence exists — so no *_id_seq is created here. sales_quote has a
-- composite key (quote_id + quote_revision_id): revisions of one quote share quote_id; a new quote is
-- revision 1. sales_reminder / sales_probability get a surrogate id for the modern write path.

CREATE TABLE sales_quote (
  quote_id            NUMBER(10)     NOT NULL,
  quote_revision_id   NUMBER(6)      NOT NULL,
  customer_id         NUMBER(10),
  contact_id          NUMBER(10),
  enduser_id          NUMBER(10),
  end_use             VARCHAR2(120),
  part_shape          VARCHAR2(60),
  material            VARCHAR2(60),
  alloy               VARCHAR2(30),
  temper              VARCHAR2(30),
  gauge               NUMBER(10,5),
  width               NUMBER(12,4),
  length              NUMBER(12,4),
  line_num            NUMBER(6),
  line_speed          NUMBER(12,3),
  num_of_coil         NUMBER(8),
  num_of_skid         NUMBER(8),
  total_lb_processed  NUMBER(16,2),
  total_rev_per_hr    NUMBER(16,4),
  variable_cost       NUMBER(16,4),
  fixed_cost          NUMBER(16,4),
  reg_process_charge  NUMBER(16,4),
  ros                 NUMBER(12,4),
  quote_notes         VARCHAR2(2000),
  approval_sales      VARCHAR2(30),
  approval_vp         VARCHAR2(30),
  approval_ceo        VARCHAR2(30),
  pass_on_quote       VARCHAR2(1),
  created_date        DATE,
  valid_date          DATE,
  CONSTRAINT pk_sales_quote PRIMARY KEY (quote_id, quote_revision_id));

CREATE TABLE sales_reminder (
  event_id            NUMBER(12)     NOT NULL,
  quote_id            NUMBER(10),
  quote_revision_id   NUMBER(6),
  event_date          DATE,
  event_notes         VARCHAR2(2000),
  event_status        VARCHAR2(20),
  user_id             VARCHAR2(64),
  CONSTRAINT pk_sales_reminder PRIMARY KEY (event_id));

CREATE TABLE sales_probability (
  probability_id      NUMBER(12)     NOT NULL,
  quote_id            NUMBER(10),
  quote_revision_id   NUMBER(6),
  review_date         DATE,
  sales_probability   NUMBER(5),
  probability_note    VARCHAR2(2000),
  CONSTRAINT pk_sales_probability PRIMARY KEY (probability_id));

CREATE INDEX ix_sales_reminder_quote    ON sales_reminder    (quote_id, quote_revision_id);
CREATE INDEX ix_sales_probability_quote ON sales_probability (quote_id, quote_revision_id);
