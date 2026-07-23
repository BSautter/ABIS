# ABIS — Remaining Work Backlog

> Living checklist of what's **still open**, generated 2026-07-11 from the parity re-audit (7 surfaces
> re-verified against the current code) + the correctness bug sweep. Work it top-to-bottom.
> The historical full-detail gap report is `PARITY_AUDIT.md` (2026-07-07, mostly closed since).
> What's **done** lives in git history + GitHub releases (current: **v0.5.5**).

**Legend:** `[ ]` open · `[~]` partial · severity **C**ritical / **H**igh / **M**edium / **L**ow.

## Version roadmap to 1.0.0 (agreed 2026-07-11)
The target for **1.0.0 is full legacy-ABIS parity, cutover-ready**. Honest distance from v0.4.x: the platform
is production-mature (auth/RBAC, ~37 pages, all domain CRUD, the 4 subsystems, doc/print engine, live edge/OPC
auto-downtime, native deploy + AD login + server console) — ~75–80% to parity **by breadth**, but the
remaining ~20–25% holds the two heaviest programs (EDI engine + the live-DAS spine), so it's more work than the
percentage suggests.

| Milestone | Definition |
|-----------|------------|
| **0.5.0** | **EDI engine — generation + 997** complete, **never transmits** (§A). ✅ **CLOSED at v0.5.5 (2026-07-22)**; two items deferred data-blocked (863 gen, inbound-856 ingest — see §A). |
| **0.6 – 0.8** | Buildable feature-gap batches (§C: commercial → coils/receiving → quality) + the **live-DAS workflow spine** (§B), in increments. ← *current: starting with C2 packing-list line items* |
| **0.9.x** | Feature-complete parity + a hardening / verification pass |
| **1.0.0** | Cutover-ready: everything built, so the deferred **EDI transmit** + **data-source cutover** become an operational go-live decision, not a code gap |

## Suggested next 5 (highest value, buildable now)
1. ~~**Packing-list line items** (C2)~~ — ✅ **DONE** (#217 sheet, #219 scrap + generalized API, #220 reject-coil; warehouse deferred). Shipments now carry line items and feed the 856.
2. **863 mechanical test-result WRITE** (C5) — the test-result list cannot populate without it.
3. **Coil-ownership transfer mint semantics** (C3) — today it mutates `customer_id` in place (wrong audit trail).
4. **Dimension-check tolerance validation** (C5) — the actual QC gate; today `in_spec` = whatever the client sends.
5. **BOL / combi-form / packing-ticket printing** (C2) — the shipping-document engine (nothing physical comes out).

---

## A. EDI engine → 0.5.0 ✅ CLOSED at v0.5.5 (2026-07-22) — BUILT fully + integrated, NEVER transmits
Directive 2026-07-11: build ALL of EDI generation/ingestion/ack, stopping at an explicit no-op transmit seam.
The VAN SFTP stays the single legacy owner (`GXS.ksh`). Design in `docs/EDI_ENGINE.md`; see `[[abis-edi-engine-build]]`,
`[[abis-no-live-firing-guardrail]]`, `[[abis-230-cron-inventory]]`. **Foundation shipped: #183 (X12Writer +
`IEdiTransport`→`NoOpEdiTransport`, no SFTP anywhere), #184 (email → cmattinson override).**
- [x] **C** EDI outbound generation (861 / 870 / 846 / 856) — **DONE + tested + verified live on codi-ABIS**.
  All 17 live-partner profiles on the `abis_edi_partner` backbone map to a built variant: **861** Novelis(1153/1459/2582)/
  Commonwealth(1980)/Constellium(2776)/Arconic(2784); **870** Novelis(1153/1459/2950)/Aleris(1980)/Constellium(2776,
  per-coil); **856** Novelis/Constellium/Arconic; **846** Cleveland-Cliffs(3061). Golden byte-tests where a production
  `.edi` existed (856/861/870). Admin UI for the profiles shipped.
