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

## Step 2 — map  ✅ schema inspected 2026-07-24

Source: `\\192.168.1.45\Maintenance\KeepTrakPro\KData.accdb` (KeepTrak Pro, 9.8 MB, **40 tables**).
Full dump: `docs/data-model/KEEPTRAK_SCHEMA.md`. Worked from a copy, never the live file.

**KeepTrak is unambiguously the live system.** The file was written the day before inspection, and
completions run ~800/year without a gap: 2021→748, 2022→776, 2023→773, 2024→864, 2025→841,
2026→466 so far, **13,648** total. ABIS's legacy `pm` tables hold 2,051 completions by comparison.

### Equipment hierarchy — maps 4-into-4 exactly

KeepTrak nests `usys_tLev0..tLev4`; `Lev0` is a single company root (`ABCo`) that has no ABIS
counterpart and is dropped. The remaining four line up one-for-one:

| KeepTrak | Rows | Example | ABIS |
| --- | ---: | --- | --- |
| `usys_tLev1.fs_Lev1` | 39 | "Auxiliary Equipment", "Die Area Building 3" | `groupdepartment` |
| `usys_tLev2.fs_Lev2` | 249 | "Air Compressors", "AB #80 Die" | `systemequipment` |
| `usys_tLev3.fs_Lev3` | 726 | "110 Line Looping Pit" | `subsystemequipment` |
| `usys_tLev4.fs_Lev4` | 233 | (components) | `itemdevice` |

**Access stores `0`, not NULL, for "no level assigned"** — and there is no Lev3/Lev4 row with id 0.
So a naive `IS NULL` test badly overstates the depth: in reality all 143 PMs carry Lev1 + Lev2, but
only **94 have a subsystem** (49 are 0) and only **19 have an item/device** (124 are 0). Mapping 0
through as an id produces `offset + 0`, a dangling parent key — this cost a failed load (ORA-02291)
before it was caught. **Treat 0 exactly like NULL.**

**Key on the KeepTrak id, never the name:** names repeat across parents ("Air Compressors" appears
three times in Lev2). Each level also carries `fb_InActive`.

### `t_PM` (143) → `pm`

| KeepTrak | ABIS | Note |
| --- | --- | --- |
| `fa_PMid` | *(natural key for idempotency)* | ABIS `pm_id` is minted MAX+1; keep a KeepTrak-id cross-reference |
| `fs_Status` | `pm_status` | text → number: 142 `ok`, 1 `Due`. Nothing is retired, so nothing maps to 0 |
| `fs_AssignedTo` / `fs_AssignedToTitle` | `assignedtogroup` / `titlecraft_id` | title resolves against `titlecraft` by name |
| `fm_Info` | `pm_notice` | memo |
| `fm_Action` | `pm_actions` | the checklist body — split into items, or land as one action |
| `fs_Freq` → `t_PM_x_Freq.fl_DaysBetween` | **`daysbetween`** | see below — this is the whole ballgame |
| `fld_EstMinPerPerson` / `fld_EstNumOfPeople` | `mins_per_unit` / `num_of_units` | |
| `fs_PMShift` | `pmshift` | seed `pmshift` from `t_PM_x_Shift` (7 rows) |
| `fl_Range` | `pmrange` | |
| `fd_LastCompDate` / `fs_LastCompBy` | `pm_completed` / `completed_by` | |
| **`fd_NextDueDate`** | **`nextduedate`** | the due board reads this directly |
| `fld_LastReading` / `fld_NextDueReading` / `fld_LastCompReading` | `lastreading` / `nextduereading` / `completedreading` | unused in practice — see below |
| `fld_HrsMilCycRepeat` | `pm_repeat` | |
| `fc_EstCost` | `pm_cost` | |
| `fdt_DateAdd` / `fdt_DateEdit` | `pm_entered` / `lastupdate` | |

### `pm.maint_freq` is a FOREIGN KEY, and the code vocabularies are identical

`PM.MAINT_FREQ` is not free text — it is an FK to a `MAINT_FREQUENCY` lookup. And that lookup uses
**exactly the same short codes as KeepTrak**, with the same intervals (`1XY`→365, `4XY`→91,
`WX8`→56, `YX10`→3650). This is the strongest evidence yet that ABIS's PM module and KeepTrak share
a CMMS lineage. So the import writes the **code** (`fs_Freq`), not the description.

All 12 codes in live use resolve except **`HOLD`**, which is KeepTrak's parking marker and has no
`MAINT_FREQUENCY` row — those import as `maint_freq = NULL` (they are already `pm_status = 0`, so
they carry no schedule anyway).

> ⚠ This FK also constrains the ABIS **write** path: `POST /pms` accepts `maintFreq` as free text
> today, so an arbitrary value would fail on Oracle with ORA-02291 while passing SQLite CI. Worth
> validating against `maint_frequency` — tracked as follow-up.

### Frequency — the auto-advance lands natively

`t_PM_x_Freq` (38 rows) carries **`fl_DaysBetween`** — precisely the quantity ABIS's completion
auto-advance consumes (`1XM`→30, `4XY`→91, `1XY`→365, …). So the mapping is a straight copy into
`pm.daysbetween`, and completing an imported PM advances it exactly as KeepTrak scheduled it.

`fs_Type` admits non-calendar modes — `HMC` (hours/miles/cycles), `DOW`, `DOM`, `SPD` — but
**every one of the 143 live PMs is `CAL`**. The meter-based risk flagged before the dump is
therefore *moot*: the calendar-only due board and auto-advance cover 100% of the real data. The
reading columns are mapped anyway (they cost nothing) so a future meter PM has somewhere to land.

