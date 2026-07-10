# Restoring an admin login — `tools/bootstrap-admin.sh`

Give an ABIS login **full write access** — membership in every group **plus** write on every feature —
in one idempotent command.

## When you need it

An ABIS user and their grants live in the `SECURITY_*` tables. When we refresh the non-prod box
(`oeldb01` / `192.168.1.230`) from production with **Oracle Data Pump** using
`TABLE_EXISTS_ACTION=REPLACE`, those tables are **overwritten with prod's copy**. Any admin account that
only ever existed on the non-prod box — e.g. `cmattinson`, and the `Server Admin` feature we add for the
[server console](SERVER_CONSOLE.md) — is **wiped**. After such a refresh:

- AD login as that user fails (no matching `security_user` row), and
- the server console / other admin screens 403 (no grant).

Re-run this script to restore the account. It's the "recreate my admin after a DB copy" step.

> The **`abis_*` tables survive** a refresh (prod doesn't have them), so break-glass credentials, truck
> appointments, and scheduler definitions are unaffected — it's only the `SECURITY_*` grants that need
> restoring.

## How it authenticates

It calls the **local API over the service API key** (`ApiKeys__Keys__0` in `/etc/abis/abis.env`). API-key
callers **bypass RBAC**, so the script can bootstrap an admin even when no admin grant currently exists —
a chicken-and-egg the UI can't solve after a wipe.

## Usage

Run it on the app host (`codi-ABIS`):

```sh
cd /opt/abis/tools           # or wherever the repo/tools live on the box
sudo ./bootstrap-admin.sh cmattinson Codi Mattinson
```

Arguments: `<loginId> [firstName] [lastName]`. A name is required by the API; if you omit it, the login id
is used as the first name. The login id should match the user's **AD `sAMAccountName`** so AD login
resolves (see [AD_LOGIN.md](AD_LOGIN.md)).

Environment overrides (all optional):

| Var | Default | Meaning |
|---|---|---|
| `BASE` | `http://127.0.0.1:8080` | API base URL. |
| `ENV_FILE` | `/etc/abis/abis.env` | systemd `EnvironmentFile` to read the API key from. |
| `API_KEY` | *(parsed from `ENV_FILE`)* | Provide the key directly instead. |

## What it does

1. **(Re)creates** the user (`userStatus=1`; a `400/409` "already exists" is fine — it continues).
2. Adds them to **every group** (`POST /api/security/users/{id}/groups/{groupId}`).
3. Grants **write** on **every feature** (`PUT …/applications/{applicationId}` with `{"privilege":1}`).
4. Prints each call's HTTP status, then the resolved **effective permissions** and a count of any feature
   still below Write (expect `0`).

### Privilege model

Per feature, effective privilege = **`MAX(direct grant, group grants)`**; **`0` = read-only, `1` = write**.
Because the script grants write **directly** on every feature, the result is full write regardless of how
any individual group is configured. Adding to all groups is belt-and-suspenders (and keeps the account
visible as a member everywhere).

## Scope & safety

- Writes **only** to the `SECURITY_*` tables of whatever DB the API is pointed at (normally the non-prod
  box). It **fires no EDI, runs no report, and never touches a prod DB host** — consistent with the
  no-live-firing rule.
- This is a deliberately **broad** grant for an operator/admin account. For an ordinary user, assign
  specific groups/features in the **Security** UI instead of running this.

## Related

- [SERVER_CONSOLE.md](SERVER_CONSOLE.md) — the `Server Admin` feature this restores alongside everything else.
- [AD_LOGIN.md](AD_LOGIN.md) — how the login id maps to AD for sign-in.
