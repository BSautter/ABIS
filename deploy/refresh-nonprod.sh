#!/usr/bin/env bash
# Weekly non-prod (.230) refresh from prod (.9), part 1 of 2: the BULK network import.
# Runs ON the .230 host; pulls from prod over the public DB link `prod_9`. Reads prod only.
# See docs/DB_REFRESH.md for setup. Companion: refresh-long-tables.sh (the LONG-column tables,
# which a network link cannot carry — must go file-based).
#
# NOT `set -e`: Data Pump returns exit 5 ("completed with errors") for the harmless
# "object already exists" noise on a replace; that is success for our purposes.
set -uo pipefail

: "${ORACLE_SID:=abc11}"; export ORACLE_SID
# Local .230 import account, e.g. ORA_LOCAL="dbo/obd#157" (no @alias — connects to the local SID).
ORA_LOCAL="${ORA_LOCAL:?set ORA_LOCAL=dbo/pwd for the local .230 import account}"
DPDIR="${DPDIR:-/u01/app/oracle/dpump}"
STAMP="$(date +%Y%m%d_%H%M)"

# LONG-column tables the network link CANNOT move (ORA-31679) — excluded here, refreshed by
# refresh-long-tables.sh. Keep this list in sync with that script.
LONG_TABLES="'OUTBOUND_EDI_TRANSACTION','INBOUND_TRANSACTION','EDI_FILE_863','IMPORTED_FILE_863','SKETCH','SKETCH_JPG'"

echo "== $(date '+%F %T') network refresh starting (SID=$ORACLE_SID) =="

# Pre-flight: prod reachable over the link, or abort WITHOUT touching .230.
probe=$(sqlplus -s "$ORA_LOCAL" <<'SQL'
set heading off feedback off pagesize 0
SELECT 'PROBE=' || COUNT(*) FROM coil@prod_9 WHERE ROWNUM = 1;
exit
SQL
)
if ! echo "$probe" | grep -q 'PROBE=1'; then
  echo "ABORT: prod (.9) not reachable over DB link prod_9; .230 left untouched." >&2
  exit 1
fi

before=$(sqlplus -s "$ORA_LOCAL" <<'SQL'
set heading off feedback off pagesize 0
SELECT COUNT(*) FROM coil;
exit
SQL
)
echo "coil rows on .230 before: ${before//[[:space:]]/}"

# Network import: all DBO tables EXCEPT the ABIS_* modernization tables and the LONG tables.
# (Standard Edition -> no parallel; 11g -> no logtime.)
cat > "$DPDIR/net_${STAMP}.par" <<EOF
network_link=prod_9
schemas=DBO
table_exists_action=replace
exclude=TABLE:"LIKE 'ABIS%'"
exclude=TABLE:"IN (${LONG_TABLES})"
exclude=STATISTICS
logfile=dpump_dir:net_${STAMP}.log
EOF

impdp "$ORA_LOCAL" parfile="$DPDIR/net_${STAMP}.par"; rc=$?
if [ "$rc" -ne 0 ] && [ "$rc" -ne 5 ]; then
  echo "network import FAILED (impdp exit $rc); see dpump_dir/net_${STAMP}.log" >&2
  exit "$rc"
fi

after=$(sqlplus -s "$ORA_LOCAL" <<'SQL'
set heading off feedback off pagesize 0
SELECT COUNT(*) FROM coil;
exit
SQL
)
echo "coil rows on .230 after:  ${after//[[:space:]]/}   (impdp exit $rc; log dpump_dir/net_${STAMP}.log)"

# ---------------------------------------------------------------------------------------------
# ADMIN LOGIN RESTORE — this refresh WIPES the ABIS admin login.
#
# schemas=DBO + table_exists_action=replace overwrites every DBO table that isn't excluded, and
# SECURITY_USER / SECURITY_GROUP / SECURITY_APPLICATION / SECURITY_USER_GROUP do NOT match the
# 'ABIS%' exclude — so they are replaced by PROD's copies, deleting any admin that existed only
# on .230 (e.g. cmattinson). Symptom: "user not found" at the ABIS sign-in.
#
# NOTE the asymmetry: abis_user_credential DOES match 'ABIS%' and therefore SURVIVES. It is keyed
# by login_id (not user_id), so once the security_user row is recreated with the same login the
# EXISTING PASSWORD WORKS AGAIN — no password reset is needed. AD/LDAP mode is unaffected either
# way (AD verifies the password; ABIS still needs the security_user row for identity + RBAC).
#
# This runs on the DB host, but the fix is an API call against the ABIS app host — hence opt-in:
# set ABIS_ADMIN_LOGIN + BOOTSTRAP_ADMIN (+ ABIS_BASE / ABIS_API_KEY) to auto-restore. Otherwise
# the exact command is printed so the next person isn't left guessing.
#
# (Alternative, if you would rather .230 keep its OWN user list across refreshes: add
#  exclude=TABLE:"LIKE 'SECURITY%'" to the parfile above. That trades prod-realistic RBAC data
#  for a stable non-prod login. Left off by default so .230 keeps mirroring prod.)
ADMIN_LOGIN="${ABIS_ADMIN_LOGIN:-}"
BOOTSTRAP="${BOOTSTRAP_ADMIN:-}"
if [ -n "$ADMIN_LOGIN" ] && [ -x "$BOOTSTRAP" ]; then
  echo "-- restoring ABIS admin login '$ADMIN_LOGIN' via $BOOTSTRAP"
  BASE="${ABIS_BASE:-http://127.0.0.1:8080}" API_KEY="${ABIS_API_KEY:-}" \
    "$BOOTSTRAP" "$ADMIN_LOGIN" || echo "WARNING: admin restore FAILED — sign-in will be broken until it is re-run" >&2
else
  cat >&2 <<WARN

*** ACTION REQUIRED: the ABIS admin login was just wiped *******************************
  This refresh replaced SECURITY_USER/_GROUP/_APPLICATION/_USER_GROUP with prod's copies,
  removing any admin that lived only on .230. Sign-in will fail with "user not found".

  Restore it on the ABIS app host (codi-ABIS):
      sudo ./tools/bootstrap-admin.sh <loginId> "First" "Last"

  The existing password still works afterwards (abis_user_credential survives, keyed by
  login_id). To automate this next time, set ABIS_ADMIN_LOGIN + BOOTSTRAP_ADMIN (and
  ABIS_BASE / ABIS_API_KEY if the API is not on this host).
***************************************************************************************

WARN
fi

echo "== $(date '+%F %T') network refresh done — now run refresh-long-tables.sh for the EDI/sketch tables =="
