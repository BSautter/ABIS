# Legacy Templar EDI translator — inbound parsers + config (vendored, read-only)

The **inbound** half of the plant's EDI, captured **2026-07-07** from
`/templar/templar/` on prod `db01` (the Templar EDI translator host, originally
Sybase-based — note `libsybdb.so` in its `bin/`). This complements the **outbound**
side already captured in [`../cron/`](../cron/README.md) (`GXS.ksh` SFTP transport +
`ediprocess.sh` → the 861/870/846 DB procs). Together with the refreshed PL/SQL
([`../../docs/data-model/oracle_plsql_current.sql`](../../docs/data-model/oracle_plsql_current.sql))
this is the **complete automated-EDI system** — the source-of-truth for EDI ownership
(Admin subsystem #8).

> ⚠️ **No live firing.** These parse/dispatch real trading-partner EDI. Never run them
> from the modernization env; the legacy crontab on `db01` stays the sole owner. The
> live `.edi` data files were **deliberately not vendored** (customer transmissions).
> Any embedded DB password was redacted.

## `util/` — inbound X12 → DB parsers (Perl) + dispatcher
- `postpro` (**sh** — the inbound dispatcher `GXS.ksh` calls per received file) and
  `postpro_2_db.pl` / `postpro_2_XML.pl`.
- Per-transaction-set parsers (X12 → Oracle): **856** ASN (`856_2_db.pl`, `856_3_db.pl`
  + partner variants: alcan/aleris/arconic/commonwealth/const/sap/ken-mac), **863** cert
  results (`863_2_db.pl`, `863_3_db*.pl`), **997** functional acks (`997_db.pl`,
  `997_856_const.pl`, `997_863.pl`, `997_from_ford.pl`), **824** app advice
  (`824_const.pl`), **841** (`841_2_db.pl`/`841_2_text.pl`), and `mapping_alcan_856.pl`,
  `send_email.pl`. Many dated `_MMDDYYYY` / partner backups are kept for history.

## `etc/` — Templar config
`client.cfg`, `server.cfg`, `postproc.cfg` (the post-processing/parse config + cert refs).

## Not vendored (noted for architecture)
- `bin/` — compiled SPARC binaries (`sendedi`, `rcvmail`, `rcvmime`, `adminjob`, …) +
  Sybase libs. The translator engine itself; re-homed by our own SFTP + parsers, not ported.
- `util/*.edi`, `incoming/`, `outgoing/`, `senddata/` — live EDI data (skipped).

## Parity implication
Inbound EDI (856 ASN receiving, 863 cert ingest, 997 reconciliation) has **no greenfield
equivalent** yet (see docs/PARITY_AUDIT.md §5). #8 must re-home both the outbound
(generate+SFTP) and this inbound (receive+parse+load+997) — inert by default.