### `t_PM_Completions` (13,648) → `pmcompletions`

`fl_PMid`→`pm_id`, `fs_CompStatus`→`pm_status`, `fd_CompletedDate`→`completeddate`,
`fs_AssignedTo`→`assignedtogroup`, `fs_CompBy`→`completedby`, `fm_CompNote`→`completed_notes`.

`fld_LaborHours` / `fc_Cost` → `labor_hours` / `comp_cost` (added by **migration 008**).

> **Reality check:** every one of the 13,648 completions stores **0** for both — the plant populated
> the fields but never used them, so there is no historical cost data to recover. The columns were
> still worth adding (the values were unknowable before the load, and future completions can record
> them through `POST /pms/{id}/complete`), but nobody should expect cost reporting from the imported
> history.

Completion history spans **2002-02-17 → 2026-07-22** — 24 years, considerably deeper than the
2021-onward window an early truncated query suggested.

## Step 4 — retire the pre-KeepTrak PMs (REQUIRED, or the due board is unusable)

ABIS's own PM module died in 2010 but its 77 definitions still hold `nextduedate` values from then,
and they are **not** status 0 — so the due board counts them ACTIVE and shows them **overdue by
~5,800 days**, burying the 125 real KeepTrak PMs.

`deploy/keeptrak/retire_legacy_pm.sql` sets `pm_status = 0` on PM rows below the import offset.
Nothing is deleted: the definitions stay browsable and their 2,051 completions are untouched, and
the due board already excludes status 0 so no code change is needed.

**Reversible, but not by a blanket update** — the legacy rows did not share one status (8×`1`,
46×`2`, 23×`3`), so `deploy/keeptrak/undo_retire_legacy_pm.sql` restores each row individually.
Regenerate it against the live schema before running the retire anywhere new.

Verified on `.230` (2026-07-24): the due board went from 125 KeepTrak + 77 legacy to **125 KeepTrak
and nothing else**, with all 77 legacy rows and 2,051 completions still present.

## Where it shows up in the UI

**Nowhere new.** KeepTrak is being *replaced*, not embedded, so its data lands in ABIS's own
`pm` / `pm_actions` / `pmcompletions` tables and appears in the existing **Maintenance** page tabs
(PM due board / PM schedules / Logs) with no additional screen. Adding a "KeepTrak" section would
recreate the two-places-to-look problem this migration exists to remove.

Imported rows stay identifiable for traceability: `pm_id >= 100000`, `groupdepartment.depttype =
'KEEPTRAK'`, and `pm.pmreference = 'KT-<KeepTrak id>'`.

## Follow-on scope (mapped, not yet planned)

- **`t_LG` (7,201)** — the work/issue log, with `t_LG_x_IssuePhrases` / `ActionPhrases` / `Status`
  lookups. Target is ABIS `maint_log`.
- **`t_PI` (1,401) + `t_PI_x_Suppliers` (90) + `t_PI_PartUsed` (151) + categories/units** — the
  spares inventory. **This settles the earlier question: KeepTrak *does* hold spares**, so ABIS's
  legacy `PARTS` (762 rows) is the wrong source and building from it was correctly deferred.
- `t_EI_EquipInfo` (2 rows), `t_DocPics` (0), `t_PI_PO`/`PO_LineItems` (0) — effectively unused.

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

**Resolved by the schema dump (2026-07-24):**

- ~~Is the legacy ABIS `pm` data live or abandoned?~~ **Abandoned since August 2010** — confirmed
  directly against `.230` (2026-07-24): newest `pm_entered` **2010-02-17**, newest `lastupdate`
  **2010-08-10**, newest `pmcompletions.completeddate` **2010-08-24**, 77 PMs / 2,051 completions.
  KeepTrak's own frequency catalog dates from 2019, so it took over long afterwards. **The import
  starts clean** — no merge, no id reconciliation against those rows.
- ~~Meter-based PMs?~~ **None.** All 143 live PMs are calendar (`CAL`) type, so the calendar-only
  due board and auto-advance cover everything. Reading columns are mapped anyway for future use.
- ~~Does KeepTrak hold spares?~~ **Yes** — `t_PI` (1,401 parts) + suppliers/categories/usage. ABIS's
  legacy `PARTS` (762) is the wrong source; deferring the spares build was correct.
- ~~Attachments / images?~~ `t_DocPics` is **empty** (0 rows), so nothing to migrate.

**Still open — need a decision:**

- ~~Labour hours + cost per completion?~~ **Decided: add the columns.** `pmcompletions` gains
  `labor_hours` and `comp_cost` via **migration 008**, preserving all 13,648 rows of cost history
  as queryable numbers. NULL means "not recorded" — deliberately distinct from 0 ("free").
  Note 008 is the first migration to ALTER a *legacy* table (001–007 only created ABIS-owned
  tables); it is additive and NULLable, so the legacy PowerBuilder app is unaffected.

**Still open — need a decision:**

1. **How much completion history to import.** All 13,648 rows back to 2021, or a recent window?
   Full history is more faithful and only ~13k rows — the default should be all of it.
2. **Retire-in-place vs cutover date.** Once imported, KeepTrak must stop being written or the two
   diverge. Needs an agreed cutover moment, not just a successful import. Current plan: load the
   `.230` sandbox as a dry run first, cut over as a separate deliberate step.
