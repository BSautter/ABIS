# Legacy web tier — handheld scanner CGI (vendored, read-only reference)

The plant's **handheld RF scanners** hit **Perl CGI** pages served by **Apache on the
Solaris DB host `db01` (192.168.1.9:80)**, docroot `/var/apache2/htdocs`, CGI in
`/var/apache2/cgi-bin/`. Captured **2026-07-07**, read-only. These are part of the
**full-parity mandate** — the modern ABIS must do everything ABIS + DAS + the scanners do
(see the parity-scope memory + [`../../docs/MODERNIZATION_ROADMAP.md`](../../docs/MODERNIZATION_ROADMAP.md)).

> 🔒 **Redacted:** these CGIs hard-coded the **prod** DB password (`dbo` @ `db01/abc11`).
> It has been replaced with `__DB_PASSWORD_REDACTED__` in every vendored copy (the real
> value is in the working-credentials memory, not the repo). Do not re-introduce it.

## `db01-prod/cgi-bin/` — coil-receiving scanner
`coil_receiving_12.pl` is the **active** version (Jun 2024); the rest are dated/named
backups (`_13`, `_14`, `_test`, `Tony_Original`, `alex*`, and the `save/` archive).

**What it does** (a handheld coil-receiving station):
- Simple auto-focus HTML form; the operator **scans the customer coil barcode** (a
  leading `S` header is stripped).
- Looks it up in **`INBOUND_COIL_STATUS`** (`COIL_NUMBER` → `COIL_ABC_NUM`) to resolve /
  mint the ABC number; shows coil detail.
- **Prints an ABC label** to the **Zebra printer mapped to the scanner's IP** — device
  routing by `remote_addr()`: floor scanners on `192.168.10.8/9/10` → printers
  `192.168.10.12/13/14` (label sent over a network socket).
- Optional: **defect email** notification, and a newer **QR-code** capture (`qrscan` →
  `addqrcode`).
- Connects directly to Oracle (`DBD::Oracle`, `db01/abc11:1523`) — 2-tier, no app layer.

## Parity gaps this exposes (for the modern ABIS receiving/scan surface)
- A **handheld-optimized** coil-receiving scan page (scan → resolve/mint ABC → label).
- **`INBOUND_COIL_STATUS`** integration (the barcode→ABC landing table) — check it's
  modelled in the API.
- **Network label printing** to device-mapped Zebra printers (a new edge/print concern).
- **QR-code** capture + **defect email**.

## Completeness sweep (2026-07-07) — scanner surface fully identified
A full read-only sweep of all Apache mount points on **both** hosts confirms the **only
handheld-scanner web app is `coil_receiving`** (many versioned copies). Ruled out:
- `/templar/templar/` `Assembly`/`Compose`/`Doneparts`/`Pickup` are the **EDI translator's**
  working dirs (Templar), not web-served — no scanner pages there.
- Tomcat holds only **stock examples** (`jsp-examples`), no custom ABIS webapp.
- `db02new-dev/cgi-bin/` has in-progress variants (`coil_receiving_new*`, `_bs`,
  `_12_alex_modified`) — vendored for reference (newer scanner tweaks).
- `htdocs/*.php` (`uptimechart*`, `sendmail`, `supportrequest`, `test`) are **ops/monitoring
  utilities**, not ABIS business functions — vendored for completeness.
