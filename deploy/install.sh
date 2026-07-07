#!/usr/bin/env bash
#
# install.sh — install/upgrade ABIS as a native systemd service on Ubuntu.
#
# Run from the extracted release tarball (this script sits next to app/ and
# abis.service). Idempotent: re-run it to upgrade in place — it stops the
# service, swaps the binaries, and restarts, preserving existing config unless
# you pass new values.
#
#   sudo ./install.sh                      # interactive
#   sudo ./install.sh --unattended \       # no prompts (CI / config mgmt)
#        --answers /path/abis.answers
#   sudo ABIS_DB_CONNECTION='...' ABIS_API_KEY='...' ./install.sh --unattended
#   sudo ./install.sh --configure-only     # .deb installs already laid the files
#                                          # down; do only config + service + TLS
#                                          # (invoked by `abis-configure`)
#
# Config inputs (env var / answers-file key — answers file is sourced, so quote
# values with spaces, e.g. ABIS_DB_CONNECTION="Data Source=..."):
#   ABIS_DB_CONNECTION     Oracle connection string               (required)
#   ABIS_API_KEY           X-Api-Key value clients send (generated if omitted)
#   ABIS_PORT              loopback port Kestrel binds            (default 8080)
#   ABIS_DB_PROVIDER       Oracle | Sqlite                      (default Oracle)
#   ABIS_SERVER_NAME       public FQDN to front with nginx+TLS.
#                          Blank = loopback only, no nginx.        (default blank)
#   ABIS_TLS_MODE          letsencrypt | provided | none   (default letsencrypt
#                          when ABIS_SERVER_NAME is set)
#   ABIS_LETSENCRYPT_EMAIL admin email for Let's Encrypt   (required if TLS mode
#                          is letsencrypt)
#   ABIS_TLS_CERT          path to fullchain cert  (required if TLS mode=provided)
#   ABIS_TLS_KEY           path to private key     (required if TLS mode=provided)
#
# Kestrel always binds 127.0.0.1 only; nginx is the sole public listener. With no
# ABIS_SERVER_NAME the service is reachable on loopback only (verify with curl on
# the box). See docs/INSTALL_PLAN.md.
#
set -euo pipefail

# --- constants ---------------------------------------------------------------
SERVICE_NAME="abis"
SERVICE_USER="abis"
INSTALL_ROOT="/opt/abis"
APP_DIR="${INSTALL_ROOT}/app"
CONFIG_DIR="/etc/abis"
ENV_FILE="${CONFIG_DIR}/abis.env"
STATE_FILE="${CONFIG_DIR}/install.state"
UNIT_DEST="/etc/systemd/system/${SERVICE_NAME}.service"
NGINX_SITE_AVAIL="/etc/nginx/sites-available/${SERVICE_NAME}"
NGINX_SITE_ENABLED="/etc/nginx/sites-enabled/${SERVICE_NAME}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_APP_DIR="${SCRIPT_DIR}/app"
SRC_UNIT="${SCRIPT_DIR}/abis.service"
SRC_NGINX_HTTP="${SCRIPT_DIR}/nginx/abis.conf"
SRC_NGINX_TLS="${SCRIPT_DIR}/nginx/abis-tls.conf"
SRC_NGINX_PENDING="${SCRIPT_DIR}/nginx/abis-pending.conf"

# --- defaults / args ---------------------------------------------------------
UNATTENDED=0
ANSWERS_FILE=""
# --configure-only: the app files + systemd unit are already on disk (installed
# by the .deb package); do ONLY config + service start + nginx/TLS. abis-configure
# (from the .deb) calls this. See build-deb.sh / deploy/debian/.
CONFIGURE_ONLY=0

die()  { echo "error: $*" >&2; exit 1; }
info() { echo "==> $*"; }
warn() { echo "warning: $*" >&2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --unattended)      UNATTENDED=1; shift ;;
    --answers)         ANSWERS_FILE="${2:-}"; shift 2 ;;
    --answers=*)       ANSWERS_FILE="${1#*=}"; shift ;;
    --configure-only)  CONFIGURE_ONLY=1; shift ;;
    -h|--help)         grep '^#' "$0" | grep -v '^#!' | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)                 die "unknown argument: $1 (try --help)" ;;
  esac
done

