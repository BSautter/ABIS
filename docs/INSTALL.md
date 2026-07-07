# ABIS — native install on Ubuntu (one command)

Install the ABIS API + web UIs as a **native systemd service** on an Ubuntu
server next to the plant Oracle DB, fronted by **nginx + HTTPS**. No Docker, no
.NET runtime to pre-install — the release tarball is self-contained.

> Prefer containers? [`DEPLOY.md`](DEPLOY.md) runs the same app via Docker
> Compose. This guide is the Docker-free path. The design rationale is in
> [`INSTALL_PLAN.md`](INSTALL_PLAN.md).

## Prerequisites

- Ubuntu (with `systemd`) that can **reach the Oracle listener**.
- Root / `sudo`.
- For Let's Encrypt TLS: a **public DNS name** pointing at the server and
  inbound **TCP/80 + 443** reachable from the internet (for the ACME challenge).
- A DB account for the app — start with a **non-prod** copy of the schema.

## Get the release

**From a GitHub Release (recommended):** download the latest
`abis-<version>-linux-x64.tar.gz` from the repo's Releases page, then:

```sh
tar -xzf abis-<version>-linux-x64.tar.gz
cd abis-<version>-linux-x64
```

**Or build it from a clone** (needs the .NET 8 SDK on the box):

```sh
git clone https://github.com/mattiIce/ABIS && cd ABIS
bash build-release.sh                       # -> dist/abis-<version>-linux-x64.tar.gz
tar -xzf dist/abis-*-linux-x64.tar.gz -C /tmp && cd /tmp/abis-*-linux-x64
```

## Install via `.deb` (apt)

If you prefer package-manager semantics (clean upgrades + removal), grab the
`abis_<version>_amd64.deb` from the Releases page (or build it on a Debian/Ubuntu
host with `bash build-deb.sh`):

```sh
sudo apt install ./abis_<version>_amd64.deb   # installs the service (stopped)
sudo abis-configure                           # set DB connection, API key, optional nginx+TLS, then start
```

`apt install` lays down the binaries + systemd unit and creates the `abis` user
but does **not** start the service (it has no config yet). `abis-configure` is the
same flow as the installer below (it also accepts `--unattended --answers …`).
Upgrades via `apt install ./<newer>.deb` restart the running service; `apt purge
abis` removes config, the cert, and the user.

## Install via the tarball

### Interactive

```sh
sudo ./install.sh
```

It prompts for the Oracle connection string, port, public server name, and TLS
mode, and offers to auto-generate a strong API key. With a public server name +
`letsencrypt`, it installs nginx + certbot, obtains the cert, and brings up
HTTPS. Leave the server name blank to run **loopback-only** (no nginx).

### Unattended (CI / config management)

```sh
sudo ./install.sh --unattended --answers abis.answers
```

Copy [`abis.answers.example`](../deploy/abis.answers.example) to `abis.answers`,
fill it in (`chmod 600` it — it holds the DB password + API key). Any missing
required value fails fast. You can also pass everything via `ABIS_*` env vars:

```sh
sudo ABIS_DB_CONNECTION='Data Source=...;User Id=APP;Password=...;' \
     ABIS_SERVER_NAME=abis.example.com ABIS_LETSENCRYPT_EMAIL=ops@example.com \
     ./install.sh --unattended
```

## Configuration

| Key (env / answer file) | Purpose | Default |
|---|---|---|
| `ABIS_DB_CONNECTION` | Oracle connection string **(required)** | — |
| `ABIS_API_KEY` | value clients send as `X-Api-Key` | auto-generated |
| `ABIS_PORT` | loopback port Kestrel binds | `8080` |
| `ABIS_DB_PROVIDER` | `Oracle` or `Sqlite` | `Oracle` |
| `ABIS_SERVER_NAME` | public FQDN to front with nginx+TLS; blank = loopback only | blank |
| `ABIS_TLS_MODE` | `letsencrypt` · `provided` · `none` | `letsencrypt` |
| `ABIS_LETSENCRYPT_EMAIL` | admin email (required for `letsencrypt`) | — |
| `ABIS_TLS_CERT` / `ABIS_TLS_KEY` | cert + key paths (required for `provided`) | — |

**TLS modes:**
- **`letsencrypt`** — certbot obtains a cert and auto-renews via its systemd
  timer. Needs the public DNS name + port 80 reachable.
- **`provided`** — bring your own cert (internal CA, or a DNS-01 wildcard).
  Supply the **fullchain** for `ABIS_TLS_CERT` or clients may see chain errors.
