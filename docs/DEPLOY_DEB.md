# Build & deploy the native `.deb` (codi-ABIS) — cheat sheet

Copy-paste-safe runbook for updating the native systemd deploy on **codi-ABIS
(192.168.3.110)** — the modernization's own box, on the **non-prod .230 Oracle**
(NOT .9 prod / .11 dev, which are read-only). Full reference: [`INSTALL.md`](INSTALL.md).

Two things that have bitten us, both handled below:
1. **The deploy clone can be in _detached HEAD_** (checked out at a release tag), so a
   bare `git pull` fails with *"You are not currently on a branch."* → force onto `main`.
2. **The `.deb` filename carries the git-describe version** (e.g. `abis_0.1.0-25-g34e7c64_amd64.deb`),
   so **never hardcode it** — grab the freshest build with a glob.

## Update (run on the server, as root)

```sh
cd ~/ABIS

# 1) Get the working tree onto current main (fixes detached HEAD; discards stray local noise).
git fetch origin
git checkout main
git reset --hard origin/main
git log -1 --oneline                      # note the commit you're about to ship

# 2) Build the .deb from that code (self-contained linux-x64; the 3 CS8619 warnings are benign).
bash build-deb.sh

# 3) Install the FRESHEST built .deb (no hardcoded filename) — dpkg -i also runs the restart.
DEB="$(ls -t dist/*.deb | head -1)"; echo "installing: $DEB"
sudo dpkg -i "$DEB"

# 4) Verify you're actually on the new build.
dpkg -s abis | grep ^Version              # should match the git-describe version from step 2
systemctl status abis --no-pager | head   # "Active: active (running) since <now>", fresh Main PID
curl -s http://127.0.0.1:8080/health/ready # {"status":"ready"} = Oracle reachable
```

> **Sanity of the running code.** The assembly version stays `0.1.0` regardless of git
> commit, so `GET /` and `dpkg`'s *unpacked* line aren't reliable discriminators — the
> **`dpkg -s abis` Version** (git-describe) is the source of truth. **Swagger is
> Development-only** (`Program.cs`: `if (app.Environment.IsDevelopment()) UseSwagger()`), so
> `/swagger/*` **404s on this Production service** — don't use it to check. For a feature-level
> check, probe a route that only exists in the new code by its **auth behavior**: an existing
> `/api/*` route answers `401` without a key, a missing route answers `404`:
> ```sh
> curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8080/api/documents/transfer-certificate/1
> #   401 = route exists (PR #82 code)   |   404 = route missing (old code)
> ```

## Config is preserved
`dpkg -i` swaps `/opt/abis/app/` and restarts; it does **not** touch `/etc/abis/abis.env`
(connection string + API key) or the nginx/TLS site. Only run `sudo abis-configure` if you
need to change the DB connection, key, or TLS.

## Rollback
Every build stays in `dist/`. To go back, install an older one and it restarts on the old code:
```sh
ls -t dist/*.deb                          # pick a previous version
sudo dpkg -i ./dist/abis_<older-version>_amd64.deb
```
Tidy stale builds occasionally: `rm dist/abis_*.deb` (keep the current + one prior).

## Guardrails
- Target is codi-ABIS on **non-prod .230** — safe. Never build/deploy against .9 or .11.
- The modern stack fires no EDI / scheduled jobs, so a deploy can't trigger legacy pipelines.
- Deploying `main` ships only what's **merged** — WIP feature branches are not included until
  their PR merges.
