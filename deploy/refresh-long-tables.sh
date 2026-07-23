#!/usr/bin/env bash
# Weekly non-prod (.230) refresh, part 2 of 2: the LONG-column tables a network DB link CANNOT
# carry (ORA-31679) — the EDI archive the modern UI reads (OUTBOUND_EDI_TRANSACTION), inbound EDI,
# the 863 files, and the sketch images. File-based Data Pump handles LONG.
#
# Runs ON prod (db01) — the direction proven to work: expdp on prod -> scp to .230 -> impdp on .230.
# Reads prod only (expdp); writes only to .230. Pairs with refresh-nonprod.sh (the bulk network job,
# which EXCLUDES these same tables so it never empties them).
#
# Requires: passwordless ssh/scp from prod (db01) -> .230; the oracle login env on .230 (ORACLE_HOME/
# SID/PATH) — the remote impdp is run under `bash -lc` so the profile is sourced. Prefer an Oracle
# wallet over inline passwords for the accounts below.
set -uo pipefail

: "${ORACLE_SID:=abc11}"; export ORACLE_SID
PROD_ORA="${PROD_ORA:?set PROD_ORA=dbo/pwd for the prod export account}"
NONPROD_SSH="${NONPROD_SSH:-oracle@192.168.1.230}"
NONPROD_ORA="${NONPROD_ORA:?set NONPROD_ORA=dbo/pwd for the .230 import account}"
NONPROD_DPDIR="${NONPROD_DPDIR:-/u01/app/oracle/dpump}"
# Path DATA_PUMP_DIR resolves to on prod (SELECT directory_path FROM dba_directories WHERE
# directory_name='DATA_PUMP_DIR'); on this box it is:
PROD_DPPATH="${PROD_DPPATH:-/u01/app_11g/product/11.2.0/home/rdbms/log}"

TABLES="OUTBOUND_EDI_TRANSACTION,INBOUND_TRANSACTION,EDI_FILE_863,IMPORTED_FILE_863,SKETCH,SKETCH_JPG"
DUMP="long_tabs.dmp"

echo "== $(date '+%F %T') LONG-table sync starting =="

# 1) export the LONG tables from prod (overwrite last week's dump).
expdp "$PROD_ORA" directory=DATA_PUMP_DIR dumpfile="$DUMP" logfile=long_exp.log \
  tables="$TABLES" reuse_dumpfiles=yes; rc=$?
[ "$rc" -ne 0 ] && [ "$rc" -ne 5 ] && { echo "expdp FAILED (exit $rc)" >&2; exit "$rc"; }

# 2) push the dump to .230.
scp "$PROD_DPPATH/$DUMP" "$NONPROD_SSH:$NONPROD_DPDIR/"

# 3) load on .230 (file mode carries LONG); replace the six tables. `bash -lc` sources the oracle
#    login profile so impdp/ORACLE_HOME are found in the non-interactive ssh session.
ssh "$NONPROD_SSH" "bash -lc 'export ORACLE_SID=${ORACLE_SID}; impdp ${NONPROD_ORA} directory=dpump_dir dumpfile=${DUMP} table_exists_action=replace tables=${TABLES} logfile=dpump_dir:long_imp.log'"; rc=$?
[ "$rc" -ne 0 ] && [ "$rc" -ne 5 ] && { echo "remote impdp FAILED (exit $rc)" >&2; exit "$rc"; }

echo "== $(date '+%F %T') LONG-table sync done — EDI/863/sketch tables refreshed on .230 =="
