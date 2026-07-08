# ABIS — Scheduled Job Inventory (cron-import discovery)

> ⚠️ **HARD RULE — do NOT fire these off from the modernization environment.** The
> legacy `oracle11g` crontab on prod `db01` is the **sole live owner** of EDI +
> scheduled processing. Never execute these scripts or their DB procs (even against
> non-prod), never SFTP to the real VAN, and keep the API's EDI generation stubbed +
> the future scheduler disabled-by-default until an explicit **single-owner cutover**
> (legacy OFF before new ON). Double-firing = duplicate EDI to trading partners.
> Discovery here is **read-only** (`cat`/`crontab -l`/SELECT) only.

Step 0 of the cron-import workstream (Admin subsystem #6, see
[`../ADMIN_SUBSYSTEM_PLAN.md`](../ADMIN_SUBSYSTEM_PLAN.md)): enumerate every scheduled
job across the ABIS servers, read-only, and classify importable-vs-not. This is a
living doc; it fills in as discovery proceeds.

**Method.** In-database jobs via the read-only [`../../tools/oraq`](../../tools/oraq)
CLI against `dba_scheduler_jobs` / `dba_jobs`. OS-level crontab needs a read-only shell
channel to each host (pending). All three DBs are the same `abc11` service; `dbo` has
the DBA role, so it sees every job — **kept strictly read-only**. Two of the boxes are
live and read-only (see the server-inventory memory).

## In-database jobs

### PROD `192.168.1.9:1523/abc11`  (enumerated 2026-07-07)

**Oracle Scheduler — 11 jobs, ALL Oracle/OEM internal maintenance. No ABIS business jobs.**

| Owner | Job | State | Class |
|---|---|---|---|
| ORACLE_OCM | MGMT_CONFIG_JOB | scheduled | Obsolete (Oracle Config Manager) |
| ORACLE_OCM | MGMT_STATS_CONFIG_JOB | scheduled | Obsolete (Oracle Config Manager) |
| SYS | BSLN_MAINTAIN_STATS_JOB | scheduled | Stay-on-DB (stats baseline) |
| SYS | DRA_REEVALUATE_OPEN_FAILURES | scheduled | Stay-on-DB (diag repair) |
| SYS | FGR$AUTOPURGE_JOB | disabled | Stay-on-DB |
| SYS | FILE_WATCHER | disabled | Stay-on-DB |
| SYS | HM_CREATE_OFFLINE_DICTIONARY | disabled | Stay-on-DB |
| SYS | ORA$AUTOTASK_CLEAN | scheduled | Stay-on-DB (autotask cleanup) |
| SYS | PURGE_LOG | scheduled | Stay-on-DB (autotask log purge) |
| SYS | RSE$CLEAN_RECOVERABLE_SCRIPT | scheduled | Stay-on-DB (streams cleanup) |
| SYS | SM$CLEAN_AUTO_SPLIT_MERGE | scheduled | Stay-on-DB (streams cleanup) |

**DBMS_JOB — 1:** `SYSMAN` job #1 `EMD_MAINTENANCE.EXECUTE_EM_DBMS_JOB_PROCS()` every
minute — the **Oracle Enterprise Manager** agent heartbeat. Obsolete (OEM), consistent
with the leftover `SMP_*`/`SMAGENTJOB` OEM tables noted in
[`../ADMIN_SUBSYSTEM_PLAN.md`](../ADMIN_SUBSYSTEM_PLAN.md).

### DEV `192.168.1.11:1523/abc11`, NON-PROD `192.168.1.230:1521/abc11`
Not yet enumerated — expected to be the same Oracle-internal set (they share the schema).

**Confirmed:** the in-DB scheduler holds no ABIS business jobs. The real ABIS cron is
**OS-level crontab under the `oracle11g` user** on the Solaris DB hosts (below).

## OS crontab — the real ABIS jobs  (enumerated 2026-07-07, read-only via `oracle11g`)

Both hosts are **Solaris 10 / SPARC** (prod `.9` = `db01`, dev `.11` = `db02new`).
The jobs run as **`oracle11g`** (`crontab -l`); `sqlplus / as sysdba` (OS auth, no
passwords in scripts). Scripts live in `/export/home/oracle11g/scripts/` and
`.../scripts/abis_scripts/`. **All 84 scripts + both crontabs are now vendored
read-only under [`../../legacy/cron/`](../../legacy/cron/README.md)** (prod `db01-prod/`,
dev `db02new-dev/`) as the source-of-truth for re-implementation.

### PROD `db01` (192.168.1.9) — active jobs

