# Oracle-only defect sweep — confirmed findings (2026-07-25)

**Production is Oracle 11g. CI is SQLite. This entire class of defect is invisible to CI.**

Found by an adversarially-verified sweep: 8 specialist finders, then 3 reviewers per finding
each trying to REFUTE it. Only findings that survived >= 2 of 3 are listed as confirmed.

63 candidates | **17 confirmed** | 1 refuted | 45 UNVERIFIED (reviewer ran out of budget — NOT cleared)

## Two systemic root causes

1. **Reserved-word bind names** (`:from` `:to` `:by` `:when` `:start` `:end` `:between`) —
   ORA-01745 at parse time. The statement never runs.
2. ~~**Positional binding.**~~ **DISPROVEN — verified against live Oracle 2026-07-25.**
   The sweep claimed that because `BindByName` is unset, a parameter object whose member order
   differs from the placeholder order silently writes values into the wrong columns. **It does not.**
   **Dapper reorders parameters to match the SQL text before executing**, so ODP.NET never sees a
   mismatch. Proven both ways on .230 with `BindByName=false`: six deliberately scrambled members,
   and a `DynamicParameters` built in the wrong order (the exact shape `PartParams` uses) — both
   bound correctly.

   **Every finding in this class is a false positive**, including `CreatePartAsync` ("all 54 columns
   shifted"), `UpdateSecurityUserAsync`, `UpdateDimensionCheckAsync`, `UpsertEvalScrapAsync`,
   `UpsertRecoveryJobCoilAsync`, the order/order-item INSERTs and the truck-appointment writes.

   Worth recording HOW this got through: three adversarial reviewers confirmed it, and I confirmed
   it myself by reading the code. But all four of us verified the *code shape* the claim described —
   member order really does differ from placeholder order — and none of us tested the *premise* that
   the shape causes a wrong result. Reading code proves what code says; only running it proves what
   it does.
3. **A bind name reused for two placeholders** → ORA-01008. Untested against live Oracle; treat as
   unconfirmed until it is, given class 2's fate.

## Confirmed

| Sev | Site | Defect |
|---|---|---|
| critical | `AbisRepository.cs:807` | CreateCustomerAsync uses :ts twice — Oracle needs one parameter per placeholder occurrence, so customer creation raises ORA-01008 |
| critical | `AbisRepository.cs:1313` | Order and order-item INSERTs bind the generated key and created-date last while the SQL places them first |
| critical | `AbisRepository.cs:2211` | UpsertRecoveryJobCoilAsync UPDATE branch binds the key columns into the value columns |
| critical | `AbisRepository.cs:4061` | CreatePartAsync binds all 54 part_num parameters one position off — the whole part master row is written to the wrong columns |
| critical | `AbisRepository.cs:5710` | Every outbound EDI document dies on Oracle: `:from`/`:to` reserved-word binds in the shared EDI sink |
| critical | `AbisRepository.cs:6918` | MintBolCoilsAsync repeats :now and :net in the coil INSERT — receiving-BOL coil minting fails on Oracle |
| critical | `AbisRepository.cs:7093` | UpsertEvalScrapAsync binds parameters out of placeholder order in BOTH branches — silently updates the wrong QC scrap row on Oracle |
| high | `AbisRepository.cs:714` | Coil QA hold/release write fails on Oracle: `:by` (and `:when`) reserved-word binds |
| high | `AbisRepository.cs:1283` | Coil quality-flaw add and delete fail on Oracle: `:start`/`:end` reserved-word binds |
| high | `AbisRepository.cs:3245` | UpdateSecurityUserAsync binds user_id first while :id is last in the SQL — the admin user edit corrupts or misses its row |
| high | `AbisRepository.cs:5081` | Warehouse skid create and modify both 500 on Oracle: `:from` reserved-word bind |
| high | `AbisRepository.cs:7008` | UpdateDimensionCheckAsync shifts every QC dimension value by two columns |
| high | `AbisRepository.cs:7149` | Job-folder note write binds a formatted string into a NOT NULL Oracle DATE column |
| high | `AbisRepository.cs:8542` | PM create and update fail on Oracle: `:between` reserved-word bind |
| high | `AbisRepository.cs:8718` | PM completion write fails on Oracle: `:by` reserved-word bind |
| high | `AbisRepository.cs:9049` | Every truck-appointment write is mis-bound on Oracle (duplicate :now and id-first parameter order) |
| high | `AbisSchema.cs:513` | Startup sequence self-heal misses PROD_ITEM_NUM_SEQ, BILL_OF_LADING_SEQ and SHEET_PACKAGING_TICKET_SEQ |

### Detail

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:807 — CreateCustomerAsync uses :ts twice — Oracle needs one parameter per placeholder occurrence, so customer creation raises ORA-01008

`VALUES (:id, :ts, :ts, {CustomerWriteVals})` has 39 placeholder occurrences but only 38 parameters are supplied, and the two key parameters (`id`, `ts`) are Add()-ed after the 36 CustomerBinds so they also bind to the wrong positions.

**Failure:** POST /customers on Oracle: ODP.NET binds parameters by position (BindByName defaults to false) and treats each occurrence of :ts as its own bind slot, so the 39th slot is left unbound → ORA-01008 'not all variables bound'. Customer creation is impossible on the plant database while the SQLite test suite passes, because SQLite binds by name and reuses :ts.

**Reviewer correction:** The claim is real but slightly narrow in two ways.

1) The primary defect is the ORDERING, not just the duplicate. Even if :ts were bound twice, the 36 CustomerBinds occupy positions 1-36 while the SQL expects id/ts/ts there — so customer_id would receive CustomerName, etc. The duplicate :ts merely converts a silent misbind into a guaranteed ORA-01008.

