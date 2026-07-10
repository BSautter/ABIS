# Server / service console (#7) — setup & security

The **server console** (`/ui/server-console.html`, nav → *EDI & Admin → Server console*)
lets an admin manage the ABIS deployment on **codi-ABIS (192.168.3.110)**: view the
allowlisted systemd services, tail their journals, restart them, and view the DB-host
crontab **read-only**. Scope is deliberately **"view + safe restarts only"** — no stop, no
config edits, no arbitrary shell.

> **OFF by default.** Shipping this code grants **no** new privilege on its own. Every
> endpoint 503s until `Admin:ServerConsole:Enabled=true`, and the mutating **restart** stays a
> no-op until `AllowRestart=true` **and** you install the sudoers allowlist below. Do the
> security review before enabling the restart / DB-host-cron paths.

## How it's kept safe
- **Fixed unit allowlist** (`AllowedUnits`, default `abis,nginx`). Any other unit → 404; the
  unit name is never interpolated into a shell.
- **Argv only** — commands run via `ProcessStartInfo.ArgumentList` (no shell string), so there
  is no command-injection surface.
- **`sudo -n`** (non-interactive) for restart: it fails fast unless the NOPASSWD allowlist
  grants exactly that command — never prompts, never escalates beyond the allowlisted units.
- **RBAC** — every endpoint requires the **`Server Admin`** feature (read for status/logs/cron,
  write for restart). Per-command **timeout** kills a hung process.

## Config (`Admin:ServerConsole:*`, in `/etc/abis/abis.env` or appsettings)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Master switch. `false` → every console endpoint 503s. |
| `AllowedUnits` | `abis,nginx` | The systemd units the console may inspect / restart. |
| `AllowRestart` | `false` | Permit the restart action (needs the polkit rule — or a sudoers allowlist). |
| `RestartCommand` | *(empty)* | Restart command prefix; empty = `systemctl restart` (polkit, no sudo — works under `NoNewPrivileges`). Set to `sudo -n systemctl restart` only on a non-hardened box. |
| `LogTailMax` | `1000` | Max journal lines one request may tail. |
| `HostCronCommand` | *(empty)* | Argv to read the DB-host crontab read-only (an ssh command-locked key). Empty → the host-cron view 503s. |
| `CommandTimeoutSeconds` | `15` | Kill a console command after this long. |

env-var form (systemd `EnvironmentFile`): double-underscore + indexed arrays, e.g.
```sh
Admin__ServerConsole__Enabled=true
Admin__ServerConsole__AllowRestart=true
Admin__ServerConsole__HostCronCommand__0=ssh
Admin__ServerConsole__HostCronCommand__1=-i
Admin__ServerConsole__HostCronCommand__2=/etc/abis/cronview_key
Admin__ServerConsole__HostCronCommand__3=-o
Admin__ServerConsole__HostCronCommand__4=BatchMode=yes
Admin__ServerConsole__HostCronCommand__5=cronview@192.168.1.9
```
> **Host topology (2026-07-10):** the plant is consolidating the old DB boxes —
> **db01 = 192.168.1.9 (production)** and **db02 = 192.168.1.11 (dev production)** — onto the
> new box **192.168.1.230 (oeldb01)** (a dev box comes later). The legacy EDI/scheduled crons currently
> run on **db01 (192.168.1.9)**, so that's the read-only cron-view target today; repoint
> `HostCronCommand` at **192.168.1.230 (oeldb01)** after the cutover. **.9 and .11 stay strictly read-only.**

## Setup (run on codi-ABIS, as root)

### 1) Grant the RBAC feature
`Server Admin` isn't in any default group (like `User Control`). In *User security*, grant it
to your admin user (or an Admin group) — otherwise the nav item hides and the API 403s.

> If you just refreshed the non-prod DB from prod, the `Server Admin` feature and your grant were
> overwritten along with the rest of `SECURITY_*`. Restore your whole admin login in one shot with
> [`tools/bootstrap-admin.sh`](ADMIN_BOOTSTRAP.md) (`sudo ./bootstrap-admin.sh <loginId>`).