# --- preflight ---------------------------------------------------------------
[[ "$(id -u)" -eq 0 ]] || die "must run as root (use sudo)."
command -v systemctl >/dev/null 2>&1 || die "systemd (systemctl) not found; this installer targets systemd hosts."
if [[ "$CONFIGURE_ONLY" -eq 0 ]]; then
  # Tarball mode: the app payload + unit ship alongside this script.
  # Check existence, not the +x bit: some extraction tools drop modes, and we
  # chmod the apphost ourselves after copying it into place.
  [[ -f "${SRC_APP_DIR}/ABIS.Api" ]] || die "release payload missing: ${SRC_APP_DIR}/ABIS.Api not found. Run this from the extracted tarball."
  [[ -f "$SRC_UNIT" ]] || die "unit template missing: ${SRC_UNIT}."
fi

if [[ -n "$ANSWERS_FILE" ]]; then
  [[ -f "$ANSWERS_FILE" ]] || die "answers file not found: ${ANSWERS_FILE}"
  info "loading answers from ${ANSWERS_FILE}"
  # shellcheck disable=SC1090
  set -a; . "$ANSWERS_FILE"; set +a
fi

# --- read an existing env-file value (for upgrade-preserves-config) ----------
existing_value() {
  # $1 = exact KEY in ENV_FILE; echoes the unquoted value, or empty.
  [[ -f "$ENV_FILE" ]] || return 0
  local line; line="$(grep -m1 -E "^$1=" "$ENV_FILE" 2>/dev/null || true)"
  [[ -z "$line" ]] && return 0
  local val="${line#*=}"
  val="${val%\"}"; val="${val#\"}"   # strip one layer of surrounding quotes
  printf '%s' "$val"
}

state_value() {
  # $1 = exact KEY in STATE_FILE (nginx/TLS settings); echoes value, or empty.
  [[ -f "$STATE_FILE" ]] || return 0
  local line; line="$(grep -m1 -E "^$1=" "$STATE_FILE" 2>/dev/null || true)"
  [[ -z "$line" ]] && return 0
  local val="${line#*=}"
  val="${val%\"}"; val="${val#\"}"
  printf '%s' "$val"
}

generate_key() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -base64 32 | tr -d '\n'
  else
    head -c 32 /dev/urandom | base64 | tr -d '\n'
  fi
}

# prompt VAR "label" "default"  — only prompts when interactive and VAR is empty
prompt() {
  local __var="$1" __label="$2" __default="${3:-}"
  local __current="${!__var:-}"
  [[ -n "$__current" ]] && return 0
  if [[ "$UNATTENDED" -eq 1 ]]; then
    printf -v "$__var" '%s' "$__default"
    return 0
  fi
  local __ans
  if [[ -n "$__default" ]]; then
    read -r -p "${__label} [${__default}]: " __ans
    __ans="${__ans:-$__default}"
  else
    read -r -p "${__label}: " __ans
  fi
  printf -v "$__var" '%s' "$__ans"
}

IS_UPGRADE=0
[[ -f "$ENV_FILE" ]] && IS_UPGRADE=1

# --- resolve configuration ---------------------------------------------------
# Seed defaults from any existing env file so an upgrade keeps current settings.
: "${ABIS_DB_CONNECTION:=$(existing_value Database__ConnectionString)}"
: "${ABIS_API_KEY:=$(existing_value ApiKeys__Keys__0)}"
: "${ABIS_DB_PROVIDER:=$(existing_value Database__Provider)}"
ABIS_PORT_EXISTING=""
if [[ -f "$ENV_FILE" ]]; then
  # parse the port back out of ASPNETCORE_URLS=http://127.0.0.1:<port>
  ABIS_PORT_EXISTING="$(existing_value ASPNETCORE_URLS | sed -n 's#.*:\([0-9]\{1,5\}\).*#\1#p')"
fi
: "${ABIS_PORT:=$ABIS_PORT_EXISTING}"

# nginx/TLS settings persist in STATE_FILE so an upgrade keeps the public config.
: "${ABIS_SERVER_NAME:=$(state_value ABIS_SERVER_NAME)}"
: "${ABIS_TLS_MODE:=$(state_value ABIS_TLS_MODE)}"
: "${ABIS_LETSENCRYPT_EMAIL:=$(state_value ABIS_LETSENCRYPT_EMAIL)}"
: "${ABIS_TLS_CERT:=$(state_value ABIS_TLS_CERT)}"
: "${ABIS_TLS_KEY:=$(state_value ABIS_TLS_KEY)}"