2) UpdateCustomerAsync (AbisRepository.cs:814-823) has the same class of defect and is NOT covered by the claim. `UPDATE customer SET {CustomerSetClause}, customer_maint_date = :ts WHERE customer_id = :id` has 38 placeholders and 38 parameters (so no ORA-01008), but positions 37/38 are :ts then :id while the parameters are added id then ts — swapped. On Oracle that writes the customer id into customer_maint_date and compares customer_id against a timestamp: ORA-00932/ORA-01722, or at best 0 rows updated returning a spurious 404. PUT /cust

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:1313 — Order and order-item INSERTs bind the generated key and created-date last while the SQL places them first

CreateOrderAsync, CreateOrderItemAsync and CreateOrderWithItemsAsync build `new DynamicParameters(OrderBinds(body))` and then `p.Add("id")` / `p.Add("created_date")` / `p.Add("ord")`, but every one of those INSERTs writes `VALUES (:id, :created_date, {OrderInsertVals})` — the appended parameters bind to the last positions, not the first.

**Failure:** POST /orders on Oracle inserts order_abc_num = body.OrigCustomerId and created_date = body.EnduserId, then shifts every remaining column by two; the trailing positions receive the freshly minted order id and a DateTime destined for varchar columns. The order is created under a bogus primary key (colliding with a real order id) or the statement fails with ORA-01722/ORA-01858. The same defect applies to order lines (order_item_num ← body.EnduserPartNum).

**Reviewer correction:** The defect is real; two details in the write-up need tightening.

1) Types/consequence. CustomerOrderWrite.OrigCustomerId and .EnduserId are `long?` (Models/Requests.cs:300-301), not strings. So on Oracle the shift is: order_abc_num <- OrigCustomerId (a valid NUMBER, i.e. the order silently gets the CUSTOMER id as its primary key — either colliding with an existing order_abc_num via ORA-00001 or creating a real row under a bogus id while the freshly minted sequence value is thrown at a trailing varchar column), created_date <- EnduserId (Int64 into a DATE -> ORA-00932/ORA-01858), orig_customer_id <- OrigCustomerPo (string into NUMBER -> ORA-01722), and so on down the line. The likeliest observed outcome is therefore a hard failure of POST /orders rather than silent corruption — but with a sparse body (most fields null) the type mismatches can vanish and a wrong-PK row can be committed. B

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:2211 — UpsertRecoveryJobCoilAsync UPDATE branch binds the key columns into the value columns

The shared `p` object is ordered (coil, job, sa, sh, rej, reb, pt) — correct for the INSERT at line 2218 — but the UPDATE at 2213 places (:sa, :sh, :rej, :reb, :pt) before (:coil, :job), so positional binding shifts every value by two.

**Failure:** An operator edits the recovery flags on an EXISTING (coil, job) worksheet row. On Oracle the statement runs as `SET special_attention = <coilAbcNum>, special_handling = <abJobNum>, coil_rejected = <specialAttention>, coil_rebanded = <specialHandling>, product_type_id = <coilRejected> WHERE coil_abc_num = <coilRebanded> AND ab_job_num = <productTypeId>`. Because the flags and ids are all numeric this executes without error and either silently corrupts an unrelated recovery row or updates nothing while the API reports success.

**Reviewer correction:** Two refinements to the claim, neither of which rescues the code:

(a) The realistic failure is not "corrupts an unrelated recovery row" — it is "silently discards the edit". The endpoint (ApiEndpoints.cs:2804-2807) constrains all four flags to 0, 1 or null, so the rewritten WHERE becomes `coil_abc_num = <0|1|NULL> AND ab_job_num = <productTypeId|NULL>`, which matches zero rows in practice (and NULL never matches anything). The UPDATE therefore affects 0 rows, `GetRecoveryCoilsByJobAsync` re-reads the untouched row at line 2224, and the API returns 200 with the OLD flag values — an operator's edit to an existing worksheet row is lost with no error, on Oracle only. Corruption of a real row would additionally require a coil whose `coil_abc_num` is literally 0 or 1. On that basis I'd rate this high, not critical.

