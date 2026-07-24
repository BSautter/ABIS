#!/usr/bin/env bash
# bootstrap-admin.sh — give an ABIS login FULL WRITE access (member of every group + write on every
# feature), and ensure the features the modern app gates on exist at all. Idempotent; safe to re-run.
#
# WHY THIS EXISTS
#   A prod -> non-prod DB copy via Oracle Data Pump with TABLE_EXISTS_ACTION=REPLACE overwrites the
#   SECURITY_* tables with prod's, which WIPES any admin user/grant that lived only on the non-prod box
#   (e.g. "cmattinson"). After such a refresh the admin can no longer sign in / reach the console. Re-run
#   this script to restore an admin login. It authenticates with the service API key, which BYPASSES RBAC,
#   so it can bootstrap access even when no admin grant currently exists.
#
# SCOPE / SAFETY
#   Writes ONLY to the ABIS SECURITY_* tables of whatever database the local API is pointed at (normally
#   the non-prod box). It fires no EDI, runs no report, and never touches a prod DB host. See
#   docs/ADMIN_BOOTSTRAP.md. This is deliberately a BROAD grant meant for an operator/admin account — for
#   an ordinary user, assign specific groups/features in the Security UI instead.
#
# PRIVILEGE MODEL
#   Per feature, a user's effective privilege = MAX(direct grant, group grants); 0 = read-only, 1 = write.
#   Granting write (1) directly on every feature guarantees full write regardless of how any group is set.
#
# USAGE
#   sudo ./bootstrap-admin.sh <loginId> ["First"] ["Last"]
#   ./bootstrap-admin.sh cmattinson Codi Mattinson
#   BASE=http://127.0.0.1:8080 ENV_FILE=/etc/abis/abis.env API_KEY=... ./bootstrap-admin.sh cmattinson
#
# ENV OVERRIDES
#   BASE      API base URL              (default http://127.0.0.1:8080)
#   ENV_FILE  systemd EnvironmentFile   (default /etc/abis/abis.env) — read for ApiKeys__Keys__0
#   API_KEY   API key                   (default: parsed from ENV_FILE)

set -uo pipefail

LOGIN="${1:-}"
FIRST="${2:-}"
LAST="${3:-}"
BASE="${BASE:-http://127.0.0.1:8080}"
ENV_FILE="${ENV_FILE:-/etc/abis/abis.env}"

if [ -z "$LOGIN" ]; then
  echo "usage: $0 <loginId> [firstName] [lastName]" >&2
  exit 2
fi

command -v jq   >/dev/null || { echo "error: jq is required"   >&2; exit 1; }
command -v curl >/dev/null || { echo "error: curl is required" >&2; exit 1; }

# API key: explicit $API_KEY wins, else pull ApiKeys__Keys__0 from the env file (quoted or bare).
KEY="${API_KEY:-}"
if [ -z "$KEY" ] && [ -r "$ENV_FILE" ]; then
  KEY=$(grep -oP 'ApiKeys__Keys__0="?\K[^"]+' "$ENV_FILE" || true)
fi
if [ -z "$KEY" ]; then
  echo "error: no API key (set API_KEY=... or ensure ApiKeys__Keys__0 is in $ENV_FILE)" >&2
  exit 1
fi

HK=(-H "X-Api-Key: $KEY")
HJ=(-H "Content-Type: application/json")
get() { curl -fsS "${HK[@]}" "$@"; }                                  # read; -f => visible HTTP errors
code_of() { curl -s -o /dev/null -w '%{http_code}' "${HK[@]}" "$@"; } # write; return HTTP status only

echo ">> ABIS admin bootstrap: login='$LOGIN' base=$BASE"

# 1) (Re)create the user. A name is required by the API (first OR last); default first to the login.
: "${FIRST:=$LOGIN}"
body=$(jq -nc --arg l "$LOGIN" --arg f "$FIRST" --arg s "$LAST" \
        '{loginId:$l, userFirstName:$f, userLastName:$s, userStatus:1}')
code=$(code_of "${HJ[@]}" -X POST "$BASE/api/security/users" -d "$body")
case "$code" in
  201)     echo "   user created" ;;
  400|409) echo "   user already exists (HTTP $code) — continuing" ;;
  *)       echo "   warning: create returned HTTP $code" ;;
esac

USERID=$(get "$BASE/api/security/users" | jq -r --arg l "$LOGIN" '.[]|select(.loginId==$l).userId')
if [ -z "$USERID" ] || [ "$USERID" = "null" ]; then
  echo "error: could not resolve userId for '$LOGIN'" >&2; exit 1
fi
echo "   userId=$USERID"

# 2) Add to EVERY group.
echo ">> adding to all groups"
for GID in $(get "$BASE/api/security/groups" | jq -r '.[].userGroupId'); do
  echo "   group $GID -> $(code_of -X POST "$BASE/api/security/users/$USERID/groups/$GID")"
done

# 2b) Ensure the features the MODERN app gates on actually exist.
#
# The 35 rows in SECURITY_APPLICATION are the LEGACY app's vocabulary. The modernization gates on
# four names that were never added to it, so they can never be granted — and the failure is silent
# and misleading:
#   Part Number       hides the Parts page AND 403s every part write
#   Scheduler Admin   hides Admin & scheduler — and makes the dashboard card read
#                     "Registry not provisioned", which looks like missing tables and is not
#   Server Admin      hides Server console
#   Maintenance_logs  403s every maintenance/PM write (reads are ungated, so the page looks fine
#                     until you try to save)
#
# Creating them here rather than as a one-off migration is deliberate: the weekly .230 refresh
# REPLACES SECURITY_* from prod (see docs/DB_REFRESH.md Part 3), so a manual insert would be wiped
# every week exactly like the admin login. Doing it in the same script that already runs after a
# refresh makes both self-healing. Adding a feature grants nobody anything on its own — step 3 does
# that — so this is safe to run anywhere.
REQUIRED_FEATURES=("Part Number" "Scheduler Admin" "Server Admin" "Maintenance_logs")
echo ">> ensuring modern-app features exist"
existing=$(get "$BASE/api/security/applications" | jq -r '.[].applicationName')
for FEAT in "${REQUIRED_FEATURES[@]}"; do
  if grep -Fxq "$FEAT" <<<"$existing"; then
    echo "   '$FEAT' present"
  else
    fbody=$(jq -nc --arg n "$FEAT" '{applicationName:$n, applicationNotes:"Created by bootstrap-admin.sh — required by the modern ABIS UI/API gating."}')
    echo "   '$FEAT' MISSING -> create: $(code_of "${HJ[@]}" -X POST "$BASE/api/security/applications" -d "$fbody")"
  fi
done

# 3) Grant WRITE (1) on EVERY feature.
echo ">> granting write on all features"
for AID in $(get "$BASE/api/security/applications" | jq -r '.[].applicationId'); do
  echo "   feature $AID -> $(code_of "${HJ[@]}" -X PUT \
      "$BASE/api/security/users/$USERID/applications/$AID" -d '{"privilege":1}')"
done

# 4) Verify: every feature should resolve to Write.
echo ">> effective permissions"
perms=$(get "$BASE/api/security/users/$USERID/permissions")
echo "$perms" | jq -r '.[] | "   \(.applicationName): \(.privilegeLabel)"'
below=$(echo "$perms" | jq '[.[]|select((.privilege // 0) < 1)] | length')
echo ">> done. features below Write: $below (expect 0)"
