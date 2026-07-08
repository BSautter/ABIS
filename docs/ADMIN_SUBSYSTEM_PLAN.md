# ABIS — Admin Subsystem Plan (scheduler, server ops, EDI ownership)

A design plan for a **new Admin area** in ABIS that consolidates three requested
capabilities that don't exist yet:

- **#6 Scheduled tasks** — pull the scheduled jobs off the DB server into an
  ABIS-owned scheduler, so they aren't managed on the database host anymore.
- **#7 Server/service console** — manage the ABIS deployment (and *view* the jobs
  that can't be imported) from an admin UI instead of the shell.
- **#8 EDI ownership** — run the full EDI process through the ABIS server, with an
  EDI **setup UI** under the admin pages.

These interlock: **#6 is the substrate #8's automation rides on**, and both are
surfaced through the **#7 admin shell**. They extend the existing security /
`RequireFeatureAsync` authorization (see [`../api/README.md`](../api/README.md)
§Authentication) and the strangler-fig discipline in
[`PHASE4_CUTOVER_PLAN.md`](PHASE4_CUTOVER_PLAN.md). This plan slots into
[`NEXT_STEPS.md`](NEXT_STEPS.md) as workstreams #6–#8.

## Decisions on record (2026-07-07)

| Decision | Choice |
|---|---|
| Cron scope | **Discover it** — we do not yet know whether the real jobs are Oracle Scheduler / `DBMS_JOB`, OS crontab on the DB host, or both. Enumeration is step 1. |
| Admin console blast radius | **View + safe restarts only** — read health/logs/config and restart `abis` + nginx. No stop, no config edits, no arbitrary shell. |
| Admin targets | **ABIS app server *and* the DB host** — manage ABIS on `codi-ABIS`; also view/manage the jobs & cron that stay on the Oracle DB host. |

## Deployment facts this builds on

- ABIS runs as systemd service **`abis`** on `127.0.0.1:8080`, fronted by **nginx**
  on `:80`, on the Ubuntu box **`codi-ABIS` (192.168.3.110)**. Config in
  `/etc/abis/abis.env`; install state in `/etc/abis/install.state`. (See the native
  deploy notes.)
- The DB is the non-prod Oracle at **`192.168.1.230:1521/abc11`**, schema owner `DBO`.
  ABIS already holds a DB connection to it — **in-database jobs can be inventoried
  and controlled over that existing connection, with no new privileged channel.**

## Server topology + read-only guardrails  *(critical — confirmed 2026-07-07)*

Discovery for #6/#7 spans multiple boxes. **Two of them are live and STRICTLY
READ-ONLY** — treat any write / job-control / cron-edit / service-restart against
them as forbidden.