(b) The phrase "shifts every value by two" is loose; the accurate statement (w

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:4061 — CreatePartAsync binds all 54 part_num parameters one position off — the whole part master row is written to the wrong columns

PartParams() returns the PartColumnBinds anonymous template first and appends `id` and `item_status` last (lines 4069-4075), but the INSERT's placeholder list starts `:id, :customer_id, :enduser_id, :enduser_part_num, :item_status, :sheet_type, …` — so with ODP.NET positional binding every value is shifted.

**Failure:** Creating a part on Oracle binds body.CustomerId into part_num_id, body.EnduserId into customer_id, and body.EnduserPartNum (a string) into enduser_id (NUMBER) → ORA-01722, or, where the shifted types happen to be compatible, a part row is inserted under a part_num_id equal to the customer id, colliding with or shadowing an existing part. Either way part creation is broken on the plant DB while green on SQLite.

**Reviewer correction:** The defect is real but narrower in wording and broader in scope than stated.

Wording: it is not a uniform one-position shift. With 52 template binds against 54 placeholders the mapping is: :id←CustomerId, :customer_id←EnduserId, :enduser_id←EnduserPartNum (string into NUMBER → ORA-01722), :enduser_part_num←SheetType, then :item_status←Alloy and a two-position shift through the rest, with the final two placeholders (:item_attachments, :govt_contract_num) receiving the id and item_status values. The dominant failure is a hard ORA-01722/ORA-01400 on every part create, not silent misfiling — though which one you get depends on which shifted pairs happen to be type-compatible.

Scope: the same "template first, ids Add()-ed last, but the SQL puts them FIRST" pattern appears at three more write sites and should be fixed together:
- api/src/ABIS.Api/Data/AbisRepository.cs:1309-1314 CreateOrderA

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:5710 — Every outbound EDI document dies on Oracle: `:from`/`:to` reserved-word binds in the shared EDI sink

`WriteEdiTransactionAsync` — the single INSERT that every 861/870/856/846 generator funnels through — binds parameters named `:from` and `:to`, both Oracle reserved words, so the statement fails to parse with ORA-01745 on Oracle 11g while passing on SQLite.

**Failure:** An operator receives a BOL and the app calls POST /receiving-bols/{id}/edi/861 (or any 870/856/846 endpoint). `PersistEdi861Async` → `WriteEdiTransactionAsync` executes `INSERT INTO outbound_edi_transaction (... duns_from, duns_to ...) VALUES (:fileId, :from, :to, ...)`. Oracle rejects the bind names at parse time: `ORA-01745: invalid host/bind variable name`. The transaction rolls back, the endpoint returns 500, and NO EDI document is ever produced or recorded — for every transaction set and every trading partner. CI stays green because SQLite accepts `from`/`to` as parameter names, and the golden byte-fidelity tests only exercise the generators, not this insert.

**Reviewer correction:** Two fixes to the claim as stated:

(a) Wrong line cited. Line 5710 is `DELETE FROM abis_edi_partner WHERE customer_id = :c AND transaction_set = :t` — harmless. The defect is at C:\Users\codim\ABIS\api\src\ABIS.Api\Data\AbisRepository.cs:5741 (SQL) / :5745 (the `from =` / `to =` members).

(b) Scope is understated — this is not EDI-only. The same reserved-word binds appear in three other live paths that will throw ORA-01745 identically:
  - line 5059: `INSERT INTO sheet_skid (... skid_from_if_whed ...) VALUES (:skid, :job, ..., :from, :ticket, ...)` — the warehouse/DAS skid mint
  - line 5278: `UPDATE ... SET skid_from_if_whed = :from, skid_ticket_if_whed = :ticket`
  - lines 9055-9056: `scheduled_start >= :from` / `scheduled_start < :to` in the schedule range filter — this one is conditional, so it only fires when the caller supplies a from/to date, which is exactly why it would look in

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:6918 — MintBolCoilsAsync repeats :now and :net in the coil INSERT — receiving-BOL coil minting fails on Oracle

The VALUES list has 13 placeholder occurrences (:now twice, :net twice) but the anonymous parameter object supplies only 11 values.

**Failure:** POST to mint coils for a receiving BOL: on Oracle positions 12 and 13 are never bound → ORA-01008, so no COIL inventory rows are created and no receiving_bol_coil line gets its coil_abc_num. The whole inbound-receiving path (the handheld-scanner replacement) is dead on the plant DB. On SQLite the repeated names resolve to the same parameter and the test passes.

**Reviewer correction:** The defect is real but two details are mis-stated. (1) The cited line 6918 is wrong: 6918 is the DELETE in DeleteReceivingBolCoilAsync, and the "Lines 6914-6926" evidence range is off. The offending statement is the INSERT at AbisRepository.cs:6945-6957, with the VALUES list on line 6949 and the 11-property parameter object on 6951-6956. (2) The same defect occurs a second time at AbisRepository.cs:9080 in CreateTruckAppointmentAsync — `..., :quantity, 0, :notes, :now, :now, :updby)` has 19 placeholder occurrences supplied by 18 properties, so truck-appointment creation (POST /truck-appointments) fails on Oracle with ORA-01008 for the same reason. Fix both by giving each occurrence a distinct bind name (e.g. :received/:entered and :net/:netbal; :created/:updated) with the parameter-object property order matching placeholder order, per the convention documented at 8597-8600.

#### [CRITICAL] api/src/ABIS.Api/Data/AbisRepository.cs:7093 — UpsertEvalScrapAsync binds parameters out of placeholder order in BOTH branches — silently updates the wrong QC scrap row on Oracle

The `vals` object is declared in the order (coil, job, type, od, mill, piece, net, note) but the UPDATE's placeholders appear as (:piece, :net, :note, :coil, :job, :type, :od, :mill) and the INSERT's as (:coil, :job, :type, :piece, :net, :note, :od, :mill); ODP.NET binds positionally, so every value lands on the wrong placeholder.

**Failure:** QC saves an eval-scrap line for coil 5001 / job 1001 / type 3, pieces 40, net 120.5. On Oracle the UPDATE executes as `SET scrap_item_piece = 5001, scrap_item_net_wt = 1001, scrap_item_note = 3 ... WHERE coil_abc_num = 40 AND ab_job_num = 120.5 AND scrap_item_type = <note>`. All the shuffled values are numeric, so nothing throws — the statement either matches zero rows (and then the equally-misordered INSERT writes a row with the key columns holding weights) or matches an unrelated coil/job row and overwrites its scrap piece count and net weight. Scrap weight feeds job yield and invoicing, so the corruption is silent and propagates into billing. SQLite CI binds by name and passes.

**Reviewer correction:** The defect is real but the citation and the failure mapping need fixing.

Correct location: api/src/ABIS.Api/Data/AbisRepository.cs:7117-7138 (`UpsertEvalScrapAsync`). `vals` is at 7122-7123; the UPDATE SQL at 7126-7127; the INSERT at 7132-7134. The claim's line numbers (7093, 7089-7092, 7095) are ~30 lines off.

Correct misbinding for the UPDATE (params bound in order coil, job, type, od, mill, piece, net, note against first-occurrence placeholders piece, net, note, coil, job, type, od, mill):
  SET scrap_item_piece = coil, scrap_item_net_wt = job, scrap_item_note = type
  WHERE coil_abc_num = od AND ab_job_num = mill AND scrap_item_type = piece AND scrap_item_od = net AND scrap_item_mill = note
Since `ScrapItemNote` is a `string?` bound into numeric `scrap_item_mill`, a non-numeric note raises ORA-01722 instead of silently updating; with a NULL/empty note the UPDATE simply matches zero

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:714 — Coil QA hold/release write fails on Oracle: `:by` (and `:when`) reserved-word binds

`WriteQaTransitionAsync` inserts the COIL_TRACK_QA audit row with binds named `:when` and `:by`; BY is an Oracle reserved word that this project has already observed raising ORA-01745 on the live database.

**Failure:** QA places a coil on hold (POST /coils/{n}/qa-hold) or releases it (/qa-release). The `UPDATE coil SET coil_status` succeeds, then the audit INSERT `VALUES (:coil, :when, :pre, :cur, :by, :note)` raises ORA-01745, the transaction rolls back, and the endpoint 500s — so a coil can never be put on or taken off QA hold on the plant DB. The quality-hold audit trail is unwritable.

**Reviewer correction:** Two corrections, both widening rather than weakening the finding:

(a) The `:by` regression is NOT confined to `WriteQaTransitionAsync`. The same reserved-word bind was reintroduced in three other live write paths in the same file, all of which will ORA-01745 identically on Oracle:
  - `AbisRepository.cs:7463-7474` `UpsertPlcFaultCodeAsync` — `by = updatedBy` used in both the UPDATE (`updated_by = :by`, line 7466) and the fallback INSERT (line 7473). PLC fault-code descriptions can never be saved.
  - `AbisRepository.cs:8749` PM-completion INSERT into `pmcompletions` (`..., :grp, :by, :notes, ...`).
  - `AbisRepository.cs:8761` the paired `UPDATE pm SET pm_completed = :completed, completed_by = :by, ...`.
  The PM pair is the more serious of the three: completing a PM is a routine plant action, and both statements sit in one transaction (`tx` opened at line 8741), so PM completion is ful

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:1283 — Coil quality-flaw add and delete fail on Oracle: `:start`/`:end` reserved-word binds

`AddCoilQualityFlawAsync` (line 1283) and `DeleteCoilQualityFlawAsync` (line 1299) use bind names `:start` and `:end` — both already confirmed by this project's live Oracle run to raise ORA-01745.

**Failure:** QA records a flaw region on a coil (POST /coils/{n}/quality/flaws) or removes one (DELETE .../quality/flaws). Oracle rejects the statement at parse time with `ORA-01745: invalid host/bind variable name`; no flaw mapping can ever be recorded or deleted on the plant database, so the coil-quality flaw map is permanently empty in production while the SQLite fixture shows it working.

**Reviewer correction:** The claim is accurate. Two small refinements: (a) the two statements are the ONLY remaining `:start`/`:end` binds in the whole API source, so the fix is contained to renaming the binds (e.g. `:spos`/`:epos`) plus the matching `p.Add(...)` names at lines 1273-1274 and the anonymous-object members at line 1300 — note the DELETE's anonymous object order (id, start, end, code) already matches the WHERE placeholder order, so a rename must preserve that order for ODP.NET positional binding; (b) the coil-quality header upsert at lines 1243-1258 is NOT affected — its binds are all safe — so only the flaw-mapping add/delete is broken.

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:3245 — UpdateSecurityUserAsync binds user_id first while :id is last in the SQL — the admin user edit corrupts or misses its row

The parameter object is `new { id, login, last, first, mi, status, notes, modified }` but the statement's placeholder order is (:login, :last, :first, :mi, :status, :notes, :modified, :id), shifting every value by one.

**Failure:** Editing a user in the Security admin page on Oracle executes `SET login_id = <userId>, user_last_name = <loginId>, user_first_name = <lastName>, … , last_modified_date = <userNotes> WHERE user_id = <DateTime>` — the date/number type clashes raise ORA-01858/ORA-01722, or where the types line up the row's login_id is overwritten with a numeric id and the WHERE matches nothing, so the API reports failure/no-change. RBAC identity data is the thing being mangled.

**Reviewer correction:** The mismatch is real, but the stated outcome should be tightened, and the finding is under-scoped:

(a) Outcome: on Oracle this will almost certainly raise rather than silently corrupt. With the rotation, user_middle_initial receives the first name (ORA-12899 on a 1-char column), user_status (NUMBER) receives the middle initial (ORA-01722 for any non-numeric char), last_modified_date (DATE) receives user_notes (ORA-01858/01861), and the WHERE compares user_id (NUMBER) against a DateTime. The statement fails atomically, so there is no partial row corruption — the concrete failure is that editing any user in the Security admin page returns a 500 (or, if every value happened to coerce, updates zero rows and the endpoint returns 404 Not Found). "Corrupts the row" is not the realistic outcome; "the admin user-edit endpoint is unusable on Oracle" is.

(b) Scope: the same id-first / :id-last in

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:5081 — Warehouse skid create and modify both 500 on Oracle: `:from` reserved-word bind

`CreateWarehouseSkidAsync` (line 5081) and `ModifyWarehouseSkidAsync` (line 5300) bind `skid_from_if_whed` to a parameter named `:from`, an Oracle reserved word — ORA-01745 on parse.

**Failure:** A warehouse clerk saves a warehoused skid (POST /warehouse/skids, shipped in #317, or the modify path from #320). The INSERT into sheet_skid raises `ORA-01745: invalid host/bind variable name` before any row is written; the transaction rolls back after the status-20 shell coil has already been minted in the same tx (so nothing persists, but the endpoint 500s on every attempt). The entire warehouse feature — the newest one in the repo — is non-functional on the plant database while its SQLite tests pass.

**Reviewer correction:** The bug is real but the line numbers are wrong and the scope is understated.

Correct lines: the INSERT is `AbisRepository.cs:5059` (not 5081 — 5081 is inside the unrelated `production_sheet_item` insert), and the UPDATE is `AbisRepository.cs:5278` (not 5300 — 5300 is inside the previous-coil garbage-collect block).

Two more instances of the same `:from` defect exist outside the warehouse module and should be fixed in the same pass:
- `AbisRepository.cs:5741` — `INSERT INTO outbound_edi_transaction ... VALUES (:fileId, :from, :to, ...)`. This has TWO reserved-word binds: `:from` AND `:to` (TO is also `RESERVED='Y'`). Every EDI document generation that stamps an outbound transaction row would 500 on Oracle.
- `AbisRepository.cs:9055-9056` — `GetTruckAppointmentsAsync` adds `scheduled_start >= :from` and `scheduled_start < :to`. This one is a user-reachable READ on the shipped truck-sched

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:7008 — UpdateDimensionCheckAsync shifts every QC dimension value by two columns

The object is ordered (id, skid, pc, gauge, width, lo, ld, sq, head, ace, inspec, updby, note) but the SQL's placeholders run (:pc, :gauge, :width, :lo, :ld, :sq, :head, :ace, :inspec, :updby, :note, :id, :skid).

**Failure:** Correcting a dimensional QC check on Oracle writes pc_number = dimensionCheckNum, gauge = sheetSkidNum, width = the pc number, length_oper = the gauge, and so on down the row, then evaluates `WHERE dimension_check_num = <checkedBy string> AND sheet_skid_num = <note>` → ORA-01722, or (if checked_by happens to be numeric) it silently overwrites a different QC record with skid numbers in the measurement columns. These records gate whether a skid ships in spec.

**Reviewer correction:** The mis-binding is real and the file/line are right, but the stated failure ranking is inverted. Because both WHERE binds are misfed with strings (`checked_by` -> `dimension_check_num`, `note` -> `sheet_skid_num`), the realistic Oracle outcomes are: (a) ORA-01722 invalid number -> 500 on every dimensional-QC edit, when `CheckedBy` is a normal operator name; or (b) NULL/no-match -> 0 rows affected -> the endpoint returns 404 and the edit silently never applies (`CheckedBy` is nullable and not required by `Validate`). Oracle raises the conversion error before any row is modified, so there is no partial write. The "silently overwrites a different QC record with skid numbers in the measurement columns" scenario requires BOTH `CheckedBy` and `Note` to be numeric-convertible AND to jointly match an existing (dimension_check_num, sheet_skid_num) pair — possible but far-fetched. So the concrete 

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:7149 — Job-folder note write binds a formatted string into a NOT NULL Oracle DATE column

`AddJobFolderNoteAsync` binds `ts.ToString("yyyy-MM-dd HH:mm:ss")` — a string — into `job_efolder_notes."TIMESTAMP"`, which is `DATE NOT NULL` on Oracle; the implicit conversion uses NLS_DATE_FORMAT (default `DD-MON-RR`) and fails.

**Failure:** A user adds a note to a job's e-folder (POST /jobs/{abJobNum}/folder/notes). Oracle attempts `TO_DATE('2026-07-25 14:03:11')` under the session's NLS_DATE_FORMAT and raises `ORA-01861: literal does not match format string` (or ORA-01843), so the note is never saved and the endpoint 500s. SQLite stores dates as text, so the fixture round-trips the string perfectly and CI is green. Every other date write in the repo binds a real `DateTime` (e.g. `p.Add("when", when, DbType.DateTime)` at line 706) — this is the only string-formatted date bind in the file. Compounding it, the column identifier `timestamp` is written unquoted here in both the INSERT column list (line 7148) and the SELECT/ORDER BY (lines 7135, 7138), while the repo's own convention (line 3675-3677) is that this reserved-word column "is always quoted" because unquoted it raises ORA-00904.

**Reviewer correction:** Two details in the claim are mis-stated, neither of which changes the verdict:

1. Location/route. The code is at AbisRepository.cs:7174-7182 (INSERT at 7179-7180), not 7148-7149; the SELECT/ORDER BY are at 7166/7169 and the note count at 7151. The endpoint is POST /api/prod-folder/jobs/{abJobNum}/notes (ApiEndpoints.cs:1389), not POST /jobs/{abJobNum}/folder/notes.

2. The "unquoted timestamp column raises ORA-00904" sub-claim is probably wrong and should be dropped from the finding. TIMESTAMP is a non-reserved keyword in Oracle (it is not in the RESERVED='Y' set of V$RESERVED_WORDS), and both uses here are unambiguous parse positions — an INSERT column list, and an alias-qualified `n.timestamp` in the SELECT/ORDER BY. The comment at AbisRepository.cs:3675-3677 asserting the column "is always quoted" because unquoted it raises ORA-00904 is an over-cautious convention, not a demonstrated

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:8542 — PM create and update fail on Oracle: `:between` reserved-word bind

The shared `PmWriteSet` fragment (line 8542) and `CreatePmAsync`'s INSERT (line 8577) bind the `daysbetween` column to a parameter named `:between`, an Oracle reserved word — ORA-01745 on parse.

**Failure:** An admin defines a new PM schedule (POST /pm) or edits an existing one (PUT /pm/{id}) in the schedule editor shipped in #276. Oracle rejects `... daysbetween = :between ...` with ORA-01745 and the write 500s, so no PM schedule can be created or edited on the plant database — and since `daysBetween` is the primary input to the auto-advance logic at line 8702, the whole scheduling feature is unusable.

**Reviewer correction:** The defect is real; three details in the claim are off.

(a) Line numbers are stale by ~31 lines. The actual locations are AbisRepository.cs:8573 (the `PmWriteSet` fragment), :8608 (the `CreatePmAsync` INSERT VALUES list), and :8626 (`UpdatePmAsync`, which interpolates `PmWriteSet`). The `PmWriteArgs` helper at :8584 and the inline anon object at :8614 also carry the `between = ...` member — a rename must touch all four sites or the positional/named binding will drift.

(b) The routes are `POST /pms` and `PUT /pms/{pmId}` (ApiEndpoints.cs:1986 and :1998), not `/pm` and `/pm/{id}`.

(c) The claim understates scope by presenting `:between` as isolated. The same reserved-word-bind class is still live elsewhere in AbisRepository.cs: `:by` at 714, 7466, 7473, 8749, 8761 — the exact name the comment at 5677 says already produced ORA-01745 on the live DB — plus `:desc` at 7466/7473, `:from` at 

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:8718 — PM completion write fails on Oracle: `:by` reserved-word bind

`CompletePmAsync` binds `completedby`/`completed_by` to a parameter named `:by` in both the pmcompletions INSERT (line 8718) and the pm UPDATE (line 8730) — an Oracle reserved word this project has already seen raise ORA-01745 live.

**Failure:** Maintenance marks a preventive-maintenance task complete (POST /pm/{pmId}/complete). Oracle raises `ORA-01745: invalid host/bind variable name` on the pmcompletions INSERT, the transaction rolls back, and the PM is never recorded as completed and never auto-advanced to its next due date. The PM due board — the whole point of the PM subsystem (#273-#276) — can never be worked down on the plant database.

**Reviewer correction:** The defect is real but the citations are wrong and the scope is under-reported. (1) Line numbers: the pmcompletions INSERT is at AbisRepository.cs:8749 and the `UPDATE pm ... completed_by = :by` is at 8761 — line 8718 is part of the XML doc comment, not SQL. (2) The corroborating in-repo evidence is at AbisRepository.cs:5677-5678 (UpsertEdiPartnerAsync), not 5646-5647 (which is GetEdiPartnerAsync and says nothing about reserved words). (3) The same :by defect is NOT confined to the PM path: AbisRepository.cs:714 (coil_track_qa QA status-change INSERT, which additionally binds the reserved `:when`) and AbisRepository.cs:7466/7473 (abis_plc_fault_code upsert, which binds BOTH `:desc` and `:by` — two separately documented ORA-01745 offenders) will fail on Oracle for the identical reason. Any fix should rename the bind (e.g. to :cby/:updby) across all four sites, not just the two PM statemen

#### [HIGH] api/src/ABIS.Api/Data/AbisRepository.cs:9049 — Every truck-appointment write is mis-bound on Oracle (duplicate :now and id-first parameter order)

CreateTruckAppointmentAsync (:now twice, 19 placeholders vs 18 params), CheckInTruckAsync (line 9084: :now twice plus `id` bound first while :id is last), StampTruckAsync/CheckOutTruckAsync (lines 9116+9122: :now twice, 3 placeholders vs 2 params), UpdateTruckAppointmentAsync (line 9070: `id` first in the object, :id last in the SQL) and SetTruckStatusAsync (line 9132: same) all violate positional binding.

**Failure:** On Oracle: creating an appointment, a driver checking in at the self-service kiosk, gate check-out, and any status change all raise ORA-01008 (unbound variable) or update using a DateTime as the appointment_id. The truck-scheduling board and the driver kiosk are entirely non-functional against the plant database, while the SQLite e2e suite is green.

**Reviewer correction:** The claim is real but overstated in three specifics, and its file anchor is wrong.

1. Wrong anchor: line 9049 is `GetTruckAppointmentsAsync` (a read, and it is fine). `CreateTruckAppointmentAsync` is at 9068-9089.

2. The CREATE case is NOT confirmed. If ODP.NET/OCI collapses a repeated bind name into one bind position (duplicates are flagged as duplicates by the SQL parser), the 19 placeholder occurrences reduce to 18 distinct binds whose order — id, direction, carrierId, carrierName, dock, startv, endv, refType, refId, driver, phone, tractor, trailer, seal, quantity, notes, now, updby — matches the parameter object at 9082-9085 exactly, and the insert is correct. Only if each occurrence consumes a position does `:updby` go unbound (ORA-01008). This same duplicate-`:now` pattern already exists outside the truck code (`AbisRepository.cs:6949` binds `:now` twice and `:net` twice; `:4517-

#### [HIGH] api/src/ABIS.Api/Data/AbisSchema.cs:513 — Startup sequence self-heal misses PROD_ITEM_NUM_SEQ, BILL_OF_LADING_SEQ and SHEET_PACKAGING_TICKET_SEQ

`SequenceBackedTables` claims to hold "one row per NextIdAsync call site in AbisRepository" but omits `production_sheet_item`/`prod_item_num`, the shipment `bill_of_lading` second sequence, and the packing-ticket sequence — so those three are never re-synced after a Data Pump refresh leaves them behind their table max.

**Failure:** After the documented weekly prod→.230 Data Pump refresh (which left 13 of 18 sequences behind on 2026-07-24), the app restarts and `ResyncSequencesAsync` advances 21 sequences but skips PROD_ITEM_NUM_SEQ. The next DAS skid save or warehouse-skid create hits `NextIdAsync(conn, tx, "production_sheet_item", "prod_item_num", ct)` (AbisRepository.cs:5091), draws an id that already exists, and the INSERT fails with ORA-00001 against XPKPRODUCTION_SHEET_ITEM — every finished-sheet write on the plant floor is blocked, and the self-heal that exists precisely to prevent this reports "all id sequences already ahead of their tables". BILL_OF_LADING_SEQ (AbisRepository.cs:4463) is worse in kind: BILL_OF_LADING has NO unique constraint (only PACKING_LIST is the PK), so a drifted sequence silently issues DUPLICATE bill-of-lading numbers on shipping documents sent to trading partners instead of erroring.

**Reviewer correction:** The defect is real but two details in the report are wrong, and the obvious fix does not work:

(a) Wrong call site / wrong trigger. `NextIdAsync(conn, tx, "production_sheet_item", "prod_item_num", ct)` is at AbisRepository.cs:5069, not 5091 (5091 is the pieces-reconciliation warning). And the DAS skid save does NOT mint prod_item_num — the only two INSERTs into production_sheet_item are line 5072 (warehouse-skid create, the sequence mint) and line 1036 (ReturnScrapSkidAsync, which copies existing ids back from scraped_production_sheet_item). So the concrete blast radius is POST /warehouse/skids and PUT /warehouse/skids/{n}, not the plant-floor DAS kiosk.

(b) Adding tuples to `SequenceBackedTables` will not fix two of the three, and one of them would make things worse. `SequenceFor(table, idColumn)` (Database.cs:164) resolves the sequence by TABLE, honouring the `Database:Sequences` ove


## UNVERIFIED — neither confirmed nor cleared

The reviewers ran out of budget before reaching these. They are NOT safe; they are unexamined.

| Sev | Site | Claim |
|---|---|---|
| critical | `AbisRepository.cs:3092` | Coil-ownership transfer has no compare-and-swap on the source coil's terminal status — a repeat/concurrent POS |
| critical | `AbisRepository.cs:6399` | 856 ASN skid query fans out over sheet_skid_detail, multiplying shipment net/gross weight and pallet count |
| critical | `ApiEndpoints.cs:190` | POST /auth/login issues a full 8-hour bearer with NO password whenever the account has no credential row — the |
| critical | `ApiEndpoints.cs:3993` | RBAC gate fails OPEN when the bearer token carries no preferred_username/name claim, and the X-User-Login head |
| critical | `Program.cs:390` | A bad-quality OPC read fabricates a "value changed" timestamp, so Changed-mode /run-state reports a stopped li |
| high | `das-console.ts:640` | /counters and /stacker are polled with no per-line tag overrides, so every line's DAS console shows BL110's pr |
| high | `das-console.ts:961` | Auto-downtime banner re-renders its own innerHTML every second, destroying the reason dropdown and note box |
| high | `reporting.ts:294` | Failed report load leaves the previous report's rows under the new report's headers (and exports them) |
| high | `truck-scheduling.ts:285` | Truck appointment list treats an HTTP error (e.g. RBAC 403) as an empty schedule |
| high | `AbisRepository.cs:821` | UpdateCustomerAsync binds :ts and :id in the wrong order |
| high | `AbisRepository.cs:1006` | DeleteScrapSkidAsync deletes only the scrap_skid header, orphaning its detail, return-scrap credits and the wh |
| high | `AbisRepository.cs:1828` | Production reports label SUM(process_end_wt) as "Processed wt" - it is the weight LEFT on the coil, not the we |
| high | `AbisRepository.cs:3509` | Effective-privilege lookup unions grants across every security_user row sharing a login, while sign-in resolve |
| high | `AbisRepository.cs:4101` | UpdatePartAsync binds item_status 48 positions away from its placeholder |
| high | `AbisRepository.cs:5039` | Warehouse coil identity read drops legacy's `cash_date IS NOT NULL` filter, bypassing the cert/cash-date refus |
| high | `AbisRepository.cs:5151` | DeleteWarehouseSkidAsync has no packing-list guard, so a skid already on a shipment can be deleted out from un |
| high | `AbisRepository.cs:5272` | Warehouse MODIFY mints a shell coil with none of legacy's cert-label / cash-date guards |
| high | `AbisRepository.cs:5655` | UpsertEdiPartnerAsync UPDATE branch binds customer_id and transaction_set to the first two value columns |
| high | `AbisRepository.cs:6927` | MintBolCoilsAsync reads its work list outside the transaction and re-mints without an IS NULL guard, so concur |
| high | `AbisRepository.cs:7435` | UpsertPlcFaultCodeAsync uses the Oracle reserved words :desc and :by as bind names AND mis-orders the UPDATE |
| high | `AbisRepository.cs:8718` | PM-completion and coil-QA writes use the reserved word :by as a bind name (ORA-01745) |
| high | `ApiEndpoints.cs:3813` | If-Match optimistic concurrency is implemented as check-then-act across two separate connections, so the lost  |
| high | `Program.cs:62` | A misspelled Edge:Opc:RunStateMode silently falls back to Equals, making a stroke-counter tag read "stopped" f |
| high | `Program.cs:161` | /reading serves an unbounded-stale weight with no age or liveness guard, so a dead scale silently stamps the p |
| medium | `coil-eval.ts:118` | A dimension check with NULL in_spec is rendered as a red "out" (out-of-spec) verdict |
| medium | `das-console.ts:517` | DAS "Pull weight" writes the scale reading into the skid NET weight ignoring the stable flag, unit and timesta |
| medium | `shipping.ts:262` | "Save dispatch" silently truncates date_sent / shipment_actualed_date_time to midnight |
| medium | `truck-scheduling.ts:270` | Carrier lookup failure is swallowed, so a CSV import writes every appointment with a null carrier link |
| medium | `warehouse.ts:153` | Warehouse-in form never sets skid_from_if_whed, the column that DEFINES a warehoused skid in legacy |
| medium | `AbisRepository.cs:2406` | Scheduled-job admin writes bind the job id first while :id is last in both statements |
| medium | `AbisRepository.cs:3584` | Recovery-report customer upsert fails on Oracle: `:all` reserved-word bind |
| medium | `AbisRepository.cs:5123` | Warehouse create never allocates the customer package number (f_insert_sheet_skid_package is not ported) |
| medium | `AbisRepository.cs:6485` | EDI duplicate guards are check-then-act with no unique constraint, and the guard query itself uses QuerySingle |
| medium | `AbisRepository.cs:6732` | EDI-type, customer-EDI and 861-flag updates all bind their WHERE-clause keys into the SET columns |
| medium | `AbisRepository.cs:7435` | PLC fault-code upsert fails on Oracle: `:desc` and `:by` reserved-word binds |
| medium | `AbisRepository.cs:9024` | Truck-appointment date-range filter 500s on Oracle: `:from`/`:to` reserved-word binds |
| medium | `HandheldBarcode.cs:70` | Handheld scan applies the 000000 → "NO BARCODE" substitution before the already-minted lookup; legacy does not |
| medium | `ApiEndpoints.cs:1396` | Job-folder note authorship is taken from the request body in preference to the authenticated principal |
| medium | `ApiKeyAuthentication.cs:48` | ApiKeys:Enabled=false authenticates every anonymous request AND disables all RBAC feature gates |
| medium | `JwtAuth.cs:94` | OIDC configuration with an Authority but no Audience turns audience validation OFF |
| medium | `Program.cs:132` | A tag chosen through the /opc/browse picker is never added to the polled set, so PLC auto-downtime is silently |
| medium | `ClassicDaTagSource.cs:90` | /opc/browse holds the Classic-DA COM lock synchronously and ignores its CancellationToken, stalling the tag po |
| low | `main.ts:42` | Unescaped coil fields rendered into innerHTML on /ui/typed.html (stored XSS) |
| low | `ApiEndpoints.cs:145` | POST /auth/login distinguishes 'Unknown user' from 'Invalid credentials', giving an anonymous login-enumeratio |
| low | `ApiEndpoints.cs:1555` | PUT/DELETE /lookups/plc-fault-codes carry no feature gate and the 'Lookups' tag is not in the documented ungat |