- [x] **H** 997 functional-ack matching + aging bell alert (#206/#207) — `Edi997Parser` + `/edi/997/waiting` + `/edi/997/ingest`.

**Deferred out of 0.5 (accepted at close — NOT built; carried on the backlog):**
- [ ] **DEFERRED (data-blocked)** **863 (test-result report) generation** — no production golden; the `.863` file may not
  even be swept/transmitted by GXS. Also depends on the 863 test-result **WRITE** path (§C5) as its data source first.
  Revisit once §C5 exists AND the plant supplies a real 863 golden / confirms it's transmitted.
- [ ] **DEFERRED (data-blocked)** **Inbound 856 (ASN) ingestion** (parse → `inbound_shipment` / `inbound_coil` / status)
  — the only inbound sample is a 2009 **test 850**; no real inbound business doc to validate against. Needs a real golden.
- [ ] **DEFERRED (by policy)** EDI VAN transport (GXS / Inovis SFTP) + postpro — legacy-owned, do NOT build (transmit seam stays no-op).
- [ ] **DEFERRED (operational, → 1.0)** Data-source cutover (codi-ABIS reads the .230 sandbox, not live prod .9) — enables the EDI-stall alert to be meaningful.

## B. Architectural program — the live-DAS workflow spine
The edge read path is live (run-state + piece-count → auto-downtime); the DAS *workflow core* is absent. Buildable in pieces.
- [ ] **C** `LINE_CURRENT_STATUS` live line board (job/coil/shift, 19 skid locations, 2 stacker skids)
- [ ] **C** Current-coil ↔ job/shift binding + `SHIFT_COIL` / `SHIFT_PROCESS_STATUS` ledger write (cross-shift carry)
- [ ] **C** Operation Panel workflow (new/end coil, end shift, change job)
- [ ] **C** Live PLC counters (good/reject/stroke/feed-length) posted as coil deltas
- [ ] **C** Coil barcode scan-to-load + actual-weight (`ABCO_COIL_NET_WT`) update
- [ ] **H** Live shift efficiency % + coil yield % / finish-% (console, 5s cadence)
- [ ] **H** End-coil recap (ending status + closing weight)
- [ ] **H** Drop/reverse a wrongly-loaded coil (+`ERROR_EVT`); change-job-mid-coil (split & save remaining wt)
- [ ] **H** Per-line job queue / `LINE_PRIORITY` sequencing
- [ ] **H** Line auto-status controls + `noauto` write (lockout); fault/health lamps (DB / OPC `_ErrorCode` / PLC `activefault`)
- [ ] **H** Stacker dual-station automation (`SHEET_SKID_STACKER_1/2`)
- [ ] **H/M** Shift lifecycle automation (auto new/end + grace, `DT_TOTAL` rollup, board reset)
- [ ] **M** Stacker physical board (11 shape displays + ~16 conveyor cells + live stack tracking)
- [ ] **M** Supervisor/role PIN gating (exit / override / drop-coil / maintenance)
- [ ] **M** Serial scale zero command + scrap-scale/gauge separation
- [ ] **M** Live job sheet / e-folder (sketch image, shape-specific tolerances, coil totals, partial-skid usage)

## C. Buildable feature gaps (no blocker)

### C1. Commercial — order entry / parts / quoting / customers / accounting
- [ ] **C** Quote pricing/cost model (CirclePro $/lb + job cost + ROS; SheetPro rectangular) — quotation emits yield-% only, "not a quote"
- [ ] **C** Quote editor (`PUT /sales/quotes` + tabbed spec/pricing/inventory/shipment body) + save/reload + print + email
- [x] **H** Order edit-in-UI — done (#249): order-detail Edit toggle wires the existing `PUT /orders/{o}` + item PUT (editable header + per-line part/alloy/sheet/gauge/qty; full-replace-safe via spread)
- [ ] **H** Assign customer coils to an order (`/orders/{id}/coils`, dup-org warning)
- [ ] **H** Part revisions (version + re-point open items); routing sequences per part
- [ ] **M** Part delete / copy; order copy/duplicate; obsolete-in-use guard; end-user change cascade; order-entry part picker
- [ ] **H/M** Sector consistency validation; edge-trim tolerance gate + override + `f_add_system_log_tran` audit
- [ ] **M** Accounting scrap-type summary; print coil-cert label on order close; customer delete

### C2. Logistics / shipping
- [~] **C** Packing-list line items — ✅ `SHEET` (#217) + `SCRAP` (#219) + `REJECT_COIL` (#220) built (add/list/remove on the shipment, feeding the 856); only `WH_PACKING_ITEM` (9 live rows) deferred
- [ ] **C** BOL / combi-form / packing-ticket printing (the `rpabco` document engine)
- [ ] **H** Sketch image storage (`sketch_view` LONG RAW) + display + job/part linkage + DAS/e-folder render
- [ ] **H** Die → shape mapping + `line_die_4sheet_type` (scheduling can't tell which line/die makes which shape); die label/report print
- [ ] **M** Shipment header EDI-trigger fields (`edi_req`/`triggered`/`file_id_856`/`desadv` — prereq for the 856)
- [ ] **M** Manual EDI send/resend from UI; view archived EDI payload; X12 map maintenance
- [ ] **L** Shipment status-change history (`SHIPMENT_TRACK`); carrier DUNS/street/zip/country fields

### C3. Coils / receiving
- [ ] **C** Warehouse skid CRUD + status-20 warehouse-coil mint (+ process_coil/sheet_skid rows, weight recon, package-num)
- [x] **H** Coil-ownership transfer mint semantics — done (#224): mints a NEW `coil_abc_num` (status 2, from-cust set) + original → status 13; cert carries the new id
- [x] **H** Bulk "Change status → Ready for transfer" (status 12) — done (#240 `POST /coils/ready-for-transfer` with eligibility guards; #241 picker `readyOnly` filter + coil-ownership mark-ready UI)
- [~] **H** Scrap-skid + sheet-skid guarded DELETE done (#243). **Return-scrap done** (#XXX): POST /scrap-skids/{n}/return faithfully ports the live F_CONVERT_BACK_TO_SHEET proc — copies the scrapped mirror rows (scraped_sheet_skid/production_sheet_item/process_partial_skid/detail) back to the live tables, deletes the mirrors + scrap_skid(+detail) + credits back the linked return_scrap_item rows. Still TODO: sheet-skid modify + weight/piece reconciliation.
- [~] **H** Guarded coil delete — done (DELETE /coils/{n}, refuses coils applied to a job or done/shipped/transferred); change-coil-customer-on-BOL cascade still TODO
- [x] **H** Mint carries full coil attributes — already done in #224: the ownership-transfer mint does a `SELECT *` schema read and copies every coil column (cash_date / part_num / material_num / mid_num / damaged_code / …) to the minted coil
- [x] **H** Coil-quality capture + flaw mapping (#246 GET/PUT /coils/{n}/quality + POST/DELETE .../quality/flaws) + a **Coil quality** capture page (#247). Inbound status-on-receipt is already handled: MintBolCoilsAsync sets `coil.date_received` at receipt and status 11 (QA-hold) when `receiving_bol_coil.damaged_fault=1` (the damage code lives on receiving_bol_coil, not the coil). Remaining tail: QR/barcode capture feeding the flaw map (needs the handheld/barcode integration).
- [~] **M/L** Import-from-BOL / show-archived-BOL browsers; multi-condition coil search (search term over org/lot/mid/notes + temper filter DONE on GET /coils + coil-inventory UI); manual new-coil + live-scale weigh-in — remaining: BOL browsers, gauge/width ranges, live-scale

### C4. Handheld scanner (RF coil-receiving)
- [ ] **C** `INBOUND_COIL_STATUS` model + barcode→ABC lookup + mint-decision (already-minted→reprint vs unminted→mint)
- [ ] **C** Native Zebra ZPL/CPCL over TCP :6101 + printer routing by device IP + connectivity check/offline page
- [ ] **H** Single-scan ABC mint (per-scan `SEQ.NEXTVAL`; today mint is desktop batch over `receiving_bol_coil`)
- [ ] **H** Lookup by scanned customer coil (`coil_org_num`); QR capture → `BARCODE_STRING` upsert
- [ ] **M** S-header strip+validate; coil-defect email notification
- *(Done: scan→verify→label handheld page + HTML coil label.)*

### C5. Quality
- [x] **C** 863 mechanical test-result WRITE (`PST_TEST_RESULT`) — done (#225 API + #226 Quality "Test results" page); the read-only list can finally populate
- [x] **C** Coil QA-hold workflow (status 11) + `COIL_TRACK_QA` audit + search/browse console — done (#227: qa-hold/qa-release/qa-history endpoints + Quality "QA hold" console)
- [x] **C** Dimension-check tolerance validation vs part-shape spec (nominal ± tol) — the real QC gate. **DONE via WinSPC.** WinSPC (the plant SPC system of record) owns the measured value + LSL/target/USL + pass/fail per characteristic, tied to ABIS by the "Job #"/"Coil #" tag. Phase 1: #229 read-only connector + #230 Dimensional QC page + #233 trend chart. Phase 2: #234 — `POST /coil-eval/skids/{n}/dimension-checks` validates the submitted measurements against WinSPC's authoritative LSL/USL and sets `in_spec` from that (falls back to the supplied flag when WinSPC has no data/disabled). Validated against live data (job 124346). The legacy `d_skid_dim_check` rule stays un-reconstructable but is moot. Live-wiring: abis_ro read-only SQL login on RSEDAM-PC (192.168.10.143,1433) + WinSpc:Enabled on ABIS.
- [ ] **H** Instron `.ASC` test-file import & parse (up to 9 samples)
- [ ] **H** Recovery report suite (remaining ~6 templates) + customer-report SETUP write (`recovery_report_customer`/`cust_scrap_type_needed`)
- [ ] **M** Recovery depth (add/remove coil-job, autoparts filter, pull-from-DAS-vs-office, email/print/export)
- [x] **M** Dimension-check edit/delete; job-level dim-QC green/red board; good-material in-spec rollup; PC# auto-increment — done (#236 edit/delete + PC# auto-increment; #237 QC board page + GET /coil-eval/jobs/{n}/qc-board with good/out-of-spec roll-ups + WinSPC verdict)
- [ ] **M** QA coil photos; QA email notification + "make scrap" action

### C6. Platform / admin / reports
- [ ] **C** Scheduler EXECUTION engine (registry `/admin/jobs` is inert) + cron auto-import off the DB host
- [ ] **M** Preventive-Maintenance (PM) scheduling subsystem
- [ ] **M** Maintenance parts/spares inventory; equipment hierarchy cascade + More-Details; log record-nav + maintenance reports
- [ ] **M** Uptime reports (uptime, uptime-per-line) + downtime pivots (job/day/month/year/part/shift + dt-vs-production ratio)
- [ ] **M** Native Excel export (reporting is CSV-only)
- [~] **C/H** Feature-gate the write tags still auth-only. Done for every tag that maps 1:1 to a nav-gated feature (safe — the user who can reach the page already holds it; kiosks/edge use the API key and bypass): **Jobs**→Production Control, **Shipments**/**Stacker**→Warehouse, **CoilOwnership**→Inventory(Coil), **TestResults**/**Recovery**→Quality Control, **ProdFolder**→Production Control, **Downtime**→Downtime report (added to `FeatureByTag`). Still **deferred:** Dies / Sketches / Sales / Accounting / Trucks / Carriers / DAS / ScanLog / OpcLog — their nav pages have NO feature gate, so there's no authoritative feature name to gate the API on without risking a lockout; needs live `security_application` verification.
- [ ] **M** OPC-log collector + item-selection config (viewer is read-only; edge is the producer); source/host/device tree
- [ ] **M** Step-up re-auth popup; in-DB job control (DBMS_SCHEDULER enable/disable/run-now)

## D. Bug / robustness leftovers (from the sweep — verified, low severity)
- [ ] **M** Invoice-save duplicate: return **409** not a 500 (wrap check+insert in a txn / catch PK ORA-00001) — `CreateInvoiceAsync`
- [ ] **L** `If-Match` optimistic concurrency: push the version into the UPDATE `WHERE` (true compare-and-swap; today check-then-act) — `WithIfMatch`
- [ ] **L** Invoice **tare** bucket sums voided skids while `SkidCount` excludes them (`<> 6`) — display inconsistency — `GetInvoiceComputationAsync`
- [ ] **L** Stacker board: tighten `job_status NOT IN (0,3)` → `IN (1,2,4)` (matches its own comment; robust to NULL/new codes) — latent, 0 impact now
- [ ] **L** On-hand-coil (`coil_status NOT IN …`) + skid-count (`skid_sheet_status <> 6`): add `IS NULL` guards — nullable columns, 0 NULL rows today
- [ ] **L** `pollPieceCount`: clear `pieceCurrent` on a transient edge outage so a stale count isn't shown — `das-console.ts`
- [ ] **L** Committed `wwwroot/…/generated/abis-client.js` drifts vs a fresh gen — regen periodically or CI-enforce

## E. Config / turn-on / deploy (user-gated, not code)
- [ ] Redeploy codi-ABIS to **v0.4.18** (dashboard piece count + the client bug fixes)
- [ ] Wire BL110 piece-count tag per DAS station via the 🔎 picker (`stacker110.station1/2_stack_counter`)
- [ ] Enable `Notifications:EdiStall` **after** the data-source cutover (else false alarms on the frozen .230 ledger)
- [ ] Server-console restart button — decide on/off (polkit rule per `docs/SERVER_CONSOLE.md`)
- [ ] BL84 stacker piece-count — **parked ~6 months** (stacker out of service)
