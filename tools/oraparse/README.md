# oraparse — compile every repository SQL statement against the real Oracle schema

Tests run on SQLite; production is Oracle 11g. A statement can be perfectly valid on SQLite and refuse
to compile on Oracle — a column the real schema doesn't have, a reserved word, a table that was never
provisioned. Nothing in CI can see that, and the failure only appears when a user tries the feature.

This validates every literal statement in `AbisRepository.cs` **without executing any of it**.

## How it is safe

`DBMS_SQL.PARSE` compiles a statement — resolving tables, columns, syntax and privileges — and stops.
Nothing runs until `DBMS_SQL.EXECUTE`, which this tool never calls. An `INSERT` is checked exactly as
thoroughly as a `SELECT`, and no row is touched. That is what makes it usable against the prod-derived
`.230` sandbox, where the earlier alternative — actually running the writes — cost a data restore.

## Running it

```bash
python tools/oraparse/extract_sql.py                     # -> sql_statements.json
ORA_CS=... dotnet run --project tools/oraparse -- sql_statements.json
```

## Reading the output

```
parsed OK : 206/208
FAILED    : 2
```

Failures print the `AbisRepository.cs` line, the Oracle error, and the head of the statement.

**Two known-benign failures** as of 2026-07-25, both the OPC/audit tables that were never provisioned
in any deployment (`opc_log`, `opc_action_log`): the read is caught by `IsMissingTableError` and the
write is caught by `AuditMiddleware`, which disables audit logging after the first failure and warns
once. If those two are all you see, the run is clean.

## What it does NOT cover

- **Interpolated SQL is skipped** — 29 statements build their text at run time (`$"""…{table}…"""`),
  so it isn't knowable statically. Reporting a guess as validated would be worse than reporting
  nothing, so they are counted and excluded. The paginated readers and the shape-geometry writers are
  the bulk of them.
- **Parsing is not executing.** It proves the statement compiles against the real schema; it says
  nothing about whether the values bind, the constraints hold, or the logic is right. `NOT NULL`
  violations, FK violations and wrong results all still need a real execution or a test.

## Why it exists

A sweep of this codebase found three real Oracle-only defect classes and three false ones, and reading
the code could not distinguish them. Compiling against the actual database can, cheaply, for a whole
category at once — and unlike a one-off investigation, it can be re-run after any change.