| Host | Role | This subsystem's access |
|---|---|---|
| `192.168.1.230:1521/abc11` | **Non-prod Oracle** (`DBO`) — dev/validation target | Read + write OK (sandbox) |
| `192.168.1.9` | **CURRENTLY RUNNING PRODUCTION** | **READ-ONLY for everything** — jobs, tasks, cron, all of it |
| `192.168.1.11` | **Dev server** — most **EDI** lives here (#8) | **READ-ONLY** — be very careful |
| `codi-ABIS` (192.168.3.110) | The ABIS app server itself | Manage per #7 (view + safe restarts only) |

Rules baked into this plan:
- **No writes of any kind** to `192.168.1.9` or `192.168.1.11`. Job/cron discovery
  there = read-only SELECTs / `crontab -l` / read-only dumps, and only with explicit
  go-ahead per run.
- **No agent fan-out / Workflow pointed at the live prod or dev boxes** — restraint
  over parallelism on live infra.
- Credentials are **per-session** (never committed to repo or memory); the box↔account
  mapping is confirmed interactively before connecting (there was some ambiguity in the
  handed-over usernames/passwords). See the server-inventory memory.

---

## Step 0 (blocking): discover the real scheduled jobs

We can't design the import until we know what's actually scheduled and where. **Do
not trust the schema dump here** — the `SMP_JOB` / `SMAGENTJOB` / `SMPARALLELJOB` /
`SMP_JOB_HISTORY` tables in [`data-model/oracle_ddl.sql`](data-model/oracle_ddl.sql)
are leftover **Oracle Enterprise Manager** (Intelligent Agent) repository objects,
**not** the plant's business cron. Ignore them.

Enumerate from the live system — **read-only**, and note the real jobs almost
certainly live on **prod `192.168.1.9`** and the **dev/EDI box `192.168.1.11`**, not
on the non-prod DB we validate against. Run each of these strictly read-only, with
explicit go-ahead, honouring the guardrails above:

1. **In-database jobs** — read-only SELECTs (safe on prod: pure reads of data-dictionary
   views):
   ```sql
   SELECT job_name, job_action, schedule_type, repeat_interval, enabled,
          state, last_start_date, next_run_date
     FROM dba_scheduler_jobs WHERE owner NOT IN ('SYS','SYSMAN','ORACLE_OCM','DBSNMP');
   SELECT job, what, interval, last_date, next_date, broken FROM dba_jobs;   -- legacy DBMS_JOB
   ```
2. **OS crontab on each host** — `crontab -l` per relevant user + read `/etc/cron.*`,
   `/etc/crontab` (needs a **read-only** shell channel — see #7; **no edits on `.9`/`.11`**).
   Capture what each entry runs (scripts, `sqlplus @...`, EDI dispatch, backups).

Produce a **job inventory** (`docs/data-model/JOB_INVENTORY.md`) classifying each:

| Class | Meaning | Disposition |
|---|---|---|
| **Importable** | Business logic ABIS can own — EDI send/poll, report generation, data rollups, housekeeping deletes | Re-home into the ABIS scheduler (#6) |
| **Stay-on-DB** | DB-internal maintenance — stats gathering, RMAN/backup, partition maintenance | Leave in place; **view/monitor** from the admin console (#7) |
| **Obsolete** | OEM/agent remnants, disabled/broken jobs | Document; propose removal with DBA sign-off |

This inventory is the input to #6 and #7. It rides on the same DB access we're
standing up for the Oracle validation sweep.

---

## #6 — Cron import + ABIS-native scheduler

**Goal:** the importable jobs run *inside* ABIS on a schedule, managed from the UI,
so nobody edits crontab on the DB box.

### Engine
A hosted background service in the `abis` process — a cron-expression scheduler
(**Quartz.NET**, or a lightweight `IHostedService` timer loop if the job set is
small). It executes jobs as **ABIS operations** (invoke repository/service logic
directly, the same code the API endpoints call) rather than shelling out.

### Data model (new tables, ABIS-owned — not legacy)
- `abis_scheduled_job` — id, name, description, cron expression, target operation +
  args, enabled, created/updated audit.
- `abis_job_run` — job id, started/finished, status (ok/failed/running), row/affect
  counts, error text, correlation id → surfaces run history + drives alerting.

### API (under the admin group, gated)
`GET/POST/PUT /api/admin/jobs`, `POST /api/admin/jobs/{id}/{enable,disable,run-now}`,
`GET /api/admin/jobs/{id}/runs`. All mutations authorized by an `admin.scheduler`
feature and audited.

### Migration (strangler-fig, per job)
1. Re-implement the importable job as an ABIS scheduled task.
2. Run it in ABIS **in parallel** (shadow) with the original still active; compare
   outputs / side-effects for a soak window.
3. **Disable the original** on the DB host / in the DB scheduler (don't delete —
   keep as instant rollback).
4. Retire the original after the ABIS task is proven.

### Concurrency note
The `abis` service is single-instance today, so in-process scheduling is safe. If it
is ever scaled out, add a DB lease/lock (`SELECT … FOR UPDATE` on a scheduler-lock
row) so a job fires once cluster-wide.

---

## #7 — Server / service admin console  *(view + safe restarts only)*

**Goal:** run the deployment from the admin UI instead of SSH, and *see* the
jobs/cron that stay on the DB host.

### Capabilities (deliberately minimal)
- **ABIS box (`codi-ABIS`):** view service status/health/version, read (never edit)
  `abis.env`/`install.state`, tail `abis` + nginx logs, and **restart** `abis` /
  reload nginx. No stop, no config edits, no arbitrary commands.
- **DB host:** **view** the stay-on-DB jobs and host crontab; **manage** the
  in-database jobs that remain there via `enable/disable/run-now`
  (`DBMS_SCHEDULER.enable/disable/run_job`) — done over the **existing DB
  connection**, so no shell privilege is needed for those.

### Security design (the hard part)
The web process must **not** run arbitrary `systemctl`. Privilege separation:
- **ABIS box:** a tightly scoped **sudoers NOPASSWD allowlist** for the `abis` user
  limited to the *exact* commands (`systemctl restart abis`, `systemctl reload nginx`,
  `journalctl -u abis -n …`) — or equivalent polkit rules for those specific unit
  actions. The API handler invokes only those fixed commands; no user input reaches a
  shell. Every action requires an `admin.server` feature grant and is audited.
- **DB host shell (for crontab *viewing*):** a restricted SSH channel with a
  `command="…"`-locked key that runs only `crontab -l` / a read-only cron dump — or,
  preferably, a small read-only **`abis-hostinfo` helper** deployed on the DB host
  exposing just that data. **No write path to the DB host's shell in this phase**
  (matches "view + safe restarts only").
- In-DB job control needs **no new channel** — it's SQL over the connection ABIS
  already has, gated on `admin.scheduler`.

### API
`GET /api/admin/services` (status), `POST /api/admin/services/{unit}/restart`
(allowlisted units only), `GET /api/admin/services/{unit}/logs?tail=`,
`GET /api/admin/host/cron` (read-only DB-host cron view). All under admin auth +
audit.

### Open discovery for #7
- **DB host OS + access method** (Linux assumed for crontab; confirm) and whether a
  restricted SSH key or a small read-only helper is preferred for the cron view.

---

## #8 — Full EDI ownership + setup UI (under admin)

**Goal:** the entire EDI process runs through ABIS (not the DB box + PB app), with a
setup UI under admin. Today's API is **read-only** (`/api/edi/transactions`,
`/api/edi/log`, `/lookups/{edi-types,customer-edi}`) plus one `generate-861` **stub**.

> ⚠️ **NO LIVE FIRING.** The legacy `oracle11g` crontab on prod `db01` is the sole live
> owner of EDI. Build this **inert by default** — generation stubbed, no SFTP to the
> real VAN in dev/non-prod, scheduler disabled — until a **single-owner cutover**
> (legacy OFF for a flow before new is ON). Duplicate firing = duplicate EDI to Ford/GM/
> Cleveland-Cliffs/Aleris. See the no-live-firing guardrail memory.

### Current split — **updated by the 2026-07-07 crontab discovery**
The **automated** EDI is **shell + `sqlplus` + DB procs, NOT `edi.pbl`** — so it is
**recoverable now, not blocked on a PB export** (see
[`data-model/JOB_INVENTORY.md`](data-model/JOB_INVENTORY.md)):
- **Generation** — `abis_scripts/ediprocess.sh` runs `sqlplus` and calls
  `p_create_edi_861_for_all` / `_for_aleris` / `edi_aleris_870` / `p_846_cleveland_cliff_ccsc`
  (plus the per-partner `EDI_ALCAN_*`/`ARCONIC_*` families). All in `oracle_ddl.sql`.
- **Transport** — `GXS.ksh` does **SFTP to the Inovis/GXS VAN**
  (`sftp.gateway.inovisworks.net`, partner `412992496`), sends `S*.edi`, downloads
  inbound, runs `postpro`. **Protocol resolved: SFTP** (not AS2).
- **Only the *interactive* EDI screens** remain in the PB `edi.pbl` (⛔ PB export) —
  the automated pipeline above does not need it.
- **Sets in use:** 856 ASN (out + inbound receiving), 870 status, 861 receiving
  advice, 863 cert results, 997/FA acknowledgments. Tables: `OUTBOUND_EDI_TRANSACTION`
  (87k), `EDI_FA` (27k), `EDI_FILE_863` (20k), `EDI_870_COIL_STATUS`, `EDI_LOG`,
  `EDI_OUT_FILE`, `EDI_INBOUND_856`, `EDI_TYPE`, `CUSTOMER_EDI`.

### Build-out (incremental slices)
1. **861 first slice** — wire the existing `POST /api/receiving-bols/{id}/generate-861`
   stub to the per-customer Oracle functions (`f_edi_*_861`), gated on
   `customer.create_861_at_receiving`. (This is the existing NEXT_STEPS item #3;
   it becomes the beachhead.)
2. **Outbound generation** — an ABIS EDI service that invokes (initially) or ports
   (eventually) the per-partner generation procs for 856/870/863; writes to
   `OUTBOUND_EDI_TRANSACTION` + `EDI_OUT_FILE` with proper ISA/GS **control-number
   management**.
3. **Transport** — re-home `GXS.ksh`'s **SFTP** send/receive/`postpro` loop (Inovis/GXS
   VAN `sftp.gateway.inovisworks.net`, partner `412992496`) into an ABIS transport
   component. Retries + failure alerting. **Protocol resolved: SFTP** (not blocked).
4. **Inbound** — poll/receive 856 ASN into `EDI_INBOUND_856`; process 997/FA into
   `EDI_FA` and reconcile `fa_receive_status` on the outbound ledger.
5. **Scheduling** — EDI sends/polls become **scheduled tasks in #6** (this is the
   concrete link between the three workstreams).

### Setup UI (under admin)
Manage what's today hand-maintained in DB tables:
- Trading partners (`CUSTOMER_EDI`, `CUSTOMER_CUSTOMER_EDI_CODE`) — DUNS/qualifiers,
  routes.
- Transaction types + versions (`EDI_TYPE` — e.g. 856 `2002FORD`/`2040GM`/`3030`).
- Per-customer flags (`customer.create_861_at_receiving`).
- Maps/versions and VAN connection config.
- A **monitor** view (extends the read-only ledger/log already built): outbound
  status, FA reconciliation, failures + retry.

### Blocker — mostly lifted (2026-07-07)
The crontab discovery showed the **automated** orchestration/transport is shell +
`sqlplus` + SFTP, **not `edi.pbl`** — so slices 1–4 (generation, SFTP transport,
inbound/997) and the setup UI can **all proceed now** by porting `GXS.ksh`/`ediprocess.sh`
+ the DB procs. The PB `edi.pbl` export is only needed later for the *interactive* EDI
screens. **Remaining reads to finish the map:** `GXS_VAN.ksh` (the sftp batch),
`GXS2.ksh`, `postpro`, `patrick_870_script.sh`, the report scripts.

---

## Cross-cutting: auth, sequencing, dependencies

- **Auth.** All admin surfaces sit behind new features (`admin.scheduler`,
  `admin.server`, `admin.edi`) enforced by `RequireFeatureAsync`. This **depends on
  the OIDC rollout + broadened enforcement** (NEXT_STEPS #2) landing first — admin
  actions must not ship on the dev API-key alone.
- **Sequencing:**
  1. **Step 0 discovery** (job inventory + EDI transport recon) — needs DB access
     (piggybacks the Oracle sweep) and, for transport, the PB export.
  2. **#6 scheduler engine** — the substrate.
  3. **#8 EDI slices 1–2 + setup UI** — ride on #6 for scheduling.
  4. **#7 console** — independent shell; ship the view-only parts early, the
     allowlisted restart after security review.
  5. **#8 slices 3–4** — unblocked only after the PB `edi.pbl` export.
- **Security review** is a gate for #7's restart path and any DB-host channel.

## Open items / next actions

- [ ] **Run Step 0 discovery** once DB access is live (`DBA_SCHEDULER_JOBS`/`DBA_JOBS`
      + DB-host crontab) → write `JOB_INVENTORY.md`.
- [ ] Confirm **DB host OS + preferred read-only access method** (SSH-restricted key
      vs a small `abis-hostinfo` helper) for the cron view.
- [ ] Recon **EDI transport** (protocol + VAN endpoint) from `edi.pbl` + DB-host
      config — needs the PB export.
- [ ] Pick the scheduler engine (Quartz.NET vs in-process timer) once the job count
      is known.