prompt ABIS_DB_PROVIDER   "Database provider (Oracle/Sqlite)" "Oracle"

# On a re-run the connection string is seeded from the existing config, and the
# plain prompt() below would silently keep it — so an admin couldn't fix a bad
# string interactively. Offer to replace it here (without echoing the current
# value, which contains the DB password).
if [[ "$UNATTENDED" -eq 0 && -n "${ABIS_DB_CONNECTION:-}" ]]; then
  read -r -p "Oracle connection string is already set — enter a new one to change it, or leave blank to keep it: " __newconn
  [[ -n "$__newconn" ]] && ABIS_DB_CONNECTION="$__newconn"
fi
prompt ABIS_DB_CONNECTION "Oracle connection string"         ""
prompt ABIS_PORT          "HTTP port Kestrel binds on loopback" "8080"

# Public exposure: a server name turns on the nginx+TLS front. Blank keeps the
# Phase-2 behaviour (loopback only).
prompt ABIS_SERVER_NAME   "Public server name for HTTPS (blank = loopback only, no nginx)" ""
if [[ -n "${ABIS_SERVER_NAME:-}" ]]; then
  prompt ABIS_TLS_MODE "TLS mode (letsencrypt/provided/none)" "letsencrypt"
  case "${ABIS_TLS_MODE}" in
    letsencrypt) prompt ABIS_LETSENCRYPT_EMAIL "Admin email for Let's Encrypt" "" ;;
    provided)    prompt ABIS_TLS_CERT "Path to TLS certificate (fullchain.pem)" ""
                 prompt ABIS_TLS_KEY  "Path to TLS private key" "" ;;
  esac
fi

# API key: keep existing, else accept provided, else offer to generate.
if [[ -z "${ABIS_API_KEY:-}" ]]; then
  if [[ "$UNATTENDED" -eq 1 ]]; then
    ABIS_API_KEY="$(generate_key)"
    info "generated a new API key (unattended)."
  else
    read -r -p "API key (blank = auto-generate a strong one): " ABIS_API_KEY
    if [[ -z "$ABIS_API_KEY" ]]; then
      ABIS_API_KEY="$(generate_key)"
      info "generated a strong API key."
    fi
  fi
fi

# --- validate ----------------------------------------------------------------
[[ -n "${ABIS_DB_CONNECTION:-}" ]] || die "ABIS_DB_CONNECTION is required (Oracle connection string)."
# A literal double-quote in the value would collide with the double-quotes that
# wrap it in the systemd EnvironmentFile and corrupt the config. The Oracle driver
# accepts SINGLE quotes for sub-values, which don't conflict — steer the user there.
case "$ABIS_DB_CONNECTION" in
  *'"'*) die "ABIS_DB_CONNECTION must not contain a double-quote (\"). If a sub-value needs quoting (e.g. a password with ';' or '='), use SINGLE quotes — Password='p;w' — which the Oracle driver accepts and won't corrupt the service config." ;;
esac
[[ "$ABIS_PORT" =~ ^[0-9]+$ ]] && (( ABIS_PORT >= 1 && ABIS_PORT <= 65535 )) || die "ABIS_PORT must be 1-65535 (got: ${ABIS_PORT})."
case "${ABIS_DB_PROVIDER}" in Oracle|Sqlite) ;; *) die "ABIS_DB_PROVIDER must be Oracle or Sqlite (got: ${ABIS_DB_PROVIDER})." ;; esac