| Schedule | Script | Class | What it does |
|---|---|---|---|
| every 5 min | `GXS.ksh` | **Importable (EDI transport)** | SFTP to the **Inovis/GXS VAN** (`sftp.gateway.inovisworks.net`, login `412992496`, batch `GXS_VAN.ksh`): sends `S*.edi` from `/templar/templar/incoming/senddata/`, downloads inbound `*.edi`, runs `util/postpro` + `GXS_delete.pl` + `GXS2.ksh` |
| every 30 min, 6am–8pm | `abis_scripts/ediprocess.sh` | **Importable (EDI generation)** | `sqlplus` → `p_create_edi_861_for_all`, `p_create_edi_861_for_aleris`, `edi_aleris_870` (alcan/wise 870 commented out) |
| 50 min hourly | `abis_scripts/check_shift_end.sh` | Importable (business) | shift-end processing |
| 08:00 & 16:00 | `abis_scripts/check_status_unmatched_coil.sh` | Importable (business) | unmatched-coil status (cf. `/reporting/unmatched-coils`) |
| 0 1,13 | `export.ksh` | Stay-on-host | DB export |
| 20:00 | `rm_arch_exp.ksh` | Stay-on-host | remove archived exports |
| 03:00 | `cold_backup_11g_act11.sh` | Stay-on-host | cold backup |
| 00:03 on the 1st | `clear_tmp.ksh` | Stay-on-host | monthly tmp cleanup |
| 04:00 | `restart_db.sh` | Stay-on-host | **daily DB restart** (note: path typo `/export/hone/`) |
| *(disabled)* | `create_inventory_report.sh 1459`, `create_recovery_report.sh 0`, `datapump.ksh`, `846.sh` | Importable / Stay-on-host | report gen (disabled); 846 is TEST-ONLY on prod |

### DEV `db02new` (192.168.1.11) — active jobs

| Schedule | Script | Class | What |
|---|---|---|---|
| daily 16:27 | `abis_scripts/846.sh` | **Importable (EDI 846)** | `sqlplus` → `p_846_cleveland_cliff_ccsc()` — EDI 846 for **Cleveland-Cliffs (cust 2516 / CCSC)**, new (Alex Gerlants 06/08/2026); prod copy is still TEST-ONLY |
| 03:00 on the 1st | `oracle9i/scripts/delete_backups.ksh` | Stay-on-host | monthly backup cleanup |
| *(disabled)* | `export.ksh` | Stay-on-host | |

### `abis_scripts/` directory (prod, the EDI/business script library)
`846.sh`, `846_email.sh`, `check_997.sh` (→ `p_check_997`, emails waiting-997 list to
`agerlants@albl.com`), `check_shift_end.sh`, `check_status_unmatched_coil.sh`,
`create_inventory_report.sh`, `create_recovery_report.sh` (+ `orig`/`old`/`patrick`
variants), `ediprocess.sh` (+ `.bak`), `patrick_870_script.sh`.

## Implications for the Admin subsystem

- **#6 scheduler import target** = the `oracle11g` crontab jobs above, classified
  Importable (EDI generation/transport + shift-end + unmatched-coil + reports) vs
  Stay-on-host (DB export/backup/restart/tmp — leave on the box, monitor from #7).
- **#8 EDI ownership is LESS blocked than thought.** The *automated* EDI pipeline is
  **shell + `sqlplus` + DB procs**, not the interactive PB `edi.pbl`. So:
  - **Generation** = call/port the `p_create_edi_861*` / `edi_aleris_870` / `p_846_*`
    DB procs (all in `oracle_ddl.sql`). Recoverable now, no PB export needed.
  - **Transport** = **SFTP to the Inovis/GXS VAN** (`sftp.gateway.inovisworks.net`,
    partner id `412992496`). Re-home `GXS.ksh`'s send/receive/postpro loop into the
    ABIS transport component. No AS2.
  - **Inbound/997** = `postpro` on received files + `p_check_997`. The `edi.pbl` PB
    export is only needed for the *interactive* EDI screens, not this automation.
- The six cron-called generation procs are **extracted** for convenience into
  [`../../legacy/cron/edi-procs/`](../../legacy/cron/edi-procs/) (`p_create_edi_861_for_all`,
  `p_create_edi_861_for_aleris`, `edi_aleris_870`, `p_846_cleveland_cliff_ccsc`,
  `p_check_997`, `p_edi_wise_870`) — verbatim from `oracle_ddl.sql`.

## EDI transport map (from the vendored `GXS.ksh` / `GXS_VAN.ksh`)

- **Protocol:** SFTP to `sftp.gateway.inovisworks.net` as partner **`412992496`**
  (SSH-key auth — no password in the scripts), driven by the `GXS_VAN.ksh` batch file.
- **Mailboxes:** inbound `mget *.edi` from `/home/412992496/fromvan/` → local
  `/export/home/oracle11g/edi/receive/`; outbound `mput S*.edi` from
  `/templar/templar/incoming/senddata/` → `/home/412992496/tovan/`. A **test mailbox
  `412992560`** exists (commented in `GXS_VAN.ksh`).
- **Test-mode safety (already in the legacy!):** `convert_to_test.pl` (dev box) rewrites
  the X12 envelope prod→test — **ISA usage indicator `*P*`→`*T*`**, sender
  `039630926T`→`2NDSFTP`, receiver →`NOVLSTEST` — "so test EDI's don't accidentally end
  up in Novelis production data." **This is the pattern the modern EDI must adopt for its
  inert/test default** (usage indicator T + test partner IDs + the `412992560` test
  mailbox, never prod). See the no-live-firing guardrail.

## Classification legend
- **Importable** — business logic ABIS can own (none found in-DB yet).
- **Stay-on-DB** — DB-internal maintenance; leave in place, monitor from the admin console.
- **Obsolete** — Oracle Enterprise Manager / agent remnants; candidates for removal (DBA sign-off).
