#!/usr/bin/env bash
# Refresh the non-prod DB (.230) from prod (.9) via network-mode Oracle Data Pump.
# READS prod only; DESTRUCTIVE on .230; preserves the modernization's ABIS_* tables.
# See docs/DB_REFRESH.md for the one-time setup (DB link `prod_9`, directory `dpump_dir`).
#
# Run on the .230 host as the `oracle` user. Weekly cron (Sunday 02:00):
#   0 2 * * 0 /home/oracle/scripts/refresh-nonprod.sh >> /home/oracle/scripts/refresh-nonprod.log 2>&1
#
# Credentials: DO NOT hardcode. Set these in the oracle user's environment (or an Oracle wallet):
#   ORA_LOCAL   e.g. SYSTEM/secret@abc11   (the .230 import account)
#   The prod password lives only in the `prod_9` DB link, not here.

set -euo pipefail

ORA_LOCAL="${ORA_LOCAL:?set ORA_LOCAL=user/pwd@abc11 for the .230 import account}"
LINK="prod_9"
STAMP="$(date +%Y%m%d_%H%M)"
LOG="refresh_${STAMP}.log"

echo "== $(date '+%F %T') non-prod refresh starting =="

# --- Pre-flight: prod must be reachable over the link, or we abort WITHOUT touching .230. ---
probe() { sqlplus -s /nolog <<SQL
whenever sqlerror exit 2
connect ${ORA_LOCAL}
set heading off feedback off pagesize 0
SELECT 'PROD_OK=' || COUNT(*) FROM coil@${LINK} WHERE ROWNUM = 1;
exit
SQL
}
if ! probe | grep -q 'PROD_OK='; then
  echo "ABORT: prod (.9) not reachable over DB link ${LINK}; .230 left untouched." >&2
  exit 1
fi

before="$(sqlplus -s ${ORA_LOCAL} <<'SQL'
set heading off feedback off pagesize 0
SELECT COUNT(*) FROM coil;
exit
SQL
)"
echo "coil rows on .230 before: ${before// /}"

# --- The refresh. table_exists_action=replace reloads legacy DBO tables; ABIS_* are excluded. ---
impdp "${ORA_LOCAL}" \
  network_link="${LINK}" \
  schemas=DBO \
  table_exists_action=replace \
  exclude=TABLE:"LIKE 'ABIS\_%' ESCAPE '\'" \
  exclude=STATISTICS \
  logfile=dpump_dir:"${LOG}" \
  parallel=4 metrics=yes logtime=all

after="$(sqlplus -s ${ORA_LOCAL} <<'SQL'
set heading off feedback off pagesize 0
SELECT COUNT(*) FROM coil;
exit
SQL
)"
echo "coil rows on .230 after:  ${after// /}   (log: dpump_dir/${LOG})"
echo "== $(date '+%F %T') non-prod refresh done =="
