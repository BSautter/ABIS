# Refresh the non-prod DB (.230) from prod (.9)

Copy the live plant schema from **prod `192.168.1.9` (ABC11)** into the **non-prod sandbox
`192.168.1.230` (abc11)** so the modernization UI (which reads .230 — see
[[abis-ui-production-data-source]]) shows current plant data.

> **Safety model.** The refresh only **READS** prod (Oracle Data Pump export). It never writes
> to .9. It is **destructive on .230** (that's the point — .230 is the write sandbox). It
> **preserves the modernization's own `ABIS_*` tables** on .230 (their config — EDI partners,
> job runs, etc. — is not on prod and must survive the refresh). It is a DB copy only: it does
> **not** run any legacy EDI / scheduled job, so the no-live-firing guardrail is intact.

Run everything below **on the .230 host as the `oracle` user**. Nothing runs on prod.

---

## One-time setup (DBA, on .230)

### 1. A database link to prod (.9)
The import pulls over this link, so no job ever runs on prod and no dump files are shuffled.

```sql
-- as SYSTEM on .230
CREATE DATABASE LINK prod_9
  CONNECT TO dbo IDENTIFIED BY "<dbo-password-on-.9>"
  USING '(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.1.9)(PORT=1521))
          (CONNECT_DATA=(SERVICE_NAME=abc11)))';

-- verify it reaches prod:
SELECT COUNT(*) FROM coil@prod_9;      -- ~150k on prod
```

> Store the link's prod password with care (it lives in the data dictionary). Rotate it if
> prod's `dbo` password changes. Prefer a **read-only** prod account if one exists over full `dbo`.

### 2. A Data Pump directory (needed for the log file only, in network mode)
```sql
-- as SYSTEM on .230; the path must exist and be writable by the oracle OS user
CREATE OR REPLACE DIRECTORY dpump_dir AS '/u01/app/oracle/dpump';
GRANT READ, WRITE ON DIRECTORY dpump_dir TO SYSTEM;
```

### 3. Import privilege
`SYSTEM` already has `IMP_FULL_DATABASE`. If you use another account, grant it.

---

## The refresh command (network-mode Data Pump import)

One command; no dump files. `table_exists_action=replace` drops+reloads each legacy table;
tables that are **not on prod** (the `ABIS_*` modernization tables) are left untouched, and the
`exclude` is a belt-and-suspenders guard.

```bash
impdp SYSTEM/"<system-pwd>"@abc11 \
  network_link=prod_9 \
  schemas=DBO \
  table_exists_action=replace \
  exclude=TABLE:"LIKE 'ABIS\_%' ESCAPE '\'" \
  exclude=STATISTICS \
  logfile=dpump_dir:refresh_$(date +%Y%m%d_%H%M).log \
  parallel=4 metrics=yes logtime=all
```

Notes:
- `schemas=DBO` — the app schema (both prod and non-prod use the `DBO` schema).
- `exclude=STATISTICS` — skip optimizer stats (regather locally if needed); much faster.
- `parallel=4` — tune to the .230 host's cores.
- To refresh **data only** into already-matching tables (faster, keeps local grants/indexes),
  use `table_exists_action=truncate` instead of `replace` — but `replace` is safer when prod's
  schema has drifted (added columns/tables), so it's the default here.
- After a `replace`, regather stats if query plans matter:
  `EXEC DBMS_STATS.GATHER_SCHEMA_STATS('DBO', DEGREE=>4);`

---

## Weekly automation (cron on .230)

`deploy/refresh-nonprod.sh` wraps the import with a **pre-flight link check** (it will *not*
wipe .230 if prod is unreachable), before/after row counts, and logging. Install it and add a
cron entry — **Sunday 02:00** keeps the prod read off business hours:

```cron
# m h dom mon dow   command   (oracle user's crontab on .230)
0 2 * * 0  /home/oracle/scripts/refresh-nonprod.sh >> /home/oracle/scripts/refresh-nonprod.log 2>&1
```

This is a **new, standalone** cron owned by the DBA on .230 — it is unrelated to the legacy
crontab and touches no EDI pipeline (see [[abis-230-cron-inventory]] / [[abis-no-live-firing-guardrail]]).

---

## Alternative: file-based (if the DB link isn't available)

Export on prod to a dump, copy it, import on .230. This runs `expdp` on prod (a read), so use a
read-only account and off-hours.

```bash
# on .9 (prod) — READ only:
expdp <ro-user>/"<pwd>"@abc11 schemas=DBO exclude=STATISTICS \
      directory=dpump_dir dumpfile=dbo_%U.dmp parallel=4 logfile=exp_dbo.log

scp /u01/.../dbo_*.dmp oracle@192.168.1.230:/u01/app/oracle/dpump/

# on .230:
impdp SYSTEM/"<pwd>"@abc11 schemas=DBO directory=dpump_dir dumpfile=dbo_%U.dmp \
      table_exists_action=replace exclude=TABLE:"LIKE 'ABIS\_%' ESCAPE '\'" \
      exclude=STATISTICS parallel=4 logfile=imp_dbo.log
```

---

## After a refresh
- The modern app on codi-ABIS keeps running against .230 — the `ABIS_*` config is intact, so no
  re-seed is needed. (If you ever DO drop the `ABIS_*` tables, the app re-provisions them on next
  start via `AbisSchema`, but their **data** — partner config etc. — would be lost.)
- Sanity check: `SELECT COUNT(*) FROM coil;` on .230 should now match prod's count.
