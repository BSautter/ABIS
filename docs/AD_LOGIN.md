# On-prem Active Directory sign-in (LDAP bind)

Let ABIS users sign in with their **existing AD (ADUC) username + password**, validated by an
**LDAP simple-bind** to a domain controller. No Entra/Azure, no AD FS, no domain-join of the Linux
host — the app just binds to a DC over LDAP(S). Off by default; when `Auth:Ldap` isn't configured,
`/auth/login` uses the local PBKDF2 credential store as before.

## How it works
1. The login screen posts `{username, password}` to `POST /auth/login`.
2. ABIS looks up the `security_user` row by the username (identity + RBAC come from ABIS).
3. It **binds to the DC** with that credential. Bind OK → issue the bearer JWT that drives RBAC;
   bind rejected → `401`. An **empty password is rejected before binding** (an LDAP simple-bind with
   an empty password is an "unauthenticated bind" that succeeds — so we never attempt it).

**Requirement:** the AD **sAMAccountName must equal the ABIS `security_user.login_id`** (both
`jsmith`-style). `DOMAIN\user` and `user@domain` inputs are normalised to the bare name. If your AD
usernames differ from the ABIS logins, we need a mapping step — tell me and I'll add it.

## Configure (server `appsettings`, e.g. `/etc/abis/abis.env` or `appsettings.json`)
```jsonc
"Auth": {
  "Ldap": {
    "Enabled": true,
    "Host": "dc01.abc.local",        // a domain controller (or a DNS name that resolves to one)
    "UseSsl": true,                    // LDAPS on 636 — recommended (the password travels to the DC)
    "Port": 0,                         // 0 = default (636 if UseSsl else 389)
    "UserBindFormat": "{0}@abc.local", // UPN form, OR NetBIOS: "ABC\\{0}"   ({0} = the username)
    "AcceptAnyCertificate": false      // set true only for an internal self-signed/private-CA DC on a trusted LAN
  },
  "Jwt": {
    "SigningKey": "<32+ char persistent secret>"   // so bearer sessions survive an app restart
  }
}
```
As env vars (double-underscore): `Auth__Ldap__Enabled=true`, `Auth__Ldap__Host=dc01.abc.local`,
`Auth__Ldap__UserBindFormat={0}@abc.local`, etc.

### Notes
- **`UserBindFormat`** — most AD accepts the UPN `{0}@abc.local`. If your UPN suffix differs from the
  AD domain, use the NetBIOS form `ABC\{0}` instead.
- **LDAPS certificate** — for a real chain check, trust the DC's issuing CA on the ABIS host. Only
  flip `AcceptAnyCertificate` to skip validation for an internal self-signed DC on a trusted LAN.
- **Persistent signing key** — set `Auth:Jwt:SigningKey` (32+ chars) in prod so sessions aren't
  invalidated on every restart (otherwise an ephemeral key is used).
- **No enrollment needed** — passwords live in AD. Users just need a matching `security_user` row
  with grants (managed in the ABIS Security admin). The **API key** stays as the machine / break-glass
  path (it bypasses user auth), so you can't lock yourself out.

## Hardening that ships with this
- **Empty-password bind is rejected** up front — closes the blank-password sign-in.
- **`/auth/login` is rate-limited** to 10 attempts/min per client IP (brute-force protection *and*
  to avoid tripping AD account lockouts). Active when `RateLimiting:Enabled` is on (recommended in prod).

## Verify after enabling
```sh
# a real AD user with a security_user row + a correct password → 200 + a token
curl -s -X POST http://127.0.0.1:8080/auth/login -H 'Content-Type: application/json' \
  -d '{"login":"jsmith","password":"<their AD password>"}'

# wrong password → 401 ; blank password → 401 (never binds)
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:8080/auth/login \
  -H 'Content-Type: application/json' -d '{"login":"jsmith","password":""}'
```
