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
| `AllowRestart` | `false` | Permit the restart action (needs the sudoers allowlist). |
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
Admin__ServerConsole__HostCronCommand__5=cronview@192.168.1.11
```

## Setup (run on codi-ABIS, as root)

### 1) Grant the RBAC feature
`Server Admin` isn't in any default group (like `User Control`). In *User security*, grant it
to your admin user (or an Admin group) — otherwise the nav item hides and the API 403s.

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
Install a **tightly-scoped** sudoers NOPASSWD allowlist for the exact commands only:
```sh
# visudo -f /etc/sudoers.d/abis-console   (validate the systemctl path with `command -v systemctl`)
abis ALL=(root) NOPASSWD: /usr/bin/systemctl restart abis, /usr/bin/systemctl restart nginx
```
Then set `Admin__ServerConsole__AllowRestart=true` and restart abis. The console's **Restart**
buttons now work; `sudo -n` guarantees nothing outside those two commands can run.

### 4) Enable the DB-host cron view (read-only, after security review)
On the **DB host** (`.11` dev/EDI — **read-only**, never `.9` prod), lock an SSH key to
`crontab -l` so the console can only *read* cron, never edit it:
```sh
# ~cronview/.ssh/authorized_keys on the DB host — the forced command wins over any requested one
command="crontab -l",no-port-forwarding,no-agent-forwarding,no-pty ssh-ed25519 AAAA...abis-cronview
```
Put the matching private key at `/etc/abis/cronview_key` (chmod 600, owned by `abis`) and set
`Admin__ServerConsole__HostCronCommand` as above. The **DB-host cron** card then shows the
crontab; edits are impossible by construction (matches the read-only policy on `.9`/`.11`).

## Endpoints
- `GET  /api/admin/console/services` — status of the allowlisted units (read).
- `GET  /api/admin/console/services/{unit}/logs?tail=N` — journal tail (read).
- `POST /api/admin/console/services/{unit}/restart` — restart an allowlisted unit (write; 409 if not permitted / failed).
- `GET  /api/admin/console/host/cron` — DB-host crontab, read-only (503 until configured).

All require the `Server Admin` feature; all 503 while the console is disabled.