if [[ -n "${ABIS_SERVER_NAME:-}" ]]; then
  : "${ABIS_TLS_MODE:=letsencrypt}"
  case "${ABIS_TLS_MODE}" in
    letsencrypt)
      [[ -n "${ABIS_LETSENCRYPT_EMAIL:-}" ]] || die "ABIS_LETSENCRYPT_EMAIL is required when ABIS_TLS_MODE=letsencrypt." ;;
    provided)
      [[ -n "${ABIS_TLS_CERT:-}" && -n "${ABIS_TLS_KEY:-}" ]] || die "ABIS_TLS_CERT and ABIS_TLS_KEY are required when ABIS_TLS_MODE=provided."
      [[ -f "$ABIS_TLS_CERT" ]] || die "TLS certificate not found: ${ABIS_TLS_CERT}"
      [[ -f "$ABIS_TLS_KEY"  ]] || die "TLS private key not found: ${ABIS_TLS_KEY}"
      # nginx -t later hard-fails on a true cert/key mismatch. Catch the two
      # things it does NOT catch here, so they surface before install, not as
      # failing clients in production:
      if command -v openssl >/dev/null 2>&1; then
        # (1) leaf-only cert where a fullchain is expected -> incomplete-chain
        #     TLS errors for clients without the intermediate cached. grep -c
        #     prints the count even when 0 (and exits non-zero); '|| true' keeps
        #     set -e happy, and the regex coercion guards a non-numeric result.
        _cert_count="$(grep -c 'BEGIN CERTIFICATE' "$ABIS_TLS_CERT" 2>/dev/null || true)"
        [[ "${_cert_count:-0}" =~ ^[0-9]+$ ]] || _cert_count=0
        if [[ "$_cert_count" -lt 2 ]]; then
          warn "ABIS_TLS_CERT has ${_cert_count} certificate(s) — looks leaf-only or not PEM."
          warn "Supply the full chain (fullchain.pem) or clients without the intermediate"
          warn "cached may get TLS errors."
        fi
        # (2) an already-expired certificate.
        openssl x509 -noout -checkend 0 -in "$ABIS_TLS_CERT" >/dev/null 2>&1 \
          || warn "ABIS_TLS_CERT appears to be expired or unreadable by openssl."
      fi ;;
    none) ;;
    *) die "ABIS_TLS_MODE must be letsencrypt, provided, or none (got: ${ABIS_TLS_MODE})." ;;
  esac
fi

info "Configuration:"
echo "    mode      : $([[ "$IS_UPGRADE" -eq 1 ]] && echo upgrade || echo fresh install)"
echo "    provider  : ${ABIS_DB_PROVIDER}"
echo "    port      : 127.0.0.1:${ABIS_PORT}"
echo "    api key   : $([[ -n "$ABIS_API_KEY" ]] && echo '(set)' || echo '(none)')"
if [[ -n "${ABIS_SERVER_NAME:-}" ]]; then
  echo "    public    : ${ABIS_SERVER_NAME} (nginx, TLS: ${ABIS_TLS_MODE})"
else
  echo "    public    : (none — loopback only)"
fi

# --- stop existing service (idempotent upgrade) ------------------------------
if systemctl list-unit-files "${SERVICE_NAME}.service" >/dev/null 2>&1 \
   && systemctl is-active --quiet "$SERVICE_NAME"; then
  info "stopping running ${SERVICE_NAME} service for upgrade"
  systemctl stop "$SERVICE_NAME"
fi

# --- port availability (best effort, after stopping our own service) ---------
if command -v ss >/dev/null 2>&1; then
  if ss -ltnH 2>/dev/null | awk '{print $4}' | grep -qE "[:.]${ABIS_PORT}\$"; then
    if [[ "$UNATTENDED" -eq 1 ]]; then
      die "port ${ABIS_PORT} is already in use."
    else
      warn "port ${ABIS_PORT} appears to be in use by another process."
    fi
  fi
fi

# --- service user ------------------------------------------------------------
if ! id -u "$SERVICE_USER" >/dev/null 2>&1; then
  info "creating system user '${SERVICE_USER}'"
  useradd --system --user-group --no-create-home --home-dir /nonexistent \
          --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# --- lay down application files ----------------------------------------------
# Skipped in --configure-only: the .deb already installed the binaries via dpkg.
if [[ "$CONFIGURE_ONLY" -eq 0 ]]; then
  info "installing application to ${APP_DIR}"
  mkdir -p "$APP_DIR"
  # Clean swap of binaries so removed files don't linger across upgrades.
  rm -rf "${APP_DIR:?}/"*
  cp -R "${SRC_APP_DIR}/." "$APP_DIR/"
  chmod +x "${APP_DIR}/ABIS.Api"
  chown -R "${SERVICE_USER}:${SERVICE_USER}" "$INSTALL_ROOT"
fi

