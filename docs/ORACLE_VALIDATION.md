# Validating the Oracle data path

CI only exercises the seeded **SQLite** fixture, so the production **Oracle** path
(driver, dialect SQL, sequences) needs validating against a real database. The
**original** core surface was validated live (read + write — see results below) and
fixed three live-only bug classes. Modules built since have only run on SQLite + the
gated smoke, so a **re-sweep of the newer read/write paths** is the top remaining item
in [`NEXT_STEPS.md`](NEXT_STEPS.md). This runbook makes that turnkey once you can
provide a connection.

> Use a **non-production** Oracle (a test/staging copy of the ABIS schema). The API
> issues real SQL; point it at prod only with explicit sign-off.

## Validation results — run against Oracle 11g (2026-06-25)

The seam was run against the live ABIS database (**Oracle 11g**, SID `abc11`).
Summary: the **read path is fully validated**; the **write path surfaced two
schema mismatches** and was initially deferred (the API is read-first). The full
write surface has since been implemented (#14) and **validated live** — see
[Write-path validation](#write-path-validation--run-against-oracle-11g-2026-06-25) below.

**Passed**

- Connectivity / readiness probe (`/health/ready` → `ready`).
- List + paging **bind order** (`/api/coils?pageSize=2&page=2` returns the true
  second page, not a repeat of page 1).
- Sorting + PK tie-breaker (`/api/jobs?sort=jobStatus&dir=desc`).
- Filters (`/api/coils?alloy=…`) and aggregation rollups (`/api/coils/summary`),
  against real data (~149k coils).

**Bugs found and fixed** (only a live 11g exposed these; CI runs SQLite)

- `ORA-00933` — the Oracle paging clause used the 12c+ `OFFSET … FETCH NEXT`
  syntax, invalid on 11g. Replaced with the `ROWNUM` inline-view form (PR #4).
- `ORA-00911` — that ROWNUM view used the alias `__p`; Oracle rejects unquoted
  identifiers starting with `_`. Renamed to `pg` (PR #5).

**Open write-path findings (deferred — need schema/DBA input)**

- **Sequences don't exist.** `POST /api/customers` → `ORA-02289: sequence does
  not exist` for `customer_seq`. The `{table}_seq` convention does not match the
  schema. Before enabling writes, confirm how ABIS assigns ids (the legacy PB9
  app likely uses `MAX+1`, or sequences under other names) and either switch the
  Oracle `NextIdQuery` to `MAX+1` or map real names via `Database__Sequences__<table>`.
- **Audit table missing.** Every request's audit write → `ORA-00942: table or
  view does not exist` for `opc_action_log` (swallowed as a warning, so requests
  still succeed — auditing silently no-ops). Confirm the real audit/log table
  name and `GRANT`s, or make the audit no-op explicit when the table is absent.

> Note: the read tests above were run with a temporary, read-only DB account.
> Write validation needs an account with INSERT + sequence privileges.

### Schema reconciliation (from live introspection, schema owner `DBO`)

A full data-dictionary dump (`tools/oracle_introspect.sql`, run against `DBO`)
resolved the deferred findings and corrected an inferred relationship. The real
schema has **414 tables** (vs ~40 in the recovered/inferred model).

- **Sequences DO exist** — named after the **id column** (`{ID_COLUMN}_SEQ`), not
  `{table}_seq`. So the fix for #6 is to derive the Oracle sequence from the id
  column the repository already passes to `NextIdQuery(table, idColumn)`:

  | table | id column | sequence |
  |---|---|---|
  | `coil` | `coil_abc_num` | `COIL_ABC_NUM_SEQ` |
  | `ab_job` | `ab_job_num` | `AB_JOB_NUM_SEQ` |
  | `customer` | `customer_id` | `CUSTOMER_ID_SEQ` |
  | `customer_order` | `order_abc_num` | `ORDER_ABC_NUM_SEQ` |
  | `sheet_skid` | `sheet_skid_num` | `SHEET_SKID_NUM_SEQ` |
  | `scrap_skid` | `scrap_skid_num` | `SCRAP_SKID_NUM_SEQ` |

- **`order_item` has a COMPOSITE primary key** (`ORDER_ITEM_NUM` + `ORDER_ABC_NUM`)
  and **no sequence** — `order_item_num` is a *line number within an order*, not a
  global id. This contradicts the inferred model (single `order_item_num` PK +
  `order_abc_num` FK). The API's order-item read (single-key lookup) and create
  (sequence-backed id) both need rework to the composite key. (Tracked: #10.)

- **`opc_action_log` does not exist.** `AB_AUDIT` is a *column-level change* log
  (`TABLE_NAME, COLUMN_NAME, VALUE_FROM, VALUE_TO, USER_ID, EVENT_DATE`), a
  different shape than the API's action log. Candidate action/log tables:
  `SYSTEM_LOG`, `USER_LOG` (with `SYSTEM_LOG_ID_SEQ` / `USER_LOG_ID_SEQ`).
  Retarget the audit middleware to a real table (matching columns) or make the
  no-op explicit. (Tracked: #7.)

### DDL export findings (triggers / business logic)

A full DDL+PL/SQL export (`data-model/oracle_ddl.sql`: 412 tables, 82 sequences,
**18 triggers**, 271 functions, 64 procedures) confirms the write-path decisions:

- **Ids are assigned application-side**, not by triggers. The only `BEFORE INSERT`
  triggers on the modeled tables set *derived display numbers*
  (`SHEET_SKID_DISPLAY_NUM_ADD`, `SCRAP_SKID_DISPLAY_NUM_ADD`) — none assign the
  PK from a sequence. So the API's "fetch `NEXTVAL`, insert explicit id" pattern
  is correct (#6). (Minor: those display-number triggers override any value the
  API sends for `*_display_num` on Oracle — harmless, but the API needn't send it.)
- **Auditing/history is trigger-based** (`COIL_HISTORY_LOG`, `SHIPMENT_HISTORY_LOG`,
  `SKID_HISTORY_LOG`, `SCRAP_HISTORY_LOG`, `*_DELETE_LOG`, … → history/log tables),
  *not* a single action log. This reinforces #7: the API's `opc_action_log` audit is
  vestigial against the real schema, so the graceful no-op is the right default.
- The export also brings the **business logic (functions/procedures) into the repo
  as text**, supporting Phase-1 rule recovery.

## Write-path validation — run against Oracle 11g (2026-06-25)

The full write surface (#14) was exercised end-to-end against the live database
with [`../tools/validate_oracle_writes.ps1`](../tools/validate_oracle_writes.ps1)
(run from inside the network — the managed sandbox's egress proxy can't carry
Oracle's TCP protocol). **Every create (`201`), update, and the 6 new lookup
reads resolved** against the real schema:

- **POST** dies, sketches, customer contacts, shipments (dual sequence:
  `packing_list_num_seq` + `bill_of_lading_seq`), receiving BOLs, scan logs, maint
  logs (MAX+1 id), shifts, downtime instances, and orders + order items.
- **PUT** dies, sketches, customer contacts, receiving BOLs, maint logs, shifts,
  downtime instances, order items, and order headers (`200`).
- **PATCH** shipment dispatch status (`200`).

**Bugs found and fixed** (only a live run exposes these; CI runs SQLite)

- `ORA-01745: invalid host/bind variable name` on every write whose Dapper bind
  name collided with an Oracle **reserved word** — `:desc`, `:date`, `:by`,
  `:start`, `:end`. SQLite accepts these as parameter names; Oracle rejects them.
  Renamed the offending binds (and their anonymous-object properties) to safe
  names (`:idesc`, `:dval`, `:cby`, `:stime`, `:etime`) in `AbisRepository.cs`.
  Affected: order-items, orders, sheet/scrap skids, dies, receiving BOLs, shifts,
  downtime instances.
- `ORA-00932: inconsistent datatypes` in the `COALESCE(:param, col)` partial-update
  pattern. When a nullable non-string field was omitted, ODP.NET bound the null as
  **CHAR**, so `COALESCE(charNull, numericOrDateCol)` failed type unification (SQLite
  is typeless, so CI passed). Fixed by binding those params with an explicit
  `DbType` (`Int32`/`DateTime`) via `DynamicParameters` in the job, coil, shipment,
  and part update paths.

**Schema facts confirmed by the run** (fixtures in the validation script encode these)

- `customer_id = 0` is the legacy **"SELECT CUSTOMER" sentinel** row — skip it
  when picking a real FK target.
- `sketch.sketch_name` is `VARCHAR2(16)` (a longer tag raises `ORA-12899`).
- `maint_log.maint_log_status` is **FK-constrained** to `MAINT_LOG_STATUS`; free
  text like `"OPEN"` raises `ORA-02291`. `"Completed"` is a verified-valid value.

> The script leaves clearly-tagged `ZZ_WRITE_TEST` rows and prints tag-based
> `DELETE` cleanup SQL (run it in SQL Developer, then `COMMIT`).

## Invoice billing validation (greenfield accounting slice)

The commercial invoice (save + document + the rejected/rebanded billed-weight rule) computes
figures that **bill trading partners**, so the numbers must be exact on real data — not just on
the SQLite fixture. A read-only runbook lives at
[`../tools/validate_oracle_invoice.ps1`](../tools/validate_oracle_invoice.ps1) (every query goes
through `tools/oraq`, which refuses anything but a single `SELECT`/`WITH`):

```powershell
# from inside the user's network (the cloud sandbox cannot reach Oracle's listener)
pwsh tools/validate_oracle_invoice.ps1 -Cs "Data Source=192.168.1.230:1521/abc11;User Id=dbo;Password=<pw>;"
```

It checks, against live non-prod `abc11` (schema `DBO`):

1. **Connectivity** + that `INVOICE`, `PRODUCTION_SHEET_ITEM`, `RETURN_SCRAP_ITEM` exist on the real schema.
2. **The reserved-word column reads when quoted** — `SELECT … "TIMESTAMP" … FROM invoice` (an unquoted
   `timestamp` would raise `ORA-00904`; the repo always quotes it, and no bind is named `:timestamp`
   — same `ORA-01745` reserved-word class as the earlier `:desc/:date/:like` fixes).
3. **The exact billed-weight rule runs on Oracle and diverges from the naive sum on real data** — it
   lists real jobs where `SUM(MAX(shift-end-or-balance, prior-process-qty))` over the reject/reband
   coils differs from the old `SUM(process_end_wt)`, then deep-dives one job (header + per-coil billed
   vs naive + every weight bucket). Any row where `billed <> naive` is a case the old browser sum
   would have mis-billed.

The greenfield SQL is deliberately portable (a correlated `MAX(process_quantity < …)` subquery and
`COALESCE`, not Oracle-only `GREATEST`); the C# applies the final `Math.Max`. `scrap_ab_job_num` is a
`CHAR` column, so the scrap-status/tare queries bind the job as a **string** (a numeric bind risks
`ORA-01722` on any non-numeric row).

### Result — run against non-prod Oracle 11g (`abc11`, schema `DBO`), 2026-07-07

**Validated live. The billed figures are exact, and the fix is material — the naive sum mis-bills to
zero on real data.**

- **Schema present:** `INVOICE`, `PRODUCTION_SHEET_ITEM`, `RETURN_SCRAP_ITEM` all exist on `DBO`.
- **Reserved-word column reads when quoted:** `SELECT … "TIMESTAMP" … FROM invoice` ran clean (no
  `ORA-00904`); the table has 0 rows in non-prod (no invoices saved there yet), which is fine — the
  point is the SQL shape is valid on Oracle. (Connectivity note: the very first `SELECT 1 FROM dual`
  returned `ORA-50000: request timed out` on a cold connect, but every subsequent query returned real
  data — the instance is up; the first-connect timeout was transient.)
- **The exact rule runs on Oracle and diverges hugely from the naive sum.** Of the reject/reband jobs,
  the top 8 by divergence **all have `naive_total = 0`** because `process_end_wt` is NULL on those
  rejected coils — so the old browser `SUM(process_end_wt)` would have billed **nothing**, while the
  correct rule (falling back to `net_wt_balance`) bills 118k–160k lb per job:

  | ab_job_num | reject coils | billed (rule) | naive (old) |
  |---|---:|---:|---:|
  | 39782 | 7 | 159,510 | 0 |
  | 27358 | 8 | 157,385 | 0 |
  | 27427 | 8 | 155,172 | 0 |
  | 25267 | 7 | 140,025 | 0 |
  | 33938 | 6 | 139,455 | 0 |

- **Deep-dive job 39782** (NOVELIS-KINGSTON, Liftgate 6111-T4E, PO 68371354): all 7 rejected coils have
  a null shift-end weight, so each bills at its coil balance (21,520 + 22,925 + 22,890 + 23,665 +
  23,185 + 22,495 + 22,830 = **159,510 lb**); the naive path would bill 0. Buckets computed cleanly:
  net 280,586 · processed 98,867 · scrap 21,739 · tare 4,774 · 19 skids. This exercises the exact
  **balance-fallback branch** of `InvoiceBilling.RejectedCoilBilledWeight` on live data.

This confirms the header joins, the correlated `MAX(process_quantity < …)` subquery, `COALESCE`
fallback, the quoted `"TIMESTAMP"`, and the string-bound `scrap_ab_job_num` all work on Oracle, and
that shipping the naive sum would have **under-billed rejected coils to zero** on real jobs. Reproduce
with [`../tools/validate_oracle_invoice.ps1`](../tools/validate_oracle_invoice.ps1).

## Newer-module read sweep — run against Oracle 11g (2026-07-07)

The modules built *after* the original validation (reporting, accounting, quality/
recovery, coil-eval, prod-folder, stacker, sales, coil-ownership, parts, carriers,
warehouse, security) had only ever run on the SQLite fixture. Swept read-only against
live non-prod `abc11` with
[`../tools/validate_oracle_reads_newmodules.ps1`](../tools/validate_oracle_reads_newmodules.ps1)
(and ad-hoc data-dictionary checks via the new read-only
[`../tools/oraq`](../tools/oraq) CLI). **~40 read/report endpoints; 4 live-only bug
classes found and fixed, 1 environment gap, plus performance findings.**

**Bugs found and fixed** (all green on SQLite CI — only live data exposed them):

- **`ORA` decimal overflow on `AVG` →** `reporting/production-summary` &
  `reporting/line-efficiency` returned **500**: ODP.NET materialises an Oracle `NUMBER`
  as `System.Decimal` before Dapper maps it to `double?`, and `AVG(material_yield)`'s
  ~40-digit result **overflows `Decimal`**. Fixed by bounding the scale in SQL —
  `ROUND(AVG(...), 4)` — at all three `AVG` sites (`GetProductionSummaryAsync`,
  `GetLineEfficiencyAsync`, `GetQaMechanicalAsync`). Reproduced + confirmed fixed
  directly at the SQL layer with `oraq`.
- **Stacker board unbounded scan →** `stacker/board` **hung** (>hundreds of seconds):
  `GetStackerBoardAsync` scanned the whole `ab_job` history (**93,835 of 97,390 rows are
  `Done`**), each with two correlated `COUNT` subqueries. Fixed by filtering to active
  work — `job_status NOT IN (0 Done, 3 Cancelled)` — per the real `ab_job_status_desc`
  codes (verified live: `0 Done / 1 InProcess / 2 New / 3 Cancelled / 4 OnHold`). The
  fixture's status codes were corrected to match reality; a regression test was added.
- **Reserved-word bind `:like` (`ORA-01745`) →** `sales/quotes` &
  `coil-ownership/transferable-coils` returned **500**: `LIKE` is an Oracle reserved
  word, so a bind *named* `:like` is rejected (SQLite accepts it). Renamed the bind to
  `:pat` in both queries. Same trap class as the write-path `:desc/:date/:by` fixes.
- **Transferable-coils whole-table scan →** `coil-ownership/transferable-coils`
  returned the **entire coil table (149,563 rows, ~200 s)** because it had no
  "transferable" predicate. A coil is transferable only if material remains, so added
  `net_wt_balance > 0` (live data: only **8,835** coils qualify, **0** NULL balances —
  a safe, exact filter). **200 s → ~10 s** unscoped (and fast when scoped by
  customer/search, the normal path). Regression test added (fixture coil 5004 set to a
  zero balance so it is excluded).

**Environment gap (not a code bug):**

- `sales/quotes` returns **`ORA-00942: table or view does not exist`**. The sales
  module's tables (`sales_quote`, `sales_probability`, `sales_order`, `sales_reminder`)
  are **absent from ALL three databases** — cross-referenced live (2026-07-07) against
  non-prod (`192.168.1.230:1521/abc11`), dev (`192.168.1.11:1523/abc11`), and **prod**
  (`192.168.1.9:1523/abc11`): **0** matching objects in any schema on any of them. All
  three carry the same complete 412-table `DBO` schema (real tables like `customer_order`,
  `coil` present), so this isn't a partial copy — the sales/quote tables **were never
  deployed anywhere**, even though the legacy `d_sales_quote_*` DataWindows reference
  them. **Product decision needed:** was the sales/quoting feature ever live? Either the
  greenfield sales-quote surface should be dropped/parked, or the tables need creating.
  The rest of the sales surface (`sales/contacts`, over the real `customer_contact`)
  passes.

**Performance findings (functional, but slow on real data — follow-up optimization):**

| Endpoint | Live time | Cause |
|---|---:|---|
| `reporting/line-efficiency` | ~380 s | per-line `AVG` + per-job correlated `SUM(process_end_wt)` + downtime merge over full history |
| `reporting/production-summary` | ~106 s | per-job correlated `SUM(process_end_wt)` subquery |
| `reporting/downtime` | ~75 s | full-history scan |
| `reporting/open-shipments` | ~30 s | |
| `stacker/board` | ~28 s | two correlated `COUNT` subqueries over the ~950 active jobs |
| `coil-ownership/transferable-coils` (unscoped) | ~10 s | returns all 8,835 balance-bearing coils |

Recommended: rewrite the correlated `SUM`/`COUNT` subqueries as `GROUP BY` joins,
add indexes on the `ab_job_num` FK columns of `process_coil`/`sheet_skid`, and default
the reporting date window to something narrower than 7 years. Tracked in
[`NEXT_STEPS.md`](NEXT_STEPS.md).

> Everything else in the sweep — the other 14 reporting endpoints, accounting,
> quality/recovery, coil-eval, prod-folder, stacker line-errors, parts, carriers,
> warehouse, and security reads — **passed clean** against live Oracle.

## 1. Connectivity smoke (no schema needed)

Confirms the driver connects and the dialect probe works (`SELECT 1 FROM dual`):

```sh
cd api
dotnet build src/ABIS.Api/ABIS.Api.csproj -c Release
Database__Provider=Oracle \
Database__Seed=false \
Database__ConnectionString="User Id=abis;Password=...;Data Source=//host:1521/ORCLPDB1" \
ASPNETCORE_ENVIRONMENT=Production \
ASPNETCORE_URLS=http://127.0.0.1:5230 \
dotnet run --project src/ABIS.Api -c Release --no-build &
curl -fsS http://127.0.0.1:5230/health        # liveness
curl -fsS http://127.0.0.1:5230/health/ready   # -> {"status":"ready"} means Oracle is reachable
```

A `503 {status:"unavailable"}` means the connection or the probe failed — check the
connection string and network reachability.

### Gated CI job

`.github/workflows/ci.yml` has an **`oracle-smoke`** job that runs this readiness
check automatically **when (and only when) the `ORACLE_CONNECTION_STRING` repo
secret is set** (otherwise it is a no-op). Set the secret to a non-prod Oracle that
the runner can reach (GitHub-hosted runners may need network access or a
self-hosted runner inside your network).

## 2. Functional validation (needs the ABIS schema)

Against an Oracle carrying the ABIS tables, exercise the API and check the
behaviours that differ from SQLite and so are **not** covered by CI:

| Check | How | What to confirm |
|---|---|---|
| **Paging bind order** | `GET /api/coils?pageSize=2&page=2&sort=coilAbcNum` | Returns the *second* page (not empty / not page 1). Guards the Oracle ROWNUM pagination (`:maxRow`/`:minRow`) positional binding. 11g-compatible — the 12c `OFFSET … FETCH NEXT` clause raises ORA-00933 on 11g. |
| **Sorting** | `GET /api/jobs?sort=jobStatus&dir=desc` | Rows ordered by status desc, with the PK tie-breaker. |
| **Server-assigned ids (sequences)** | `POST /api/customers` | Returns `201` with a new id. Requires the table's sequence to exist — default name `{table}_seq`; override via `Database__Sequences__<table>` (see `api/README.md`). |
| **Filters** | `GET /api/coils?alloy=3003` | Only matching rows. |
| **Readiness** | `GET /api/coils/summary?groupBy=alloy` | Aggregations return real rollups. |

### Reconcile the inferred schema

The API introduces two **inferred** relationships (flagged in
[`DATA_MODEL.md`](DATA_MODEL.md) and `api/README.md`):

- `order_item.order_item_num` treated as the PK,
- `order_item.order_abc_num` as the FK to `customer_order`.

Confirm these against the real schema; if they differ, adjust
`Models/Entities.cs` / `Data/AbisRepository.cs` and the seed fixture accordingly.

## 3. Replace the partial schema

Once a DDL dump or live connection is available, extend
[`../tools/extract_schema.py`](../tools/extract_schema.py) to ingest it and
regenerate [`DATA_MODEL.md`](DATA_MODEL.md) with full tables/PKs/FKs/indexes,
replacing the partial model recovered from DataWindows.
