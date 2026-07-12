# ABIS — Remaining Work Backlog

> Living checklist of what's **still open**, generated 2026-07-11 from the parity re-audit (7 surfaces
> re-verified against the current code) + the correctness bug sweep. Work it top-to-bottom.
> The historical full-detail gap report is `PARITY_AUDIT.md` (2026-07-07, mostly closed since).
> What's **done** lives in git history + GitHub releases (current: **v0.4.18**).

**Legend:** `[ ]` open · `[~]` partial · severity **C**ritical / **H**igh / **M**edium / **L**ow.

## Version roadmap to 1.0.0 (agreed 2026-07-11)
The target for **1.0.0 is full legacy-ABIS parity, cutover-ready**. Honest distance from v0.4.x: the platform
is production-mature (auth/RBAC, ~37 pages, all domain CRUD, the 4 subsystems, doc/print engine, live edge/OPC
auto-downtime, native deploy + AD login + server console) — ~75–80% to parity **by breadth**, but the
remaining ~20–25% holds the two heaviest programs (EDI engine + the live-DAS spine), so it's more work than the
percentage suggests.

| Milestone | Definition |
|-----------|------------|
| **0.5.0** | **EDI engine complete** — generation + inbound + 997, **never transmits** (§A). ← *pushing for this now* |
| **0.6 – 0.8** | Buildable feature-gap batches (§C: commercial → coils/receiving → quality) + the **live-DAS workflow spine** (§B), in increments |
| **0.9.x** | Feature-complete parity + a hardening / verification pass |
| **1.0.0** | Cutover-ready: everything built, so the deferred **EDI transmit** + **data-source cutover** become an operational go-live decision, not a code gap |

## Suggested next 5 (highest value, buildable now)
1. **Packing-list line items** (C2) — unblocks a real packing list + the 856 trigger.
2. **863 mechanical test-result WRITE** (C5) — the test-result list cannot populate without it.
3. **Coil-ownership transfer mint semantics** (C3) — today it mutates `customer_id` in place (wrong audit trail).
4. **Dimension-check tolerance validation** (C5) — the actual QC gate; today `in_spec` = whatever the client sends.
5. **BOL / combi-form / packing-ticket printing** (C2) — the shipping-document engine (nothing physical comes out).

---

## A. EDI engine → 0.5.0 (BUILD fully + integrated, but NEVER transmit)
Directive 2026-07-11: build ALL of EDI generation/ingestion/ack, stopping at an explicit no-op transmit seam.
The VAN SFTP stays the single legacy owner (`GXS.ksh`) — the ONLY items still deferred-by-policy are the
transport + the data-source cutover. Design in `docs/EDI_ENGINE.md`; see `[[abis-edi-engine-build]]`,
`[[abis-no-live-firing-guardrail]]`, `[[abis-230-cron-inventory]]`. **Foundation shipped: #183 (X12Writer +
`IEdiTransport`→`NoOpEdiTransport`, no SFTP anywhere), #184 (email → cmattinson override).**
- [~] **C** EDI outbound generation (861 / 870 / 846 / 856 / 863) — foundation + **861 DONE** (generate + payload store + view endpoint); **870 = next**
- [ ] **H** Inbound EDI ingestion (856 ASN parse → `inbound_shipment` / `inbound_coil` / status)
- [ ] **H** 997 functional-ack matching + aging alert (>2h no FA) — routes through `IEmailSender` (override-safe)
- [ ] **DEFERRED** EDI VAN transport (GXS / Inovis SFTP) + postpro — legacy-owned, do NOT build (transmit seam stays no-op)
- [ ] **DEFERRED** Data-source cutover (codi-ABIS reads the .230 sandbox, not live prod .9) — enables EDI-stall alert to be meaningful

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
- [ ] **H** Order edit-in-UI — wire `order-entry.ts` to the existing `PUT /orders/{o}` + item endpoints (create+view only today)
- [ ] **H** Assign customer coils to an order (`/orders/{id}/coils`, dup-org warning)
- [ ] **H** Part revisions (version + re-point open items); routing sequences per part
- [ ] **M** Part delete / copy; order copy/duplicate; obsolete-in-use guard; end-user change cascade; order-entry part picker
- [ ] **H/M** Sector consistency validation; edge-trim tolerance gate + override + `f_add_system_log_tran` audit
- [ ] **M** Accounting scrap-type summary; print coil-cert label on order close; customer delete

