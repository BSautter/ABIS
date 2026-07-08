# Legacy cron / EDI scripts (vendored, read-only reference)

Read-only copies of the legacy **`oracle11g` crontab + shell scripts** that drive the
plant's scheduled processing (EDI, reports, DB maintenance), captured **2026-07-07** from
the two Solaris 10 DB hosts. Vendored the same way as [`../src/`](../src/README.md) (PB
source): **source-of-truth for re-implementing these as ABIS-owned scheduled tasks**
(Admin subsystem #6/#8 — see [`../../docs/ADMIN_SUBSYSTEM_PLAN.md`](../../docs/ADMIN_SUBSYSTEM_PLAN.md)
and the classified inventory in [`../../docs/data-model/JOB_INVENTORY.md`](../../docs/data-model/JOB_INVENTORY.md)).

> ⚠️ **DO NOT EXECUTE any of these from the modernization environment.** The legacy
> `oracle11g` crontab on prod **`db01`** is the sole live owner of EDI + scheduled
> processing. Running any of these (or their DB procs, or the SFTP to the real VAN)
> would **duplicate EDI to trading partners** (Ford/GM/Cleveland-Cliffs/Aleris). These
> are reference only, until an explicit single-owner cutover. See the no-live-firing
> guardrail memory.

## Layout
- `db01-prod/`   — **prod** `192.168.1.9` (`db01`): `scripts/crontab.oracle11g.txt` (the
  live schedule) + `scripts/*` + `scripts/abis_scripts/*` + `edi/receive/*`.
- `db02new-dev/` — **dev** `192.168.1.11` (`db02new`): the crontab + scripts, including
  the **EDI test/dev variants** (`GXS_ALEX_TEST.ksh`, `GXS_SWITCH.ksh`, `convert_to_test.pl`,
  `run_2copy_2van.ksh`) — relevant for building the modern EDI in a non-firing test mode.

## Key files (the automated-EDI pipeline)
- `scripts/crontab.oracle11g.txt` — the schedule (what runs when).
- `scripts/GXS.ksh` — **EDI transport**: SFTP to the Inovis/GXS VAN
  (`sftp.gateway.inovisworks.net`, partner `412992496`) via `GXS_VAN.ksh`; send `S*.edi`,
  download inbound, `postpro`, `GXS_delete.pl`, `GXS2.ksh`.
- `scripts/abis_scripts/ediprocess.sh` — **EDI generation**: `sqlplus` →
  `p_create_edi_861_for_all` / `_for_aleris` / `edi_aleris_870`.
- `scripts/abis_scripts/846.sh` — EDI 846 (`p_846_cleveland_cliff_ccsc`); **active on dev**,
  TEST-ONLY on prod.
- `scripts/abis_scripts/check_997.sh` — 997/FA check (`p_check_997`) + email alert.
- `scripts/abis_scripts/{check_shift_end,check_status_unmatched_coil,create_*_report}.sh`
  — shop-floor checks + report generation.
- The rest (`cold_backup_*`, `hot_backup_*`, `export*`, `datapump`, `restart_db`,
  `clear_tmp`, `rm_arch_exp`) are **DB maintenance — stay on the host** (monitor from #7,
  don't import).

Captured read-only via `plink` + `oracle11g`/`__DB_PASSWORD_REDACTED__` (see the working-credentials memory).