# --- write config (root-owned, group-readable by the service user) -----------
info "writing config to ${ENV_FILE}"
mkdir -p "$CONFIG_DIR"
umask 027
cat > "$ENV_FILE" <<EOF
# ABIS service configuration — written by install.sh. Root-owned, 0640.
# Re-run install.sh to change these; do not edit binaries under ${APP_DIR}.
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS="http://127.0.0.1:${ABIS_PORT}"
Database__Provider=${ABIS_DB_PROVIDER}
Database__Seed=false
Database__ConnectionString="${ABIS_DB_CONNECTION}"
ApiKeys__Enabled=true
ApiKeys__Keys__0="${ABIS_API_KEY}"
EOF
chown "root:${SERVICE_USER}" "$ENV_FILE"
chmod 0640 "$ENV_FILE"

# --- install + start the unit ------------------------------------------------
# In --configure-only the unit ships with the .deb (at /lib/systemd/system) and
# was already daemon-reloaded by the package's postinst; just (re)start it here.
if [[ "$CONFIGURE_ONLY" -eq 0 ]]; then
  info "installing systemd unit ${UNIT_DEST}"
  install -m 0644 "$SRC_UNIT" "$UNIT_DEST"
  systemctl daemon-reload
fi
systemctl enable "$SERVICE_NAME" >/dev/null 2>&1 || true
info "starting ${SERVICE_NAME}"
systemctl restart "$SERVICE_NAME"

# --- health gate -------------------------------------------------------------
HEALTH_URL="http://127.0.0.1:${ABIS_PORT}/health/ready"
info "waiting for readiness at ${HEALTH_URL} (Oracle must be reachable)"
get_code() {
  if command -v curl >/dev/null 2>&1; then
    curl -fsS -o /dev/null -w '%{http_code}' --max-time 3 "$1" 2>/dev/null || true
  elif command -v wget >/dev/null 2>&1; then
    wget -q -T 3 -O /dev/null "$1" >/dev/null 2>&1 && echo 200 || true
  else
    echo "SKIP"
  fi
}

# --- nginx + TLS front -------------------------------------------------------
# Render a template to the site file using bash literal substitution (NOT sed):
# cert/key/server values are admin-supplied and may contain sed metacharacters
# ('|', '&', '\'). The replacements are QUOTED — "${ABIS_TLS_CERT:-}" — which
# keeps them literal even on bash 5.2+, where an unquoted '&' in a ${//}
# replacement is otherwise expanded to the matched text (patsub_replacement),
# the same hazard sed has.
render_site() {
  local src="$1" line
  [[ -f "$src" ]] || die "nginx template missing: ${src}"
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line//@SERVER_NAME@/"$ABIS_SERVER_NAME"}"
    line="${line//@PORT@/"$ABIS_PORT"}"
    line="${line//@TLS_CERT@/"${ABIS_TLS_CERT:-}"}"
    line="${line//@TLS_KEY@/"${ABIS_TLS_KEY:-}"}"
    printf '%s\n' "$line"
  done < "$src" > "$NGINX_SITE_AVAIL"
  ln -sfn "$NGINX_SITE_AVAIL" "$NGINX_SITE_ENABLED"
}

nginx_test_reload() {
  if ! nginx -t >/dev/null 2>&1; then
    nginx -t || true
    die "nginx configuration test failed (see output above)."
  fi
  systemctl reload nginx 2>/dev/null || systemctl restart nginx
}

open_firewall() {
  # Open 80+443 only if ufw is the active firewall (best effort).
  if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
    ufw allow 'Nginx Full' >/dev/null 2>&1 || true
  fi
}