### C2. Logistics / shipping
- [ ] **C** Packing-list line items (`SHEET`/`SCRAP`/`REJECT_COIL`/`WH_PACKING_ITEM`) — shipment is header-only
- [ ] **C** BOL / combi-form / packing-ticket printing (the `rpabco` document engine)
- [ ] **H** Sketch image storage (`sketch_view` LONG RAW) + display + job/part linkage + DAS/e-folder render
- [ ] **H** Die → shape mapping + `line_die_4sheet_type` (scheduling can't tell which line/die makes which shape); die label/report print
- [ ] **M** Shipment header EDI-trigger fields (`edi_req`/`triggered`/`file_id_856`/`desadv` — prereq for the 856)
- [ ] **M** Manual EDI send/resend from UI; view archived EDI payload; X12 map maintenance
- [ ] **L** Shipment status-change history (`SHIPMENT_TRACK`); carrier DUNS/street/zip/country fields

### C3. Coils / receiving
- [ ] **C** Warehouse skid CRUD + status-20 warehouse-coil mint (+ process_coil/sheet_skid rows, weight recon, package-num)
- [ ] **H** Coil-ownership transfer mint semantics — mint a NEW `coil_abc_num` (status 2) + set original → status 13 (today: in-place `customer_id` UPDATE)
- [ ] **H** Bulk "Change status → Ready for transfer" (status 12) — precondition of the transfer picker
- [ ] **H** Scrap-skid lifecycle (delete / return-scrap / credit); sheet-skid modify/delete (weight+piece recon)
- [ ] **H** Guarded coil delete; change-coil-customer-on-BOL cascade
- [ ] **H** Mint carries full coil attributes (cash_date / part_num / PO / material_num / coil_location / mid_num / damaged_code / …)
- [ ] **H** QR / coil-quality capture + flaw mapping (`coil_quality*`); inbound status-on-receipt (received_time, damage codes)
- [ ] **M/L** Import-from-BOL / show-archived-BOL browsers; multi-condition coil search; manual new-coil + live-scale weigh-in

### C4. Handheld scanner (RF coil-receiving)
- [ ] **C** `INBOUND_COIL_STATUS` model + barcode→ABC lookup + mint-decision (already-minted→reprint vs unminted→mint)
- [ ] **C** Native Zebra ZPL/CPCL over TCP :6101 + printer routing by device IP + connectivity check/offline page
- [ ] **H** Single-scan ABC mint (per-scan `SEQ.NEXTVAL`; today mint is desktop batch over `receiving_bol_coil`)
- [ ] **H** Lookup by scanned customer coil (`coil_org_num`); QR capture → `BARCODE_STRING` upsert
- [ ] **M** S-header strip+validate; coil-defect email notification
- *(Done: scan→verify→label handheld page + HTML coil label.)*

### C5. Quality
- [ ] **C** 863 mechanical test-result WRITE (`PST_TEST_RESULT`) — `/test-results` is read-only; list can't populate
- [ ] **C** Coil QA-hold workflow (status 11) + `COIL_TRACK_QA` audit + search/browse console
- [ ] **C** Dimension-check tolerance validation vs part-shape spec (nominal ± tol, 15% sanity) — the real QC gate; `in_spec` is client-supplied today
- [ ] **H** Instron `.ASC` test-file import & parse (up to 9 samples)
- [ ] **H** Recovery report suite (remaining ~6 templates) + customer-report SETUP write (`recovery_report_customer`/`cust_scrap_type_needed`)
- [ ] **M** Recovery depth (add/remove coil-job, autoparts filter, pull-from-DAS-vs-office, email/print/export)
- [ ] **M** Dimension-check edit/delete; job-level dim-QC green/red board; good-material in-spec rollup; PC# auto-increment
- [ ] **M** QA coil photos; QA email notification + "make scrap" action

### C6. Platform / admin / reports
- [ ] **C** Scheduler EXECUTION engine (registry `/admin/jobs` is inert) + cron auto-import off the DB host
- [ ] **M** Preventive-Maintenance (PM) scheduling subsystem
- [ ] **M** Maintenance parts/spares inventory; equipment hierarchy cascade + More-Details; log record-nav + maintenance reports
- [ ] **M** Uptime reports (uptime, uptime-per-line) + downtime pivots (job/day/month/year/part/shift + dt-vs-production ratio)
- [ ] **M** Native Excel export (reporting is CSV-only)
- [ ] **C/H** Feature-gate the last ~12 write tags still auth-only (Shipments / Dies / Sketches / Accounting / Sales / CoilOwnership / DAS / Stacker / Recovery / TestResults / Downtime)
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
