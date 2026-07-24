# KeepTrak → ABIS migration (maintenance / PM)

The plant runs **KeepTrak**, a Microsoft Access application, for maintenance and preventive
maintenance. The decision (2026-07-24) is to **replace it**: import its data into ABIS once, then
ABIS becomes the system of record and KeepTrak is retired.

This note records the decisions, the shape of the work, and the open questions — so the import can
be finished without re-deriving any of it.

## Why an import, not a live connection

Two hard constraints rule out ABIS talking to the Access file at runtime:

1. **The ABIS server is Ubuntu.** Reading `.mdb`/`.accdb` requires the Microsoft ACE/Jet OLEDB
   provider, which is Windows-only. A runtime connector from `codi-ABIS` would be fragile at best.
2. **Access is a file, not a server.** Concurrent access over a share is the classic way Access
   databases corrupt. Making ABIS a second live writer would be asking for trouble.

So the ETL runs **on Windows** (where ACE is available — this dev machine has ACE OLEDB 12.0 and
16.0 installed) and loads into Oracle. Nothing Windows-only enters the server or the CI build:
the tooling is a PowerShell script under `tools/`, deliberately **not** a `.csproj`, so the Linux
CI never tries to build it.

## The target model already exists

The ABIS PM subsystem was built first (PRs #273 read, #274 write, #275 completions, #276 UI), so
the import has somewhere to land:

| KeepTrak concept | ABIS destination |
| --- | --- |
| Assets / equipment | `systemequipment` → `subsystemequipment` → `itemdevice` (under `groupdepartment`) |
| PM definitions + interval | `pm` (`daysbetween` / `numoftimesperyear` drive the auto-advance) |
| Task lists / instructions | `pm_actions` (checklist items) |
| Completion log | `pmcompletions` (history) |
| Trades / labour rates | `titlecraft` |
| Spare parts | `parts` / `parts_suppliers` — **not yet built in ABIS** (see Open questions) |

## Step 1 — inspect (tool is ready)

`tools/keeptrak-inspect.ps1` produces a read-only inventory of the Access file: every user table
with its columns (name, type, size, nullability), row counts, and optional sample rows.

```powershell
./tools/keeptrak-inspect.ps1 -Path C:\temp\KeepTrak-copy.accdb -OutFile docs/data-model/KEEPTRAK_SCHEMA.md -SampleRows 5
```

**Always point it at a COPY.** Access takes a lock file (`.laccdb`/`.ldb`) even for readers, so
running against the live plant database risks locking or corrupting what people are using. The
script opens with `Mode=Read` and never writes, but the lock-file behaviour is Access's, not ours.

The script was verified against a synthetic CMMS-shaped Access database (equipment / PM schedule /
history, including a table name containing a space and `MEMO` columns) — schema and samples both
render correctly.

## Step 2 — map

Write the KeepTrak→ABIS field mapping from the *real* schema dump, not from assumptions. The
mapping is the step where surprises live; see Open questions.

## Step 3 — import

- **Dry-run first**: report what would land, and — more importantly — everything that *cannot* be
  mapped, rather than silently dropping it.
- **Idempotent**: re-running must not duplicate equipment or completions.
- **Oracle care** (all three of these already bit the PM API and would pass SQLite CI silently):
  - `pm` / `pm_actions` / `pmcompletions` have **no sequence** — ids come from `MAX+1`
    (`Database:MaxIdTables`), else `ORA-02289`.
  - ODP.NET binds parameters **positionally**; argument order must match the SQL placeholders.
  - Oracle stores `''` as **NULL**, so an empty string into a `NOT NULL` column fails.

## Open questions

1. **Is the legacy ABIS `pm` data live or abandoned?** The tables hold 77 PMs / 2,051 completions /
   1,618 actions. If KeepTrak superseded them, the import should start clean rather than merge into
   stale rows. Needs a read-only check against `.230` for the newest `pm_entered` / `lastupdate` /
   `completeddate` — blocked on a credential (the tooling takes it at runtime; nothing is stored).
2. **Meter-based PMs.** KeepTrak may schedule some PMs by run-hours rather than calendar days. The
   ABIS `pm` table *does* carry `lastreading` / `nextduereading` / `completedreading` for this, so
   there is somewhere to put it — but the due board and the completion auto-advance are
   **calendar-only** today. If KeepTrak uses meter-based PMs, extend them rather than drop those PMs.
3. **Spares inventory.** ABIS has legacy `PARTS` / `PARTS_SUPPLIERS` tables (762 rows) that were
   *not* built out, on the assumption they were the maintenance spares store. If KeepTrak also holds
   spares, building ABIS spares from the legacy tables would be building on the wrong source —
   decide after the schema dump. This is why the spares half of the maintenance module is on hold.
4. **Attachments / images.** KeepTrak may store PM documents or photos (OLE object columns). ABIS's
   `pm` carries `hasimage` / `image_path` but no blob storage; decide whether to migrate or drop.