configure_nginx() {
  info "configuring nginx for ${ABIS_SERVER_NAME} (TLS: ${ABIS_TLS_MODE})"

  if ! command -v nginx >/dev/null 2>&1; then
    info "installing nginx"
    apt-get update -qq || true
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq nginx \
      || die "failed to install nginx via apt-get; install it manually and re-run."
  fi
  open_firewall

  case "$ABIS_TLS_MODE" in
    provided)
      render_site "$SRC_NGINX_TLS"
      nginx_test_reload
      ;;

    none)
      # Operator opted out of TLS: proxy the app over plain HTTP on :80.
      render_site "$SRC_NGINX_HTTP"
      nginx_test_reload
      ;;

    letsencrypt)
      if ! command -v certbot >/dev/null 2>&1; then
        info "installing certbot"
        apt-get update -qq || true
        DEBIAN_FRONTEND=noninteractive apt-get install -y -qq certbot python3-certbot-nginx \
          || die "failed to install certbot; install it manually or use ABIS_TLS_MODE=provided."
      fi

      local le_live="/etc/letsencrypt/live/${ABIS_SERVER_NAME}"
      # Preserve only a site that is genuinely THIS server's certbot-managed LE
      # site: a live cert dir, a 443 listener, AND the site file actually pointing
      # at the LE cert path. The last check matters because the provided-mode
      # template also has `listen 443` — without it, a provided->letsencrypt
      # switch would wrongly preserve the provided cert and ignore the mode change.
      if [[ -d "$le_live" ]] && [[ -f "$NGINX_SITE_AVAIL" ]] \
         && grep -qE 'listen[^;]*443' "$NGINX_SITE_AVAIL" \
         && grep -qF "/etc/letsencrypt/live/${ABIS_SERVER_NAME}/" "$NGINX_SITE_AVAIL"; then
        # Upgrade of an already-TLS host: the site file already carries certbot's
        # 443 block + redirect. Do NOT re-render the HTTP base over it (that would
        # drop TLS and, if certbot then failed, silently downgrade to plaintext).
        # Leave the working TLS config in place; the certbot timer renews the cert.
        info "existing Let's Encrypt TLS site detected — preserving it."
        ln -sfn "$NGINX_SITE_AVAIL" "$NGINX_SITE_ENABLED"
        nginx -t >/dev/null 2>&1 && { systemctl reload nginx 2>/dev/null || true; }
        # The proxy upstream port lives in the existing file; warn if it drifted.
        local cur_up
        cur_up="$(grep -oE 'server 127\.0\.0\.1:[0-9]+' "$NGINX_SITE_AVAIL" 2>/dev/null \
                  | grep -oE '[0-9]+$' | head -1 || true)"
        if [[ -n "$cur_up" && "$cur_up" != "$ABIS_PORT" ]]; then
          warn "nginx still proxies to port ${cur_up}, but the app now listens on ${ABIS_PORT}."
          warn "to apply the new port to the existing TLS site: run uninstall.sh then install.sh."
        fi
      else
        # Fresh TLS setup. Render the HTTP base (which proxies the app) so that
        # certbot --nginx can clone that proxy into a new 443 block and add the
        # HTTP->HTTPS redirect. There is a brief (seconds) plaintext window here
        # that is inherent to certbot --nginx; it closes the moment certbot adds
        # the redirect below.
        render_site "$SRC_NGINX_HTTP"
        nginx_test_reload
        info "requesting Let's Encrypt certificate for ${ABIS_SERVER_NAME}"
        if certbot --nginx -d "$ABIS_SERVER_NAME" --non-interactive --agree-tos \
             -m "$ABIS_LETSENCRYPT_EMAIL" --redirect; then
          systemctl reload nginx 2>/dev/null || true
          info "TLS certificate installed; the certbot systemd timer handles renewals."
        else
          # Do NOT leave the app proxied over cleartext. Swap to a closed site that
          # serves only the ACME path and returns 503 for everything else, so the
          # API key / traffic is never exposed in plaintext. A later re-run retries.
          warn "certbot could not obtain a certificate for ${ABIS_SERVER_NAME}. Common causes:"
          warn "  - DNS for ${ABIS_SERVER_NAME} doesn't resolve to this server yet"
          warn "  - inbound TCP/80 from the internet is blocked (ACME HTTP-01 needs it)"
          warn "closing plaintext access (HTTP 503) until TLS is in place."
          warn "fix DNS/firewall, then re-run: sudo ./install.sh   (or use HTTP only with ABIS_TLS_MODE=none)."
          render_site "$SRC_NGINX_PENDING"
          # The cleartext HTTP base is still live in memory from the reload above;
          # the pending 503 config MUST load now, or the app stays exposed on :80.
          # Refuse to exit cleanly while that's possible.
          if nginx -t >/dev/null 2>&1; then
            systemctl reload nginx 2>/dev/null \
              || die "rendered the TLS-pending (503) config but 'nginx reload' failed; the cleartext proxy may still be live. Inspect: systemctl status nginx"
          else
            nginx -t || true
            die "could not load the TLS-pending (503) config after certbot failed; refusing to leave the app exposed over plaintext on :80. Fix the nginx error above, then re-run."
          fi
        fi
      fi
      ;;
  esac

  # Best-effort public probe (non-fatal; DNS propagation / cert trust may lag).
  local scheme="https"; [[ "$ABIS_TLS_MODE" == "none" ]] && scheme="http"
  if command -v curl >/dev/null 2>&1; then
    local pub_code
    pub_code="$(curl -k -fsS -o /dev/null -w '%{http_code}' --max-time 5 \
                "${scheme}://${ABIS_SERVER_NAME}/health/ready" 2>/dev/null || true)"
    if [[ "$pub_code" == "200" ]]; then
      info "public endpoint ${scheme}://${ABIS_SERVER_NAME}/health/ready is reachable."
    else
      warn "public probe of ${scheme}://${ABIS_SERVER_NAME}/health/ready returned '${pub_code:-none}'; verify DNS/firewall."
    fi
  fi
}

