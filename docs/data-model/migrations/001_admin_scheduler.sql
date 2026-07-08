-- Migration 001 — Admin scheduler registry (ABIS-owned tables)
-- ================================================================
-- Apply once on the ABIS Oracle schema (DBO on 192.168.1.230/abc11, and later the
-- cutover target) BEFORE the /api/admin/jobs endpoints are exercised on Oracle. These
-- are NEW ABIS-owned tables (not part of the legacy schema), so they must be created
-- explicitly — nothing in the app creates them at startup.
--
-- ⚠️  INERT BY DESIGN. These tables only STORE scheduled-job definitions + run history so
--     they can be viewed/managed in ABIS. There is NO execution engine in this phase: ABIS
--     never fires a job from this registry. The legacy db01 crontab remains the sole live
--     owner of all scheduled work until a single-owner cutover (legacy OFF before new ON).
--     See docs/ADMIN_SUBSYSTEM_PLAN.md #6 and the no-live-firing guardrail.
--
-- The app assigns scheduled_job_id / job_run_id with a portable MAX(id)+1 inside a
-- transaction (this is a low-write admin table), so no Oracle sequence is required. Add one
-- later if the write rate ever warrants it.

CREATE TABLE abis_scheduled_job (
  scheduled_job_id  NUMBER(10)     NOT NULL,
  job_name          VARCHAR2(100)  NOT NULL,
  job_description   VARCHAR2(1000),
  cron_expression   VARCHAR2(128)  NOT NULL,
  target_operation  VARCHAR2(100),
  target_args       VARCHAR2(4000),
  enabled           NUMBER(1)      DEFAULT 0 NOT NULL,
  source            VARCHAR2(30),
  created_utc       DATE,
  updated_utc       DATE,
  CONSTRAINT pk_abis_scheduled_job PRIMARY KEY (scheduled_job_id)
);

-- Job names are unique case-insensitively (mirrors the app's LOWER(job_name) dup check).
CREATE UNIQUE INDEX ux_abis_scheduled_job_name ON abis_scheduled_job (UPPER(job_name));

CREATE TABLE abis_job_run (
  job_run_id        NUMBER(12)     NOT NULL,
  scheduled_job_id  NUMBER(10)     NOT NULL,
  started_utc       DATE,
  finished_utc      DATE,
  run_status        VARCHAR2(20),
  affected_count    NUMBER(12),
  error_text        VARCHAR2(4000),
  correlation_id    VARCHAR2(64),
  CONSTRAINT pk_abis_job_run PRIMARY KEY (job_run_id),
  CONSTRAINT fk_abis_job_run_job FOREIGN KEY (scheduled_job_id)
    REFERENCES abis_scheduled_job (scheduled_job_id)
);

CREATE INDEX ix_abis_job_run_job ON abis_job_run (scheduled_job_id);
