# ABIS API + greenfield web modules

The REST **seam** over the ABIS database and the **greenfield web app** built on it.
ABIS was a 2-tier PowerBuilder client that talked straight to the database with no
service tier (see [`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md)); this API is
the integration point, and `wwwroot/ui/` + `clientapp/` are the typed web modules that
replace the legacy screens module-by-module
([`../docs/MODERNIZATION_ROADMAP.md`](../docs/MODERNIZATION_ROADMAP.md), Phase 3/4).

> **Scope:** ~160 endpoints across **31 tags** covering every rebuilt area —
> jobs, coils (+ inventory & ownership transfer), orders/order-entry, customers,
> parts, dies, skids/warehouse, scrap, sales (quote lifecycle), quotation, QA
> mechanical results, coil evaluation/QC, recovery, accounting, EDI, OPC log,
> scan, maintenance, shifts, downtime, shipping, receiving (with coil-minting),
> sketches, security/authorization, the production folder, the stacker line
> board, and a production/customer/inventory/QA **reporting** suite. Reads are
> paged + allowlist-sorted; writes are server-assigned-id, validated
> (`400 ProblemDetails`), and exercised against both the SQLite fixture and the
> live Oracle database. Full per-endpoint reference: the OpenAPI doc at
> `/swagger` (the table below lists the core surface by group).

## Stack

- **ASP.NET Core 8** minimal APIs
- **Dapper** for data access (portable SQL across engines)
- **Oracle** in production (`Oracle.ManagedDataAccess.Core`)
- **SQLite** for local dev / CI via a seeded in-process fixture
- **Swagger / OpenAPI** (served at `/swagger` in Development)

## Layout

```
api/
  src/ABIS.Api/
    Program.cs              app wiring (DI, config, Swagger, seeding)
    Endpoints/              minimal-API route definitions
    Models/                 read models (shapes follow docs/DATA_MODEL.md)
    Data/                   connection factory, repository (Dapper), SQLite fixture
    wwwroot/ui/             demo UIs (vanilla JS); /app is the compiled typed client
  tests/ABIS.Api.Tests/     xUnit: repository + in-process HTTP smoke tests
  clientapp/                TypeScript demo that consumes the generated client (tsc)
```

## Run locally

Requires the .NET 8 SDK.

```sh
cd api
dotnet run --project src/ABIS.Api          # Development profile: seeds SQLite
# browse http://localhost:5xxx/swagger          (API explorer)
# browse http://localhost:5xxx/ui/index.html    (order-entry demo UI)
```

The Development profile (`appsettings.Development.json`) uses
`Provider=Sqlite`, `Seed=true`, so the API comes up populated with sample data
and needs no external database.

### Web modules

~34 pages under `wwwroot/ui/`, the greenfield screens that replace the legacy
PowerBuilder windows. Most are TypeScript modules in `clientapp/src/` that consume
the **generated, typed client** (compiled by `tsc` to ES modules under `/ui/app/`,
committed so they run with no runtime build); a handful of early demos are vanilla
JS. Pages cross-link; sign in via OIDC or paste the dev key (`dev-local-key`).

By domain (page → what it does):
- **Commercial** — `order-entry`, `sales` (quote lifecycle), `quotation` (CirclePro
  calculator), `customers`, `parts`, `accounting` (invoice coils).
- **Coils & inventory** — `coil-inventory`, `coil-ownership` (toll-transfer +
  certificate), `receiving` (BOL + coil lines + mint), `skids`, `warehouse`.
- **Shop floor** — `jobs`, `das-console` (w_da_sheet), `stacker` (line board + error
  log), `coil-eval` (QC sheet), `prod-folder`, `downtime`, `scan`, `shifts`.
- **Quality** — `qa-results`, `quality` (recovery).
- **Logistics** — `shipping`, `carriers`, `edi`, `sketches`, `dies`.
- **Platform** — `security` (users/groups/permissions), `opc-log`, `reporting`
  (production / customer-shipment / inventory / QA-scrap, with CSV export).
- **`/ui/typed.html`** — the original codegen demo (coil screen + a typed
  create-order write) proving the generated-client loop end-to-end.

#### Typed client demo (`clientapp/`)

`clientapp/` is a small TypeScript app that imports the NSwag-generated client and
is compiled to browser ES modules (committed under `wwwroot/ui/app/`, so the demo
runs with no runtime build). After changing the API, regenerate and rebuild:

```sh
cd api
dotnet build src/ABIS.Api/ABIS.Api.csproj -c Release
dotnet tool restore && dotnet tool run swagger tofile --output openapi.json src/ABIS.Api/bin/Release/net8.0/ABIS.Api.dll v1
cd clientapp && npm ci && npm run all     # gen (NSwag) + build (tsc) -> ../src/ABIS.Api/wwwroot/ui/app/
```

CI compiles this on every run, so a contract change that breaks the typed UI fails the build.
CI also runs an **end-to-end check** (`clientapp/e2e/run.mjs`) that drives the generated
client against a live, seeded API instance — so a change that breaks the real
request/response path (not just compilation) fails too. Run it locally against a
running API with `ABIS_BASE=… ABIS_KEY=… npm --prefix clientapp run e2e`.

## Test

```sh
cd api
dotnet test                                # 174 tests: repository + HTTP smoke
```

The typed-client **e2e** suite (`clientapp/e2e/run.mjs`, **58 tests**) drives the
generated client against a live seeded API and is run by CI on every PR.

`api/requests.http` has ready-to-run sample calls (VS Code REST Client / JetBrains).

### OpenAPI contract & client codegen

The Swagger/OpenAPI document is served at `/swagger/v1/swagger.json` at runtime.
Every operation declares its **response types** (`.Produces<T>()`) — list
endpoints return a typed `…PagedResult`, single-gets a typed entity + `404`,
writes a `201`/`200` + `400` validation problem, and all `/api/*` a `401` — so
the contract is fully typed and codegen produces real models, not `any`.

Emit the contract as a file and generate a typed **TypeScript client** (CI does
both and uploads them as the `openapi` and `ts-client` artifacts):

```sh
cd api
dotnet build src/ABIS.Api/ABIS.Api.csproj -c Release
dotnet tool restore
# OpenAPI document:
dotnet tool run swagger tofile --output openapi.json src/ABIS.Api/bin/Release/net8.0/ABIS.Api.dll v1
# Typed fetch-based TypeScript client (models + methods):
dotnet tool run nswag openapi2tsclient /input:openapi.json /output:abis-client.ts /template:Fetch /className:AbisClient
```

Both generated files are git-ignored (build artifacts). Swap the NSwag template
(`/template:Angular`, `Axios`, …) for other TS flavors. CI also generates a
**Python client** with `openapi-generator` from the same `openapi.json` (uploaded
as the `python-client` artifact), demonstrating the contract is language-agnostic:

```sh
java -jar openapi-generator-cli.jar generate -i openapi.json -g python \
  -o python-client --additional-properties=packageName=abis_client
```

## Container

```sh
# build context is the api/ directory
docker build -f api/Dockerfile -t abis-api:latest api
docker run -p 8080:8080 \
  -e Database__Provider=Oracle \
  -e Database__ConnectionString="User Id=abis;Password=...;Data Source=//host:1521/ORCLPDB1" \
  -e ApiKeys__Keys__0="<a-strong-key>" \
  abis-api:latest
# -> http://localhost:8080/   (Kestrel listens on 8080 in the container)
```

CI builds this image on every PR (see `.github/workflows/ci.yml`).

## Endpoints

| Method & path | Description |
|---|---|
| `GET /` | Service info (name, version, environment, links) |
| `GET /health` | Liveness probe (process up; no dependencies touched) |
| `GET /health/ready` | Readiness probe (database reachable); 503 when not |
| `GET /api/jobs?page&pageSize&status&sort&dir` | List production jobs (paged, sortable) |
| `GET /api/jobs/{abJobNum}` | One job |
| `GET /api/jobs/{abJobNum}/coils` | Coils processed by a job (joined) |
| `GET /api/jobs/{abJobNum}/skids` | Finished sheet skids for a job |
| `GET /api/jobs/{abJobNum}/scrap` | Scrap skids for a job |
| `GET /api/jobs/{abJobNum}/partial-skids` | In-process partial skids for a job |
| `POST /api/jobs` | Create a production job → 201 |
| `PATCH /api/jobs/{abJobNum}` | Update job status / notes / men / finish time |
| `GET /api/coils?page&pageSize&status&alloy&location&customerId&sort&dir` | List coils (paged, filterable, sortable) |
| `GET /api/coils/summary?groupBy=alloy\|location` | Inventory weight rollup (count + net wt + balance) |
| `GET /api/coils/{coilAbcNum}` | One coil |
| `GET /api/coils/{coilAbcNum}/processing` | A coil's processing history (consuming jobs) |
| `POST /api/coils` | Create a coil on receipt (requires `coilAlloy2`) → 201 |
| `PATCH /api/coils/{coilAbcNum}` | Update coil status / location / notes |
| `GET /api/orders?page&pageSize&customerId&po&sort&dir` | List customer orders (paged, filterable, sortable) |
| `GET /api/orders/{orderAbcNum}` | One order header |
| `GET /api/orders/{orderAbcNum}/items` | Line items for an order |
| `GET /api/orders/{orderAbcNum}/full` | Order header + customer + items (order-entry read model) |
| `POST /api/orders` | Create an order header (server-assigned id) → 201 |
| `POST /api/orders/with-items` | Create an order + its line items in one transaction → 201 |
| `PUT /api/orders/{orderAbcNum}` | Replace an order header |
| `GET /api/order-items?page&pageSize&alloy&sort&dir` | List order items (paged, sortable) |
| `GET /api/orders/{orderAbcNum}/items/{orderItemNum}` | One order line item (composite key) |
| `POST /api/orders/{orderAbcNum}/items` | Add a line item to an order (line number assigned per order, requires `enduserPartNum`) → 201 |
| `PUT /api/orders/{orderAbcNum}/items/{orderItemNum}` | Replace an order line item |
| `GET /api/orders/{orderAbcNum}/items/{orderItemNum}/shape` | A line's blank geometry — the shape's dimensions (value + tolerances) and dies |
| `PUT /api/orders/{orderAbcNum}/items/{orderItemNum}/shape` | Set a line's shape geometry (upsert; aligns `sheet_type`; 400 on unknown shape) |
| `GET /api/customers?page&pageSize&name&sort&dir` | List customers (paged, sortable) |
| `GET /api/customers/{customerId}` | One customer |
| `POST /api/customers` | Create a customer (server-assigned id) → 201 |
| `PUT /api/customers/{customerId}` | Replace a customer |
| `GET /api/sheet-skids?page&pageSize&sort&dir` | List finished sheet skids (paged, sortable) |
| `GET /api/sheet-skids/{sheetSkidNum}` | One sheet skid |
| `POST /api/sheet-skids` | Create a sheet skid (requires `abJobNum`) → 201 |
| `GET /api/scrap-skids?page&pageSize&sort&dir` | List scrap skids (paged, sortable) |
| `GET /api/scrap-skids/{scrapSkidNum}` | One scrap skid |
| `POST /api/scrap-skids` | Create a scrap skid (requires `scrapAbJobNum`) → 201 |
| `GET /api/partial-skids?page&pageSize&sort&dir` | List in-process partial skids (paged, sortable) |
| `GET /api/parts?page&pageSize&customerId&alloy&sort&dir` | List part-number master records (paged, filterable, sortable) |
| `GET /api/parts/{partNumId}` | One part-number record |
| `POST /api/parts` | Create a part-number record (requires `customerId`) → 201 |
| `PUT /api/parts/{partNumId}` | Replace a part-number record |
| `GET /api/dies?page&pageSize&status&sort&dir` | List dies/tooling (paged, filterable, sortable) |
| `GET /api/dies/{dieId}` | One die |
| `POST /api/dies` | Create a die/tooling record (requires `dieName`) → 201 |
| `PUT /api/dies/{dieId}` | Replace a die/tooling record |
| `GET /api/shipments?page&pageSize&customerId&sort&dir` | List shipments / packing lists (paged, filterable, sortable) |
| `GET /api/shipments/{packingList}` | One shipment |
| `POST /api/shipments` | Create a shipment (packing-list & bill-of-lading numbers server-assigned) → 201 |
| `PUT /api/shipments/{packingList}` | Replace a shipment header |
| `PATCH /api/shipments/{packingList}` | Update dispatch status (status, sent/actual times, notes) |
| `GET /api/receiving-bols?page&pageSize&customerId&status&sort&dir` | List inbound receiving BOLs (paged, filterable, sortable) |
| `GET /api/receiving-bols/{receivingBolId}` | One receiving BOL |
| `POST /api/receiving-bols` | Create a receiving BOL (requires `bol`, `customerId`) → 201 |
| `PUT /api/receiving-bols/{receivingBolId}` | Replace a receiving BOL |
| `GET /api/edi/transactions?page&pageSize&customerId&transactionTypeId&sort&dir` | List outbound EDI transactions, newest first (paged, filterable, sortable) |
| `GET /api/edi/transactions/{ediFileId}` | One outbound EDI transaction |
| `GET /api/edi/log?page&pageSize&customerId&sort&dir` | List EDI transmission-log entries, newest first |
| `GET /api/scan-logs?page&pageSize&abJobNum&sort&dir` | List shop-floor scan events, newest first (paged, filterable, sortable) |
| `GET /api/scan-logs/{scanId}` | One scan event |
| `POST /api/scan-logs` | Record a scan event, append-only (requires `abJobNum`, `scanStation`, `note`) → 201 |
| `GET /api/jobs/{abJobNum}/scans` | Scan events for a job |
| `GET /api/maint-logs?page&pageSize&status&groupDepartmentId&sort&dir` | List maintenance log entries, newest first (paged, filterable, sortable) |
| `GET /api/maint-logs/{maintLogId}` | One maintenance log entry |
| `POST /api/maint-logs` | Create a maintenance log entry (requires `probDateTime`, `probDetails`, `author`) → 201 |
| `PUT /api/maint-logs/{maintLogId}` | Replace a maintenance log entry |
| `GET /api/carriers?page&pageSize&status&sort&dir` | List carriers / trucking partners (paged, filterable, sortable) |
| `GET /api/carriers/{carrierId}` | One carrier |
| `POST /api/carriers` | Create a carrier (requires `carrierFullName`) → 201 |
| `PUT /api/carriers/{carrierId}` | Replace a carrier |
| `GET /api/shifts?page&pageSize&lineNum&sort&dir` | List production shifts, newest first (paged, filterable, sortable) |
| `GET /api/shifts/{shiftNum}` | One shift |
| `POST /api/shifts` | Create a production shift → 201 |
| `PUT /api/shifts/{shiftNum}` | Replace a production shift |
| `GET /api/downtime?page&pageSize&abJobNum&shiftNum&sort&dir` | List downtime instances, newest first (paged, filterable, sortable) |
| `GET /api/downtime/{instanceNum}` | One downtime instance |
| `POST /api/downtime` | Log a downtime instance → 201 |
| `PUT /api/downtime/{instanceNum}` | Replace a downtime instance |
| `GET /api/customers/{customerId}/contacts` | Contacts for a customer |
| `POST /api/customers/{customerId}/contacts` | Add a contact to a customer (requires `lastName`) → 201 |
| `GET /api/customer-contacts/{contactId}` | One customer contact |
| `PUT /api/customer-contacts/{contactId}` | Replace a customer contact |
| `GET /api/sketches?page&pageSize&status&sort&dir` | List part sketches/drawings, no image (paged, filterable, sortable) |
| `GET /api/sketches/{sketchId}` | One sketch header (no image) |
| `POST /api/sketches` | Create a sketch header (requires `sketchName`; image not written) → 201 |
| `PUT /api/sketches/{sketchId}` | Replace a sketch header (image left untouched) |
| `GET /api/test-results?page&pageSize&testType&position&from&to&sort&dir` | List posted mechanical test results (paged, filterable, sortable) |
| `GET /api/temp-test-results?page&pageSize&testType&position&from&to&sort&dir` | List in-progress (working-set) test results (paged, filterable, sortable) |
| `GET /api/lookups/alloys` | Distinct alloys (dropdown reference data) |
| `GET /api/lookups/shape-types` | Blank shape catalog: each shape's dimension schema (names + tolerance flag) + die count (drives a dynamic per-shape form) |
| `GET /api/lookups/lines` | Production lines (referenced by jobs, coils, downtime) |
| `GET /api/lookups/groupdepartments` | Maintenance groups/departments (referenced by maintenance logs) |
| `GET /api/lookups/downtime-causes` | Downtime causes/reasons |
| `GET /api/lookups/transportation-methods` | Transportation method codes (referenced by shipments) |
| `GET /api/lookups/equipment-types` | Shipping equipment type codes (referenced by shipments) |
| `GET /api/lookups/customer-types` | Customer classifications (referenced by customers) |
| `GET /api/lookups/edi-types` | EDI transaction-set types + X12 versions (table `edi_type`) |
| `GET /api/lookups/customer-edi` | Customer EDI trading-partner configuration (table `customer_edi`) |
| `GET /api/audit-log?page&pageSize&source&sort&dir` | List the action/audit log, newest first (sortable) |
| `PATCH /api/sheet-skids/{n}/warehouse` | Warehouse update of a skid (location / ticket / status) |
| `GET /api/accounting/rej-reband-coils?abJobNum=` | Rejected/rebanded coils that drive a job's invoice |
| `GET /api/quality/{scrap-types,product-types,recovery-customers,customer-defects?customerId=}` | Recovery / customer-defect setup |
| **Sales** | `GET /api/sales/quotes?search=`, `/quotes/{id}/{rev}`, `/contacts?customerId=`, `…/events`, `…/probability` (+ POST events/probability) — quote lifecycle, follow-ups, win-probability |
| **Coil ownership** | `GET /api/coil-ownership/transfers?customerId=`, `…/{cert}/certificate`, `…/transferable-coils`; `POST /api/coil-ownership/transfers` (issues certificate, re-points coil owner) |
| **Security** | `GET /api/security/{users,users/{id},users/{id}/groups,users/{id}/permissions,groups,applications}`; `GET /api/security/me/{permissions,allowed}`; `POST /api/security/{users,groups,applications}`; `PUT …/applications/{id}` grants; user↔group membership — per-feature authorization (MAX of direct+group), gate enforced on the admin writes |
| **Receiving (lines)** | `GET /api/receiving-bols/{id}/{detail,coils}`; `POST …/coils`; `DELETE …/coils/{coilId}`; `POST …/{mint,generate-861}` — coil line items + coil-minting (861 = DB-side stub) |
| **Coil eval / QC** | `GET /api/coil-eval/coils?abJobNum=`, `…/skids/{n}/dimension-checks`, `…/jobs/{n}/eval-scrap`; `POST` dimension-checks + eval-scrap |
| **Production folder** | `GET /api/prod-folder/jobs/{n}` (summary), `…/notes`; `POST …/notes` — e-folder notes |
| **Stacker** | `GET /api/stacker/board?lineNum=`, `/stacker/line-errors?lineNum=&from=&to=`; `POST /stacker/line-errors` — line board + fault log |
| **Reporting** | `GET /api/reporting/{production-summary,line-efficiency,monthly-production,downtime,on-time,customer-shipments,open-shipments,customer-orders,customer-skid-count,coil-inventory,coil-on-hold,skid-inventory,unmatched-coils,qa-mechanical,scrap-summary,scrap-by-job}` — 19 report endpoints (UI viewer + CSV export) |

Collections return a paged envelope: `{ items, page, pageSize, totalCount, totalPages }`.
The grouped rows above summarize each tag; see `/swagger` for the full per-endpoint contract.

**Sorting.** List endpoints accept `sort` (a field name) and `dir` (`asc`/`desc`,
default `asc`). Sortable fields are **allowlisted per resource** and mapped to
physical columns server-side (so only known columns and a validated direction
reach the SQL — injection-safe); a stable tie-breaker (usually the primary key)
is appended for deterministic paging. An unknown `sort` field or a bad `dir`
returns `400` with a ProblemDetails listing the allowed fields. Without `sort`,
each list keeps its natural default order (e.g. test results and the audit log
default to newest-first).

**Readiness vs liveness.** `GET /health` is a cheap liveness check (the process
is up). `GET /health/ready` opens a database connection and runs a trivial probe
(`SELECT 1`, or `SELECT 1 FROM dual` on Oracle), returning `200 {status:"ready"}`
or `503 {status:"unavailable"}` — point an orchestrator's readiness gate here so
traffic is held until the data path serves. Both are anonymous.

**Audit trail.** Every mutating request (POST/PUT/PATCH/DELETE under `/api`) is
recorded in the legacy `opc_action_log` table by `AuditMiddleware` (source =
`"{method} {path}"`, success = HTTP status < 400). Auditing is best-effort and
never fails the request. Read it back via `GET /api/audit-log`.

**Write semantics.** `POST /api/customers` requires `customerName` (else 400) and
returns 201 with a `Location` header. `PATCH` applies a partial update — omitted
(null) fields are left unchanged (so a field cannot be cleared to null via PATCH),
and an unknown id returns 404. Ids are server-assigned inside a transaction by the
connection factory's dialect-specific next-id SQL: `MAX+1` on the SQLite dev
fixture (single-writer, keeps seed ids tidy) and a **sequence `NEXTVAL` on Oracle**
(concurrency-safe). Sequence names default to the `{table}_seq` convention and are
overridable per table — see *Configuration* below. *(The audit-log insert still
uses `MAX+1`; it is append-only and best-effort.)*

## Authentication

`/api/*` accepts **either** of two schemes (a request is authorized if either
yields a valid principal); `/health` and Swagger stay anonymous:

- **API key** (`X-Api-Key` header) — for machine clients (the edge service, scripts).
- **JWT bearer** (`Authorization: Bearer …`) — for interactive users, when JWT is
  configured. Provider-agnostic: point it at any OIDC issuer.

```sh
curl -H "X-Api-Key: dev-local-key" http://localhost:5xxx/api/jobs        # API key
curl -H "Authorization: Bearer <jwt>" http://localhost:5xxx/api/jobs      # JWT
```

**JWT** is enabled by configuring the `Auth:Jwt` section (off by default → API-key
only, backward compatible):

```sh
# OIDC provider (Entra ID / Keycloak / Auth0 / …)
export Auth__Jwt__Authority="https://login.example.com/realms/abis"
export Auth__Jwt__Audience="abis-api"
# …or a symmetric signing key (no OIDC provider)
export Auth__Jwt__SigningKey="<a-long-random-secret>"
export Auth__Jwt__Issuer="https://issuer.example"
```

Configure the **API key** via the `ApiKeys` section (keys belong in a secret store
/ environment, never in source):

```sh
export ApiKeys__Enabled="true"
export ApiKeys__Keys__0="<a-strong-key>"
export ApiKeys__Keys__1="<another-key>"     # multiple keys supported
```

### Web-UI sign-in (browser OIDC)

The greenfield screens run a standard **Authorization Code + PKCE** login (no client
secret, no bundler — just `fetch` + Web Crypto in `clientapp/src/auth.ts`). On load
each page calls anonymous `GET /auth/config`:

- **OIDC configured** → the page shows **Sign in**; users authenticate at your
  provider and every API call carries `Authorization: Bearer …`.
- **Not configured** → the page falls back to the **X-Api-Key** field (the dev flow).

Enable it with the `Auth:Oidc` section — the *browser* client (public `ClientId`),
separate from the API's `Auth:Jwt` token-validation config above:

```sh
export Auth__Oidc__Authority="https://login.example.com/realms/abis"  # same issuer as Auth:Jwt
export Auth__Oidc__ClientId="abis-spa"                                # a PUBLIC SPA client
export Auth__Oidc__Scope="openid profile api://abis/.default"         # must yield a token whose aud = Auth:Jwt:Audience
```

In the provider, register the SPA client with each screen's URL as a redirect URI
(e.g. `https://<host>/ui/order-entry.html`, … — or a wildcard `https://<host>/ui/*`).
The access token's audience must match `Auth:Jwt:Audience` so the API accepts it.

The dev profile ships a throwaway key (`dev-local-key`). Set `ApiKeys__Enabled=false`
only on a trusted internal network. In Swagger UI, use **Authorize** to supply the key.

## Rate limiting & security headers

The `/api` group is protected by a **fixed-window rate limiter**, partitioned per
API key (falling back to the remote IP) so one noisy client can't starve others or
hammer the legacy database. Exceeding the window returns `429` (ProblemDetails +
`Retry-After`). Tune via the `RateLimiting` section:

```sh
export RateLimiting__Enabled="true"      # set false to remove the limiter
export RateLimiting__PermitLimit="1000"  # requests per window per partition
export RateLimiting__WindowSeconds="10"
```

Every response also carries baseline security headers: `X-Content-Type-Options:
nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: no-referrer`.

**Correlation IDs.** Each request gets an `X-Request-Id` (a caller-supplied one is
honored when it looks safe, otherwise generated), echoed on the response, surfaced
in `ProblemDetails` (`requestId`), and recorded in the audit-log notes — so a call
can be traced across client, API, and the legacy `opc_action_log`.

**Conditional GETs.** `/api` GET responses carry a weak `ETag`; a caller that sends
a matching `If-None-Match` gets `304 Not Modified` with no body — cheap bandwidth
savings for the polling shop-floor screens.

**Optimistic concurrency (`If-Match`).** Every PUT/PATCH honours `If-Match`: send
the `ETag` you got from a GET, and the write is rejected with `412 Precondition
Failed` if the row changed in the meantime (omit the header to skip the check).
The legacy schema has no row-version column, so the check compares the row's
current **content-hash** ETag — no schema change, and it coexists with the legacy
client that writes the same rows.

## Configuration (production / Oracle)

Set the `Database` section — preferably via environment variables so secrets
stay out of source:

```sh
export Database__Provider="Oracle"
export Database__Seed="false"
export Database__ConnectionString="User Id=abis;Password=...;Data Source=//host:1521/ORCLPDB1"
export ASPNETCORE_ENVIRONMENT="Production"
dotnet run --project src/ABIS.Api
```

**Sequences (Oracle).** Inserts draw ids from a sequence per table. By default the
name follows the live ABIS `{id_column}_seq` convention
(`Database__SequenceNameFormat`, default `{0}_seq`, applied to the table's id
column — e.g. `coil_abc_num` → `coil_abc_num_seq`); override individual tables for
names that don't fit:

```sh
export Database__SequenceNameFormat="{0}_seq"          # coil_abc_num -> coil_abc_num_seq
export Database__Sequences__ab_job="ABIS.AB_JOB_S"     # per-table override (schema-qualified ok)
export Database__Sequences__coil="COIL_SEQ"
```

Several overrides ship in `appsettings.json` for sequences that don't fit the
convention: `customer_contact` → `customer_contact_id_seq` (id column is
`contact_id`), `dt_instance` → `dt_instance_seq`, `scan_log` → `scan_log_seq`,
and `shipment` → `packing_list_num_seq`. The `shipment` insert also needs a
second generated number (`bill_of_lading`, NOT NULL) drawn from its own
`bill_of_lading_seq`, passed explicitly so the table-keyed override isn't applied
to it. Resolved names are validated as plain/schema-qualified identifiers before use.

**Tables without a sequence.** Some legacy tables (e.g. `maint_log`) have no
Oracle sequence — the application assigns the id. List them under
`Database:MaxIdTables` and the next-id falls back to `MAX(id)+1` on Oracle too
(the legacy behaviour; low-volume tables only, as MAX+1 is not concurrency-safe):

```sh
export Database__MaxIdTables__0="maint_log"
```

**Confirm the real sequence names against the database** (Phase 1) — these are
conventions, not recovered facts.

The repository SQL is engine-portable (`:name` parameters; columns aliased to
model property names; dialect-specific paging and next-id supplied by the
connection factory). The Oracle path was **validated end-to-end** against the live
non-prod database (read + write/update); the recurring live-only traps — ORA-01745
reserved-word binds, ORA-00932 CHAR-null COALESCE, Int64 unboxing — are coded
against throughout (see [`../docs/ORACLE_VALIDATION.md`](../docs/ORACLE_VALIDATION.md)).
A secret-gated `oracle-smoke` CI job runs it when `ORACLE_CONNECTION_STRING` is set.
Model shapes are reconciled against the full recovered schema and each built module
was cross-checked against the real DataWindow/Oracle columns
([`../docs/data-model/BACKCHECK.md`](../docs/data-model/BACKCHECK.md)) — e.g.
`order_item`'s composite PK `(order_abc_num, order_item_num)` is **confirmed** (no
longer inferred).