# Persist nginx/TLS choices so a later upgrade (re-run) reuses them.
write_state() {
  local prev_umask; prev_umask="$(umask)"
  umask 077
  cat > "$STATE_FILE" <<EOF
# ABIS install state — written by install.sh. Records nginx/TLS settings so an
# upgrade (re-run) reuses them. Root-owned, 0600.
ABIS_SERVER_NAME="${ABIS_SERVER_NAME:-}"
ABIS_TLS_MODE="${ABIS_TLS_MODE:-}"
ABIS_LETSENCRYPT_EMAIL="${ABIS_LETSENCRYPT_EMAIL:-}"
ABIS_TLS_CERT="${ABIS_TLS_CERT:-}"
ABIS_TLS_KEY="${ABIS_TLS_KEY:-}"
EOF
  chown root:root "$STATE_FILE"
  chmod 0600 "$STATE_FILE"
  umask "$prev_umask"
}

ready=0
for _ in $(seq 1 30); do
  code="$(get_code "$HEALTH_URL")"
  if [[ "$code" == "SKIP" ]]; then
    warn "neither curl nor wget present; skipping the health check."
    ready=2; break
  fi
  if [[ "$code" == "200" ]]; then ready=1; break; fi
  if ! systemctl is-active --quiet "$SERVICE_NAME"; then
    die "service ${SERVICE_NAME} exited during startup. Inspect: journalctl -u ${SERVICE_NAME} -n 50 --no-pager"
  fi
  sleep 2
done

echo
if [[ "$ready" -eq 1 ]]; then
  info "ABIS is up and Oracle is reachable."
elif [[ "$ready" -eq 2 ]]; then
  info "ABIS service started (health check skipped)."
else
  warn "service is running but ${HEALTH_URL} did not return 200 within the timeout."
  warn "the app is up but likely can't reach Oracle yet. Check the connection string and:"
  warn "  journalctl -u ${SERVICE_NAME} -n 50 --no-pager"
fi

# --- nginx + TLS front (only when a public server name was given) ------------
if [[ -n "${ABIS_SERVER_NAME:-}" ]]; then
  configure_nginx
fi

# Record settings for upgrades (writes empty server name when loopback-only).
write_state

PUBLIC_LINE=""
if [[ -n "${ABIS_SERVER_NAME:-}" ]]; then
  if [[ "$ABIS_TLS_MODE" == "none" ]]; then
    PUBLIC_LINE="  Public : http://${ABIS_SERVER_NAME}/ui/order-entry.html"
  else
    PUBLIC_LINE="  Public : https://${ABIS_SERVER_NAME}/ui/order-entry.html"
  fi
fi

echo
echo "ABIS ${SERVICE_NAME} service installed."
echo "  Status : systemctl status ${SERVICE_NAME}"
echo "  Logs   : journalctl -u ${SERVICE_NAME} -f"
echo "  Local  : http://127.0.0.1:${ABIS_PORT}/ui/order-entry.html"
[[ -n "$PUBLIC_LINE" ]] && echo "$PUBLIC_LINE"
echo "  Config : ${ENV_FILE}"
echo "  API key (X-Api-Key header):"
echo "    ${ABIS_API_KEY}"
if [[ -z "${ABIS_SERVER_NAME:-}" ]]; then
  echo
  echo "No public server name set — reachable on loopback only. Re-run with"
  echo "ABIS_SERVER_NAME=<fqdn> (and TLS settings) to put it behind nginx + TLS."
fi