### 2) Enable status + logs (read-only, no sudo)
```sh
# abis.env
Admin__ServerConsole__Enabled=true
# The service user needs to read the journal for its units:
usermod -aG systemd-journal abis    # (abis = the systemd unit's User=)
systemctl restart abis
```
Verify: *Server console* now lists `abis` + `nginx` with live state; **Logs** tails the journal.

### 3) Enable restart (mutating — after security review)
> **The `abis` unit is hardened with `NoNewPrivileges=true`, which blocks `sudo`** (sudo can't gain
> root). So the default restart is **polkit**, not sudo: the console runs `systemctl restart <unit>`
> (no sudo) and a polkit rule authorizes it — which works under the hardening and keeps the sandbox
> intact. Config `Admin__ServerConsole__RestartCommand` is empty by default = this polkit path.

**Recommended — polkit rule** (grants the `abis` user *only* restart on `abis`/`nginx`):
```sh
# /etc/polkit-1/rules.d/49-abis-console.rules
cat >/etc/polkit-1/rules.d/49-abis-console.rules <<'EOF'
polkit.addRule(function(action, subject) {
  if (action.id == "org.freedesktop.systemd1.manage-units" && subject.user == "abis") {
    var unit = action.lookup("unit");
    if ((unit == "abis.service" || unit == "nginx.service") && action.lookup("verb") == "restart")
      return polkit.Result.YES;
  }
});
EOF
```
Then set `Admin__ServerConsole__AllowRestart=true` and `systemctl restart abis`. Sanity-check the rule
first: `sudo -u abis systemctl restart nginx` should succeed with no password prompt.

*Alternative for a non-hardened box (only if you drop `NoNewPrivileges`):* set
`Admin__ServerConsole__RestartCommand__0=sudo … __3=restart` + a sudoers allowlist
(`abis ALL=(root) NOPASSWD: /usr/bin/systemctl restart abis, /usr/bin/systemctl restart nginx`).

### 4) Enable the DB-host cron view (read-only, after security review)
On the **DB host that runs the EDI crons** — currently **db01 (192.168.1.9, production; strictly
READ-ONLY)**, moving to **192.168.1.230 (oeldb01)** after the cutover — lock an SSH key to `crontab -l` so
the console can only *read* cron, never edit it:
```sh
# ~cronview/.ssh/authorized_keys on the DB host — the forced command wins over any requested one
command="crontab -l",no-port-forwarding,no-agent-forwarding,no-pty ssh-ed25519 AAAA...abis-cronview
```
Put the matching private key at `/etc/abis/cronview_key` (chmod 600, owned by `abis`). Because the
`abis` service user has **no home dir** (and the unit sets `ProtectHome`), ssh can't use a `known_hosts`
file — so pass those bits explicitly in `HostCronCommand`:
```sh
Admin__ServerConsole__HostCronCommand__0=ssh
Admin__ServerConsole__HostCronCommand__1=-i
Admin__ServerConsole__HostCronCommand__2=/etc/abis/cronview_key
Admin__ServerConsole__HostCronCommand__3=-o
Admin__ServerConsole__HostCronCommand__4=BatchMode=yes
Admin__ServerConsole__HostCronCommand__5=-o
Admin__ServerConsole__HostCronCommand__6=StrictHostKeyChecking=accept-new
Admin__ServerConsole__HostCronCommand__7=-o
Admin__ServerConsole__HostCronCommand__8=UserKnownHostsFile=/dev/null
Admin__ServerConsole__HostCronCommand__9=cronview@192.168.1.9
```
The **DB-host cron** card then shows the crontab; edits are impossible by construction (the forced
`command=` + the read-only policy on `.9`/`.11`).

## Endpoints
- `GET  /api/admin/console/services` — status of the allowlisted units (read).
- `GET  /api/admin/console/services/{unit}/logs?tail=N` — journal tail (read).
- `POST /api/admin/console/services/{unit}/restart` — restart an allowlisted unit (write; 409 if not permitted / failed).
- `GET  /api/admin/console/host/cron` — DB-host crontab, read-only (503 until configured).

All require the `Server Admin` feature; all 503 while the console is disabled.