- **`none`** — HTTP only (testing / behind another proxy).

## What gets installed

| Path | What |
|---|---|
| `/opt/abis/app/` | self-contained app binaries (replaced on upgrade) |
| `/etc/abis/abis.env` | service config (root-owned `0640`) — connection string, API key |
| `/etc/abis/install.state` | nginx/TLS settings for upgrades (root-owned `0600`) |
| `…/abis.service` | the systemd unit (`Type=notify`, hardened) |
| `/etc/nginx/sites-available/abis` | the reverse-proxy site (when a server name is set) |
| system user `abis` | non-login account the service runs as |

> The tarball installs the unit to `/etc/systemd/system/abis.service`; the `.deb`
> ships it at `/lib/systemd/system/abis.service` and puts the nginx templates +
> `abis-configure` under `/usr/share/abis/` and `/usr/sbin/`. Both run the same
> service.

## Manage the service

```sh
systemctl status abis            # state
journalctl -u abis -f            # live logs
systemctl restart abis           # restart
curl http://127.0.0.1:8080/health/ready   # local readiness (200 = Oracle reachable)
```

## Upgrade

Re-run the installer from a newer tarball — it stops the service, swaps the
binaries, restarts, and re-verifies, **preserving** your config and an existing
Let's Encrypt TLS site:

```sh
sudo ./install.sh                # idempotent; reuses /etc/abis/* settings
```

> Changing the port on a host that already has a Let's Encrypt cert: run
> `uninstall.sh` then `install.sh` (the installer preserves the existing nginx
> site on upgrade and won't rewrite its upstream port — it warns if it drifts).

## Uninstall

Tarball install:

```sh
sudo ./uninstall.sh              # remove service + /opt/abis + nginx site; keep config & cert
sudo ./uninstall.sh --purge      # also remove /etc/abis, the abis user, and the cert
```

`.deb` install:

```sh
sudo apt remove abis             # remove service + binaries + nginx site; keep config & cert
sudo apt purge abis              # also remove /etc/abis, the abis user, and the cert
```

nginx/certbot packages are left installed (they may serve other sites).

## Troubleshooting

- **`/health/ready` returns `ORA-50201` (or the install fails on readiness)** —
  the app can't establish the Oracle session. Work through it in this order:
  1. **Is the Oracle instance actually running?** A *stopped* instance leaves the
     listener up (TCP to `:1521` succeeds) but with nothing registered, so it
     rejects the connection — and the .NET driver reports that unhelpfully as
     `ORA-50201`. This is the most common cause. Confirm the DB is started.
  2. **Service name vs SID.** `Data Source=host:port/NAME` treats `NAME` as a
     *service name*. If that's really a SID, use the descriptor form:
     `Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=…)(PORT=1521))(CONNECT_DATA=(SID=…)));`
  3. **Get the real error.** The .NET driver hides the specifics behind
     `ORA-50201`; a quick independent probe spells it out:
     `sudo apt install -y python3-oracledb` then
     `python3 -c "import oracledb; oracledb.connect(user='U',password='P',dsn='host:1521/service')"`
     — a `DPY-6001`/`DPY-6003` says the service/SID isn't registered; `DPY-3010`
     (thin mode can't do 11g) still confirms the listener + service are good.
  4. Then check `journalctl -u abis -n 50`.
- **Password / connection string has special characters** — `#` is fine
  unquoted. If a sub-value contains `;`, `=`, or a space, wrap it in **single**
  quotes (`Password='p;w'`) — not double quotes, which collide with the config
  file's quoting (the installer rejects a `"` for this reason).
- **certbot failed** — the installer closes public access with an HTTP `503`
  (it never serves the app over plaintext). Fix DNS / port-80 reachability, then
  re-run `install.sh`. To run without TLS, re-run with `ABIS_TLS_MODE=none`.
- **Browser shows the API-key field, not Sign in** — OIDC isn't configured;
  that's expected. See `api/README.md` §Authentication to enable JWT/OIDC.

## Security notes

- Kestrel binds `127.0.0.1` only; nginx is the sole public listener and
  terminates TLS. A client cannot spoof the request scheme or its IP past nginx
  (`X-Forwarded-Proto`/`-For` are set by the proxy; the app trusts one hop).
- Keep `abis.answers` / `.env` out of version control — they hold secrets.
- Start against a **non-prod** Oracle copy; cut over to production with sign-off.
