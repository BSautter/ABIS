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
| **0.6.x** | Buildable feature-gap batches (§C: commercial → coils/receiving → quality → reporting → maintenance). ✅ **CLOSED at v0.6.16 (2026-07-24)** — §C is cleared; what remained was the DAS spine, blocked items, and small tails. |
| **0.7.0** | **The live-DAS workflow core** (§B) — ✅ **CUT 2026-07-24**. A line can be scheduled, staffed, run, corrected and closed out entirely in ABIS: line board, Operation Panel, coil-run ledger w/ cross-shift carry, LINE_PRIORITY queue, change-job mid-coil + reverse, live efficiency/yield on the recovered legacy formulas, end-coil recap, stale-shift monitor, PLC counters + dual-station stacker. **Read AND write paths validated on live Oracle**; the edge serves 5 typed endpoints from both OPC boxes. |
| **0.8.x** | The rest of §B (stacker physical board, scan-to-load, shift-lifecycle automation, auto-status controls) + the remaining §C tails. |
| **0.9.x** | Feature-complete parity + a hardening / verification pass |
| **1.0.0** | Cutover-ready. **NOTE (user, 2026-07-24): 1.0.0 is the STARTING POINT for alpha/beta testing** — the point where new ABIS can replace old ABIS and users first exercise it. It is not "finished"; it is "ready to begin being tested". **There is therefore NO user-feedback loop before 1.0** — correctness up to that point must come from legacy-source fidelity, live-data validation on .230 / the plant PLCs, and automated tests. |

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

> **WRITE paths validated on the LIVE non-prod Oracle (.230) 2026-07-24** (#292), after the sequence
> re-sync (see [[abis-230-sequence-drift]]) was RUN on .230: the full DAS write suite — shift
> create/start/end, coil-run start/end, change-job, reverse, and the LINE_PRIORITY queue upsert/reorder/
> delete — all execute correctly on Oracle. Two Oracle-only bugs SQLite CI couldn't show were found + fixed:
> `ERROR_EVT.ERROR_USER`/`ERROR_TYPE_ID` are NOT NULL (reverse now defaults them to `das` / `1 OPERATOR`),
> and `shift_coil` FKs (coil, job) to `process_coil` so coil-run/change-job now guard with a clean 409
> instead of ORA-02291. Real INGEAR tag ids discovered live + captured in `edge/appsettings.Plant.example.json`.
>
> **READ paths validated on the LIVE non-prod Oracle (.230) 2026-07-24** (#288). Every read added by #281–#287
> was run there read-only — the 21-branch skid unpivot, the line board, the queue, the coil-run ledger and
> the live-metric reads all execute correctly on Oracle against real data. What the live data changed:
> (a) the 7 lines are `line_num` 1–7 = **BL 24 / 36 / 60 / 78 / 108 / 110 / 84** (internal codes; the LINE
> table is the map); (b) the **19 floor skid-position columns are unused in practice** — only a stacker slot
> was occupied, so the board's skid strip is normally empty; (c) **all three "open" shifts were stale**
> (31 h, 31 h, 103 h, no `dt_total`), which is why live efficiency is now withheld for a shift left open
> (§B live-metrics item); (d) real `LINE_PRIORITY` rows carry a **NULL `priority_num`**, which Oracle sorts
> last and SQLite first — the queue's ORDER BY is now explicit. **Still unvalidated: the WRITE paths**
> (#283–#286 mutate `line_current_status` / `shift` / `shift_coil` / `line_priority`); exercising those on
> .230 needs a decision, since the deployed UI reads that same sandbox.
- [x] **C** `LINE_CURRENT_STATUS` live line board (job/coil/shift, 19 skid locations, 2 stacker skids) — done
  (#281): `GET /das/line-board` (+ `/{lineNum}`, 404 when the line has no board row) reads the one-row-per-line
  DAS table joined to `line`/`shift`/`ab_job`/`coil`, and unpivots the 21 flat skid columns
  (`sheet_skid_location_0..18` + `sheet_skid_stacker_1/2`) into an ordered `skids[]` of occupied slots resolved
  against `sheet_skid` (LEFT JOIN — a stacker position written before its skid row still reports). The DAS floor
  board now takes its Running/Idle light from the LINE (coil loaded on an open shift) instead of inferring it from
  the job list, and shows the open shift, the coil on the mandrel and the physical skid positions per line.
  **Read-only** — the Operation-Panel write path (next item) owns the mutations.
- [x] **C** Current-coil ↔ job/shift binding + `SHIFT_COIL` / `SHIFT_PROCESS_STATUS` ledger write (cross-shift carry)
  — binding done (#283); the **coil-run ledger** done (#284): `POST /das/lines/{n}/coil-run/start` opens the
  `shift_coil` run on the line's open shift (run number is per-shift `MAX+1`, begin weight/status default from the
  coil) and puts the coil on the board — idempotent per (shift, job, coil), the legacy insert guard;
  `POST /das/lines/{n}/coil-run/end` stamps end status/weight/time + `process_wt` (begin − end, floored at 0),
  rolls the weight through `process_coil` (`shift_process_status` + `current_wt`) and the coil
  (`coil_status`/`coil_status_from_line`, `net_wt_balance`/`_from_line`), drops the coil off the board, and
  **finishes the job** (`ab_job.time_date_finished` + queue → status 0) when every `process_coil` on it is spent
  (NULL `current_wt` = never run, so it keeps the job open — the legacy predicate).
  `GET /das/shifts/{n}/coil-runs` is the shift's ledger. **Cross-shift carry** both ways: a run still open when the
  shift ends is closed at the coil's current balance, and binding the next shift re-opens a fresh run for a coil
  still on the mandrel — so a coil spanning midnight splits across both shifts' production instead of landing in one.
  Console: Load/End coil-run buttons + the live ledger table.
- [~] **C** Operation Panel workflow (new/end coil, end shift, change job) — done (#283): `POST /das/lines/{n}/current-job`
  (null clears; re-sequences `LINE_PRIORITY` — the running job drops to status 2, the new one takes 1, in legacy
  order), `POST /das/lines/{n}/current-coil` (null drops; loading zeroes the process rate and sets
  `coil.coil_status_from_line = 1`), `POST /das/lines/{n}/shift/start` (409 if the shift belongs to another line),
  `POST /das/lines/{n}/shift/end` (stamps `end_time` + rolls `dt_instance` up into `dt_total` **in seconds**, then
  clears the board's shift; 409 when nothing is open) + `GET /das/lines/{n}/queue` (`LINE_PRIORITY`, running job
  first). Each mirrors the legacy `w_da_sheet` UPDATE. The DAS console gained an **Operation panel** card
  (live shift/job/coil + the actions). New/end **coil run** landed in #284 (above); still TODO here: the
  end-coil recap screen.
- [~] **C** Live PLC counters (good/reject/stroke/feed-length) posted as coil deltas — **live-display half done**
  (#291): edge `GET /counters` exposes the four running PLC counters (legacy `goodpartcnt`/`rejectpartcnt`/
  `strokecnt`/`feedlength`); the DAS console baselines them when a coil run opens and shows the delta as this
  coil run's production, on the same edge primary→fallback path + baseline pattern as the stacker piece count.
  Whole counts round; feed-length keeps decimals. Validated end-to-end against a local mock edge (deltas climb
  live). **Config: `Edge:Opc:GoodCountTag`/`RejectCountTag`/`StrokeCountTag`/`FeedLengthTag` (or per-line `?good=`
  etc.); needs the real INGEAR item ids wired + the edge on .170/.175 REDEPLOYED (current live build predates
  `/counters` → 404).** PERSISTENCE deferred: the good/reject piece totals already persist via the skid save
  (production_sheet_item); strokes/feed stay a live readout, faithful to legacy (they were display-only there).
- [x] **C** Coil barcode scan-to-load + actual-weight (`ABCO_COIL_NET_WT`) update — done (#300), ported from
  legacy `w_scan_coil_id`: `GET /das/scan/coil?barcode=&abJobNum=` normalises the label (upper/trim, strips the
  **`2S` vendor header** — `POS(id,"2S")` → keep what follows — then requires a plain digit string) and resolves
  it against the coils **on that job**, returning `Resolved` / `NotOnJob` / `Unreadable` with the coil's identity
  + weight so the operator confirms before loading. `POST /coils/{n}/actual-weight` records
  `abco_coil_net_wt` under the **legacy plausibility guard (>100 and <99999 lb)** so a scale misread or slipped
  digit can't become the recorded weight — enforced server-side, not just in the UI. DAS console gained a scan
  box (submit-on-Enter, as a scanner types) → confirm (optional actual weight) → loads the coil and opens its
  run. The `abco_coil_net_wt` column did not exist in the modern model at all and was added.
  *Improvement over legacy:* the label rules live server-side, so every future scanning surface (handheld,
  kiosk) shares one implementation instead of re-deriving them per window.
- [x] **H** Live shift efficiency % + coil yield % / finish-% (console, 5s cadence) — done (#287):
  `GET /das/lines/{n}/live`. **Both formulas were recovered from the legacy client, not invented**
  (there is NO efficiency/yield logic in Oracle PL/SQL — same finding as the PM scheduler):
  **efficiency** = `(shift seconds − downtime seconds) / shift seconds × 100` from
  `d_daily_prod_dt_efficiency`, where downtime is `shift.dt_total` once rolled up and the shift's own
  `dt_instance` rows until then (legacy's `if shift_dt_total > 0` precedence) — live, the shift runs
  to NOW and an OPEN downtime instance counts up to now, so the number moves while the line is down;
  **yield** = `(1 − scrap weight / the coil's ORIGINAL net weight) × 100` from `u_coil.of_get_yield`,
  with the legacy **95% red line** carried in the payload (`yieldTargetPct`) rather than hard-coded in
  the UI. Also returns coil finish-% against the run's begin weight and the shift's processed weight.
  Percentages are **omitted, not zeroed**, when there is no shift or coil. Surfaced on the DAS console
  (Efficiency / Coil finish / Yield / Shift wt cells, red under target, amber while down) and on the
  floor board (Effic + Yield metrics + a coil-finish bar).
- [x] **H** End-coil recap (ending status + closing weight) — done (#289):
  `GET /das/shifts/{n}/coil-runs/{run}/recap` returns the run plus what came off that coil on that job —
  skids, pieces, finished weight, scrap and the legacy yield. Scoped to the (coil, job) pair, since finished
  items are booked against the job and carry no run number. The console shows it the moment a run closes,
  and the **ending status is now an operator choice**: a picker seeded with the codes the plant actually
  uses (`shift_coil.coil_end_status` on the COIL_STATUS domain — by frequency over ~108k live runs:
  Done 83k, InProcess 8.6k, Rebanded 7.9k, New 6.4k, Rejected 1.6k, OnHold 54).
- [~] **H/M** Shift lifecycle: **auto-CREATE from the schedule done** (#301) — `create-scheduled-shifts`, a
  registered scheduler operation (+ `POST /das/shifts/create-scheduled` to run it on demand), derives the day's
  `shift` rows from the plant's own **`SHIFT_SCHEDULE` calendar** (~18.7k rows live) joined to `LINE_SCHEDULE`
  for the standing time pattern. **This is an IMPROVEMENT, not a port**: legacy maintained that calendar for
  years but still made a human create every SHIFT row on the daily-production screen — which is why the live DB
  carries shifts left open for days. Rules: idempotent per (line, schedule_type, day) using the same guard as the
  manual create; a **cancelled** calendar row is never created; a row with no time on either the calendar or the
  line pattern is **skipped rather than given an invented time**; the created shift is left OPEN (the DAS ends it).
  **Auto-CLOSE deliberately NOT done** — ending a shift stamps when work actually stopped, and a timer guessing
  that would corrupt the production record; the stale-shift monitor surfaces the ones nobody closed instead.
  ⚠⚠ **VALIDATED AGAINST LIVE DATA 2026-07-25 — THE PREMISE IS FALSE, DO NOT ENABLE.** The plant **stopped
  maintaining `SHIFT_SCHEDULE` in 2009** (newest row 2009-01-14, oldest 2005-04-03, **zero rows in the last
  365 days**), so on the real database this creates **nothing**. It is inert and harmless, not broken — but it
  does not do the job it was built for. **Falling back to `LINE_SCHEDULE` would be worse:** its standing pattern
  says shift 1 starts 06:00 / shift 2 at 14:30, while the plant actually starts them at **05:00** and **15:31**
  (30 of 32 recent shifts), so every auto-created shift would be ~1 h off — and **shift length is the denominator
  of the efficiency calculation**. Shifts are hand-created today *at the moment work actually begins*, which is a
  truer start than any stored pattern; that is very likely why legacy never automated it. Kept for the case where
  the plant revives a calendar, with the reason logged at runtime. **Reviving a schedule source is a plant
  decision, not a code change.**
- [~] **H/M** Shift lifecycle: **stale-shift detection done** (#289) — `GET /das/shifts/open`
  (`staleOnly=true` for those open longer than a day) + a notification-bell alert. This is the operational
  defect the live DB exposed: three shifts open 31 h / 31 h / 103 h, none with a `dt_total` roll-up.
  **Deliberately detection-only, not auto-close**: the legacy DAS station owns shift closure on the
  production database, and an automatic closer would be a competing writer (same single-owner rule as the
  EDI transmit seam). Auto new/end + grace stays open pending the DAS ownership decision.
- [x] **H** Drop/reverse a wrongly-loaded coil (+`ERROR_EVT`); change-job-mid-coil (split & save remaining wt)
  — done (#286): `POST /das/lines/{n}/change-job` ports legacy `u_coil.split_and_save` — closes the coil's run on
  the OLD job at the weight left (`process_wt` = begin − remaining), squares up its `process_coil` (status 1, as
  legacy hard-codes on a split — the coil is still running, just on another job) + runs the job-done cascade,
  moves the board **and** the `LINE_PRIORITY` queue to the new job, then opens a FRESH run for the SAME coil on
  the new job beginning at that weight. One deliberate deviation, documented in code: the coil's own balance is
  persisted (legacy carried it in the in-memory coil object) because the modern path is stateless and the new run
  would otherwise begin at a stale weight. `POST /das/lines/{n}/coil-run/reverse` drops a wrongly-loaded coil and
  **deletes** its run — as if it had never been loaded — logging an `error_evt` (with shift/coil/job) so the
  correction is on the record; **409 once the run has processed weight** (a real pass is corrected by weight, not
  erased). Console: "Change job (keep coil)" + "↺ Reverse coil".
- [x] **H** Per-line job queue / `LINE_PRIORITY` sequencing — done (#285): `GET /das/lines/{n}/queue`
  (schedule order, ended jobs hidden unless `includeEnded=true`), `PUT /das/lines/{n}/queue/{job}`
  (add/edit; omitted fields keep their value, a new row lands at the end as Waiting),
  `DELETE .../queue/{job}` (409 for the job the line is RUNNING) and `POST .../queue/reorder`
  (listed jobs take priority 1..N; jobs left out follow in their existing order, so a partial
  reorder can't drop work off the schedule). **Status legend recovered from the legacy
  `d_job_schedule` DataWindow: `0 = Ended, 1 = Running, 2 or NULL = Waiting`** — so a job displaced
  by the Operation Panel goes back to *Waiting*, and only the job-done cascade sets Ended (an
  earlier comment in #283 called status 2 "ran"; corrected). Console: a Line-queue table with
  move-up / remove / queue-a-job.
- [~] **H** Line auto-status controls + `noauto` write (lockout); fault/health lamps (DB / OPC `_ErrorCode` / PLC `activefault`)
  — **fault-code decode DONE** (#303): the `activefault` item reports a numeric CODE (live BL110 = 68) whose
  meaning lives in that line's **PLC program** — there is NO mapping in the ABIS schema, no fault table, and
  legacy never decoded it either (it only tested `> 0`). So rather than invent labels, ABIS now provides the
  dictionary and the plant fills it in: `abis_plc_fault_code` (line + code → description, **line 0 = a wildcard
  applying to every line**) + `GET/PUT/DELETE /lookups/plc-fault-codes`. **Ships EMPTY**; the lamp reads
  "code 68 — meaning not recorded" until an entry exists, then "Feed jam at leveller (code 68)". Verified
  against the live plant fault.
  — **lamps DONE** (#299): a DB / OPC / PLC / AUTO lamp strip on the DAS console, ported from legacy
  `u_fault_status_button` semantics — **all four are "healthy = lit"** (`uo_sql` = db alive, `uo_opc`/`uo_plc` =
  error code 0, `uo_noauto` = `set_select(NOT noauto)` so AUTO is lit in auto and dark on the lockout).
  Unknown renders grey, never green, so a dead feed can't look healthy. Health tags are DERIVED from the
  configured run tag's OPC branch (`PLC5-BL<n>.strokecnt` → `.activefault`/`.noauto`), so no extra operator
  config. **Verified against the live plant PLC**: BL110 showed PLC red "fault active (code 68)" + AUTO dark
  "MANUAL / auto locked out", matching the real bits. Still TODO: the **`noauto` WRITE** (the actual lockout
  control) — blocked on an edge WRITE path, which does not exist (the edge is read-only by design).
- [~] **H** Stacker dual-station automation (`SHEET_SKID_STACKER_1/2`) — **live dual-station view done**
  (#294): edge `GET /stacker` exposes each head's live piece counter + stack-complete bit + the stacker
  scale (legacy `stacker<n>.station1/2_stack_counter` / `Sta1/2StackComplete` / `ScaleSkidWt/Id`), and the
  DAS console gained a **"Stacker stations"** panel showing both heads side by side — each with the skid
  AT it (from the line board's `SHEET_SKID_STACKER_1/2` slots, already resolved) + its live count + a
  stack-complete badge. Same edge primary→fallback pattern; validated end-to-end vs a mock edge (station 1
  showed the seeded stacker skid + live count). Config in `edge/appsettings.Plant.example.json`; **needs
  the edge on .170/.175 REDEPLOYED** (the `/stacker` endpoint postdates the last edge build → 404). Still
  TODO here: the **stack-complete → auto-save skid** write (legacy `ue_sta1/2_complete` finalizes the skid
  at the head + clears the `SHEET_SKID_STACKER` slot) — the automation's write half.
- [~] **M** Stacker physical board (11 shape displays + ~16 conveyor cells + live stack tracking) — **conveyor
  path DONE** (#302). Key finding: the **19 `LINE_CURRENT_STATUS.SHEET_SKID_LOCATION_0..18` columns ARE the 19
  stations of the stacker→wrapper conveyor path** — the legacy board's `location_code` value list
  (`d_conveyor_skid.srd:12`) maps 1:1 onto them (0 Stack complete, 1 Leaving lift table, … 13 Overhead crane, …
  18 At end WP2 unload). So the board renders straight from the line board's `skids[]` slots with no new read
  path. Both that legend and the `conveyor` zone legend (`:13`) are now decoded in the shared `status-labels`
  (`stackLocation` / `stackConveyor` + an ordered `STACK_PATH`), ported verbatim rather than invented. The
  Stacker page gained a **Conveyor path** card: one row per line, stations left-to-right, occupied stations
  highlighted with the skid + job. **WRAPPER 2 HAS BEEN REMOVED FROM THE PLANT** (user, 2026-07-25 — the belt
  now runs stacker → wrapper 1 → output), so locations **14–18 are omitted** and the board shows **14** stations
  (0–13), not legacy's 19 — a deliberate divergence so the board reflects the real line. The removed codes stay
  in the decode map so historical rows still resolve, and a skid recorded at a removed station is **surfaced as
  a warning rather than dropped**, so real inventory can't disappear off the board. ⚠ **These columns are unpopulated on the live DB** (only a stacker head had a
  value), so the path renders empty in practice and says so explicitly rather than showing a silent blank row —
  they are written by the stacker automation.
  **LIVE CELLS DONE** (this PR): edge `GET /conveyor[?line=]` reads the conveyor's physical position
  sensors, keyed by the SAME location code, so the board no longer depends on those empty columns. The
  cell→location mapping is **recovered from the legacy stacker window**, where each tag's rising edge set
  `location_code` literally (`ue_on_conv1` → 3, `ue_entering_wp1` → 9, …) — ported, not invented; the table
  is in `docs/EDGE_SERVICE.md#conveyor-cells`. Config is a **map** (`Edge:Opc:ConveyorCells` + per-line
  `ConveyorCellsByLine`, since each line has its own `stacker110`/`stacker84` branch); a location may carry
  several tags (station 1 has one per head) and is occupied if ANY is truthy. **Occupancy only, deliberately**
  — the cells say a stack IS there, not WHICH skid; identity is overlaid from the DB where it exists, and we
  do NOT re-run legacy's tracking state machine because it owns those columns and a second copy would be a
  competing writer (same single-owner rule as shift close / EDI transmit). Board shows live cells distinctly
  from recorded DB positions, an unreadable cell as **unknown rather than clear**, and names the actual source
  in the subtitle — feed health is checked BEFORE contents, so recorded rows can never make a dead feed read
  as "live" (caught in browser verification). Two locations have no cell by design: 0 is the head's own done
  bit (`/stacker`), and **13, the overhead crane, is not a sensor** — legacy inferred it from cell 12's
  FALLING edge, which needs state the edge deliberately doesn't keep. *(That handler also settles the open
  question from #302: the crane is **wrapper 1's** output, so it correctly stays on the 14-station board.)*
  ⚠ **Needs the edge on .170/.175 REDEPLOYED** — `/conveyor` postdates the deployed build → 404 (the board
  then says "edge line feed unreachable" and falls back to DB-only, which is correct but empty).
  Still TODO: the 11 **shape displays**.
- [ ] **M** Supervisor/role PIN gating (exit / override / drop-coil / maintenance)
- [ ] **M** Serial scale zero command + scrap-scale/gauge separation
- [ ] **M** Live job sheet / e-folder (sketch image, shape-specific tolerances, coil totals, partial-skid usage)

## C. Buildable feature gaps (no blocker)

### C1. Commercial — order entry / parts / quoting / customers / accounting
- [~] **C** Quote pricing/cost model (CirclePro $/lb + job cost + ROS; SheetPro rectangular) — quotation emits
  yield-% only, "not a quote". **DECODED, not yet ported — see `docs/QUOTE_PRICING.md`.** Key finding:
  `w_circlepro.srw` is **transliterated BASIC** (`wf_line_240`, `wf_sub_2380` … are the original BASIC LINE
  NUMBERS kept as function names), which is why it has 129 single-letter variables and GOTO-shaped control
  flow. 112 of those carry the author's comments — the full map is in the spec. It computes total job cost
  and price/lb under TWO spacing modes (input spacing vs spacing = metal gauge), against average and maximum
  coil weights, with and without the scrap-handling charge. **The four-variable output groups are four
  NESTINGS — 1, 2, 3 or 4 circles across the coil width** (the program labels them "1 WIDE"…"4 WIDE"),
  staggered by √3/2 for hex packing, so the estimator sees four complete costings and picks one. Yield is
  circle area over consumed strip area. `ZU$` switches the whole model between the coil and PLATE paths.
  ⚠ **Gating task: real worked quotes (inputs + accepted outputs) from the plant.** Without goldens this is a
  re-derivation of a pricing engine, not a port, and errors surface as wrong prices to customers rather than
  as anything the UI would show. Also watch `Int(x + 0.5)` — round-half-up, NOT .NET banker's rounding.
- [ ] **C** Quote editor (`PUT /sales/quotes` + tabbed spec/pricing/inventory/shipment body) + save/reload + print + email
- [x] **H** Order edit-in-UI — done (#249): order-detail Edit toggle wires the existing `PUT /orders/{o}` + item PUT (editable header + per-line part/alloy/sheet/gauge/qty; full-replace-safe via spread)
- [x] **H** Assign customer coils to an order — done (#253): `GET/POST /orders/{id}/coils` + `DELETE /orders/{id}/coils/{coil}` + `GET /orders/{id}/available-coils` (legacy `ORDER_COIL` / `w_order_entry_coil_list`). Re-adding to the same order is blocked; a coil already on another order needs `confirm=true` (the dup-org warning, `otherOrderAbcNum`). Order-entry detail gained an assigned-coils panel + available-coil picker.
- [~] **H** Part revisions (version + re-point open items) — still TODO; **routing sequences per part — done (#258)**: `GET/POST /parts/{id}/routings` + `DELETE /parts/{id}/routings/{seq}/{line}/{die}/{shape}` over legacy `ROUTING` (line/die/shape + SPM & efficiency standards + edge-trim/stacker; all-column PK → edit = delete + re-add). Routings travel with a part copy and are cleared on part delete. Routing panel on the Parts page.
- [~] **M** **Part copy/delete + obsolete-in-use guard — done (#256)**: `POST /parts/{id}/copy` (part + blank geometry, INSERT…SELECT) + `DELETE /parts/{id}` refused with 409 when referenced by any order line (order_item.part_num_id), with Duplicate/Delete buttons on the Parts page. **Order copy/duplicate — done (#255)**: `POST /orders/{id}/copy` clones header + items + geometry. **Order-entry part picker — done (#265)**: the New-order line's Part # field autocompletes from the customer's parts (datalist) and, on selection, prefills alloy/sheet/gauge/pieces + tags the line with its part_num_id. Still TODO here: end-user change cascade (largely covered by order-edit's enduserId)
- [ ] **H/M** Sector consistency validation; edge-trim tolerance gate + override + `f_add_system_log_tran` audit
- [~] **M** Accounting scrap-type summary; print coil-cert label on order close; **customer delete — done (#261)**: `DELETE /customers/{id}` refused with 409 when referenced by any order/part/coil/shipment, else deletes the customer + its contacts + recovery config; Delete button on the Customers page.

### C2. Logistics / shipping
- [~] **C** Packing-list line items — ✅ `SHEET` (#217) + `SCRAP` (#219) + `REJECT_COIL` (#220) built (add/list/remove on the shipment, feeding the 856); only `WH_PACKING_ITEM` (9 live rows) deferred
- [~] **C** BOL / combi-form / packing-ticket printing (the `rpabco` document engine) — **BOL + packing
  tickets DONE; combi form sized, not built.**
  - **BOL totals** (#307): `f_get_bol_totals` ported — single- vs multi-stop detection, the per-BOL
    sheet/scrap/reject rollups, and the "Shipping with BOL …" package note (multi-stop only; a stored
    note in `shipment_reference_codes` wins over a recount so paperwork already in a driver's hand
    can't be contradicted).
  - **BOL document + printed form** (#308): `GET /shipments/{pl}/bol-document` + the upgraded printable
    BOL — three named sections (Skids of Aluminum Sheets / Accumulated Scrap Return / Rejected Coil
    Return), per-job PO/part blocks, shipment total, multi-stop note. Section weights are GROSS but a
    job's subtotal is NET (legacy, pinned by test); job data comes from the skid's REFERENCE order, not
    the job's own; >3 jobs prints totals-only with a stated reason (legacy's "print without details"
    branch); an EMPTY shipment is refused with 409 rather than printing a blank form.
  - **Per-skid packing ticket** (#309): `GET /documents/packing-ticket/{itemType}/{pl}/{refNum}` for
    SHEET / SCRAP / REJECT_COIL. Shape dimensions resolve through `ShapeGeometry` instead of legacy's
    eight-way outer join — which also fixes REINFORCEMENT and LIFTGATE, omitted from legacy's ticket
    query and therefore printing no dimensions at all.
  - [ ] **Combi form — SIZED 2026-07-25, own session.** 16 layout variants in `legacy/src/rpabco`
    (alcan, alcoa, alcoa_pn, kaiser, novelis, novelis_cd, twb, twb_cd, sm, display, display_pn,
    display_t, input, input_twb, input_detail, base) but only **7 distinct queries — and 8 of the 16
    share one**. So this is the EDI-partner shape: one base document + per-customer layout overrides,
    not sixteen documents. The top-level query is just the `shipment` header with NULL placeholder
    columns; the detail comes from nested reports (`d_report_combi_sheet` / `_scrap` / `_rejcoil`).
    Detail grain is the **production item**, not the skid: ticket, coil lot + org, pieces, net /
    theoretical / tare / gross — **each in both lb and kg** (it is a packing list and weight
    certificate combined, hence "combi").
    ⚠ **Customer-specific rule found in the SQL:** `d_report_combi_sheet` hard-codes
    `customer_id = 2802` — **TOYOTA TSUSHO AMERICA (TOYOTA TSUSHO - KY**, confirmed on live Oracle
    2026-07-25) — to print `prod_item_theoretical_wt` in place of `prod_item_net_wt`. A real business
    rule buried in a DataWindow; **do not port the combi form without carrying it**. Swept every combi
    query for hard-coded ids: 2802 appears in **all four sheet-detail variants** (`d_report_combi_sheet`,
    `_cd`, `_cd_twb`, `_pn`) and **nowhere else** — so it is one consistent rule, not a scatter of
    special cases. Model it as a per-customer "invoice on theoretical weight" flag rather than an id
    literal, and confirm with the plant whether any customer has since joined or left it.
- [ ] **H** Sketch image storage (`sketch_view` LONG RAW) + display + job/part linkage + DAS/e-folder render
- [~] **H** Die → shape mapping — done (#254): `GET/POST /line-die-shapes` + `DELETE /line-die-shapes/{shape}/{line}/{die}` over `LINE_DIE_4SHEET_TYPE` (composite PK), so scheduling can resolve the eligible line/die for a shape (filter by sheetType/lineNum/dieId; add guards line/die-exist + dup). Dies page gained a mapping panel. Still TODO: **die label/report print**.
- [x] **M** Shipment header EDI-trigger fields — done (#259): the shipment read now carries `edi_req`/`edi_triggered`/`edi_file_id_856`/`edi_file_id_desadv` + the 856/desadv/des-856 dates, and `POST /shipments/{pl}/edi-trigger` (docType 856|desadv + optional file id) stamps them (bookkeeping only — never transmits). Surfacing on the shipping UI is a follow-up.
- [~] **M** **View archived EDI payload — done (#270)**: the EDI monitor's Transaction-detail card has a "View X12 payload" button that fetches the stored X12 (`GET /edi/transactions/{id}/payload`) into a scrollable pre + Copy. Still TODO (both deliberately deferred): manual EDI **send/resend** from UI (blocked by the no-transmit guardrail — legacy owns the VAN); X12 map maintenance.
- [~] **L** **Shipment status-change history — done (#264)**: `GET /shipments/{pl}/history` reads `SHIPMENT_TRACK` (before/after shipment+vehicle status + customer/ship-to + who/when, newest first); **UI done (#271)**: a newest-first status-history table on the Shipment detail card (pre→cur transitions). **carrier DUNS/street/zip/country fields — done (#261)**: added `carrier_street`/`carrier_zip`/`carrier_country`/`carrier_duns_number` to the carrier read+write + Carriers form inputs.

### C3. Coils / receiving
- [~] **C** Warehouse skid CRUD + status-20 warehouse-coil mint — **create path done (#317)**, ported from
  the legacy warehouse module (`w_wh_business` action 1). `POST /warehouse/skids` runs the whole chain in one
  transaction: resolve the reference order from the job → **resolve-or-mint the status-20 warehouse coil**
  keyed on (customer coil number, lot) → `sheet_skid` + `production_sheet_item` + `sheet_skid_detail` →
  package number. **The warehouse coil is an empty SHELL** — minted at `net_wt`/`net_wt_balance` 0 with
  `process_quantity` 0; anything summing coil weight must keep excluding status 20 (`OnHandCoilPredicate`
  does, and a test pins it) or the floor appears to hold warehoused metal it doesn't. Identity (cash date,
  customer) is INHERITED from the customer's real coil; when there is none **and** the customer requires a
  cert label or cash date, the mint is REFUSED (409) rather than back a certificate with nothing.
  Weight/piece mismatch is a **warning, not a gate** — legacy asks "save it anyway?", so blocking would stop
  real corrections. Fixture gained `coil.cash_date`. Still TODO: skid **modify/delete** (`wf_coil_used_by_others`
  guards a coil shared by other items), the item-level editor, and the warehouse UI page.
- [x] **H** Coil-ownership transfer mint semantics — done (#224): mints a NEW `coil_abc_num` (status 2, from-cust set) + original → status 13; cert carries the new id
- [x] **H** Bulk "Change status → Ready for transfer" (status 12) — done (#240 `POST /coils/ready-for-transfer` with eligibility guards; #241 picker `readyOnly` filter + coil-ownership mark-ready UI)
- [~] **H** Scrap-skid + sheet-skid guarded DELETE done (#243). **Return-scrap done** (#XXX): POST /scrap-skids/{n}/return faithfully ports the live F_CONVERT_BACK_TO_SHEET proc — copies the scrapped mirror rows (scraped_sheet_skid/production_sheet_item/process_partial_skid/detail) back to the live tables, deletes the mirrors + scrap_skid(+detail) + credits back the linked return_scrap_item rows. Still TODO: sheet-skid modify + weight/piece reconciliation.
- [~] **H** Guarded coil delete — done (DELETE /coils/{n}, refuses coils applied to a job or done/shipped/transferred); change-coil-customer-on-BOL cascade still TODO
- [x] **H** Mint carries full coil attributes — already done in #224: the ownership-transfer mint does a `SELECT *` schema read and copies every coil column (cash_date / part_num / material_num / mid_num / damaged_code / …) to the minted coil
- [x] **H** Coil-quality capture + flaw mapping (#246 GET/PUT /coils/{n}/quality + POST/DELETE .../quality/flaws) + a **Coil quality** capture page (#247). Inbound status-on-receipt is already handled: MintBolCoilsAsync sets `coil.date_received` at receipt and status 11 (QA-hold) when `receiving_bol_coil.damaged_fault=1` (the damage code lives on receiving_bol_coil, not the coil). Remaining tail: QR/barcode capture feeding the flaw map (needs the handheld/barcode integration).
- [~] **M/L** Import-from-BOL / show-archived-BOL browsers; multi-condition coil search (search term over org/lot/mid/notes + temper filter DONE on GET /coils + coil-inventory UI); manual new-coil + live-scale weigh-in — remaining: BOL browsers, gauge/width ranges, live-scale

### C4. Handheld scanner (RF coil-receiving)
- [x] **C** `INBOUND_COIL_STATUS` model + barcode→ABC lookup + mint-decision — done (#311), ported from the
  plant's LIVE handheld CGI (`coil_receiving_12.pl`, the active version per the vendored README).
  `GET /receiving/scan?barcode=` normalises the scan, reports ABC numbers already minted for that
  customer coil, and attaches the mill's advance notice. **The two scanning surfaces do NOT share a
  barcode rule**: the DAS console (`CoilBarcode`, #300) strips through `"2S"` and needs digits because it
  resolves our numeric `coil_abc_num`; the handheld drops ONE leading `S` and resolves
  `INBOUND_COIL.COIL_NUMBER`, the CUSTOMER's number, which may contain letters. Each label run through
  the other's rule resolves to nothing — #300's claim that one implementation would serve every scanning
  surface was wrong and is corrected in code, with a test pinning the divergence. Also ported: the
  `000000` → `"NO BARCODE"` sentinel, and already-minted being a CHOICE (legacy offers reprint AND mint-
  again on the same screen), not a block. Fixes over the CGI: bound parameter (a scanner is untrusted
  input), all matching ABCs returned, and `FirstOrDefault` for the advance notice since a coil can appear
  on several inbound EDI files.
- [~] **C** Native Zebra ZPL/CPCL over TCP :6101 + printer routing by device IP + connectivity check —
  **label + seam done, transport not wired.** `ZplLabels.CoilAbcLabel` is the legacy payload byte-for-byte
  (inverted `^BCI`/`^A0I` orientation, `^PW384`/`^LL0203` stock size, sent TWICE per mint), pinned by test.
  `ICoilLabelPrinter` is the transport seam; the default `NoOpCoilLabelPrinter` does not print and reports
  itself **unreachable** — which is the safety property, because minting checks reachability FIRST.
  Still TODO: a real socket transport to `:6101` + the device-IP → printer map
  (`192.168.10.8/9/10` → `192.168.10.12/13/14`) + an offline page. **Needs hardware to validate.**
- [x] **H** Single-scan ABC mint — done (#312): `POST /receiving/scan/mint` draws the next
  `coil_abc_num` and stamps `inbound_coil_status`. **The printer is checked BEFORE anything is minted**
  (legacy pings first): unreachable → 503 and nothing minted, because an ABC number with no printed label
  leaves a coil untagged on the dock with nothing to reconcile it against. Unknown coil → 409, and no
  sequence value burned. Legacy's UPDATE is unscoped (`WHERE COIL_NUMBER = …`) so minting again
  OVERWRITES and orphans the earlier label — preserved faithfully, but `replacedAbcNum` reports it
  instead of it being silent.
- [ ] **H** Lookup by scanned customer coil (`coil_org_num`); QR capture → `BARCODE_STRING` upsert
- [ ] **M** S-header strip+validate; coil-defect email notification
- *(Done: scan→verify→label handheld page + HTML coil label.)*

### C5. Quality
- [x] **C** 863 mechanical test-result WRITE (`PST_TEST_RESULT`) — done (#225 API + #226 Quality "Test results" page); the read-only list can finally populate
- [x] **C** Coil QA-hold workflow (status 11) + `COIL_TRACK_QA` audit + search/browse console — done (#227: qa-hold/qa-release/qa-history endpoints + Quality "QA hold" console)
- [x] **C** Dimension-check tolerance validation vs part-shape spec (nominal ± tol) — the real QC gate. **DONE via WinSPC.** WinSPC (the plant SPC system of record) owns the measured value + LSL/target/USL + pass/fail per characteristic, tied to ABIS by the "Job #"/"Coil #" tag. Phase 1: #229 read-only connector + #230 Dimensional QC page + #233 trend chart. Phase 2: #234 — `POST /coil-eval/skids/{n}/dimension-checks` validates the submitted measurements against WinSPC's authoritative LSL/USL and sets `in_spec` from that (falls back to the supplied flag when WinSPC has no data/disabled). Validated against live data (job 124346). The legacy `d_skid_dim_check` rule stays un-reconstructable but is moot. Live-wiring: abis_ro read-only SQL login on RSEDAM-PC (192.168.10.143,1433) + WinSpc:Enabled on ABIS.
- [ ] **H** Instron `.ASC` test-file import & parse (up to 9 samples)
- [~] **H** Recovery report suite (remaining ~6 templates); **customer-report SETUP write — done (#257)**: `PUT/DELETE /quality/recovery-customers/{id}` (recovery_report_customer: name + allProducts/autoOnly/commOnly) + `PUT/DELETE /quality/customer-defects/{cid}/{scrapTypeId}` (cust_scrap_type_needed; PUT 404s on an unknown scrap type, returns the enriched row). API + tests; **setup UI done (#272)**: the Quality/Recovery page's Recovery-customers tab gained an add/edit form + per-row Delete, and the Customer-defects tab an add/update form (scrap type from catalog + ABC-Mill + autoparts flags) + per-row Remove.
- [~] **M** Recovery depth — **add/remove coil-job done** (`PUT` upsert #—prior + **`DELETE /recovery/jobs/{job}/coils/{coil}` #267** removes only the `recovery_job_coil` overlay row, 204/404). Still TODO: autoparts filter, pull-from-DAS-vs-office, email/print/export.
- [x] **M** Dimension-check edit/delete; job-level dim-QC green/red board; good-material in-spec rollup; PC# auto-increment — done (#236 edit/delete + PC# auto-increment; #237 QC board page + GET /coil-eval/jobs/{n}/qc-board with good/out-of-spec roll-ups + WinSPC verdict)
- [~] **M** QA coil photos; QA email notification; **"make scrap" action — done (#266)**: `POST /sheet-skids/{n}/make-scrap` faithfully ports legacy `F_CONVERT_TO_SCRAP` (mints a scrap skid, moves the skid + production items to the `scraped_*` mirrors, credits each as a `return_scrap_item`, removes the live rows) — the inverse of the return-scrap #250, and reversible via it. **UI done (#269)**: "Make scrap" (per sheet skid) + "Return to sheet" (per scrap skid) confirm-gated row actions on the Skids page.

### C6. Platform / admin / reports
- [~] **C** Scheduler EXECUTION engine — DONE: `SchedulerHostedService` (off by default, `Scheduler:Enabled=false`) + `SchedulerService`/`CronSchedule` (5/6-field cron matcher) dispatch enabled+due jobs to an **allowlist** of in-process `IScheduledOperation` handlers (noop/heartbeat seeded); unknown/legacy `target_operation` is recorded "unsupported" and NEVER executed (no shell/legacy path → guardrail intact). `POST /admin/jobs/{id}/run` for manual/on-demand. Still TODO: cron auto-import off the DB host (the server-console DB-host cron card already reads the .230 crontab read-only — see [[abis-230-cron-inventory]]).
- [~] **M** Preventive-Maintenance (PM) scheduling subsystem — **API COMPLETE** (#273 read, #274 write, #275 completions):
  models `pm` / `pm_actions` / `pmcompletions` / `pmshift` over the 4-level equipment hierarchy
  (`groupdepartment → systemequipment → subsystemequipment → itemdevice`) + `titlecraft` rates.
  `GET /pms` (paged, hierarchy names, derived `daysUntilDue`/`dueBucket`), `GET /pms/due` (due board),
  `POST/PUT/DELETE /pms` (guarded delete: 409 when completions exist → retire via `pm_status = 0`),
  `POST/DELETE /pms/{id}/actions` (checklist, pm-scoped), `POST /pms/{id}/complete`.
  **Key finding:** legacy PM has NO scheduling engine — `nextduedate` is hand-entered (zero PM logic in
  the live PL/SQL; the PB windows are DataWindow forms). Auto-advance on completion is a deliberate
  ADDITION (user-approved): explicit date wins, else `daysBetween`, else `365/numOfTimesPerYear`; a PM
  with no interval keeps its stored date. The response reports the basis used.
  Oracle care: `pm`/`pm_actions`/`pmcompletions` added to `Database:MaxIdTables` (no sequence — legacy
  minted ids via `wf_getnew_id`, else ORA-02289); INSERT params ordered to match placeholders (ODP.NET
  binds positionally); `assignedtogroup` NOT NULL falls back to a non-empty label (Oracle `''` = NULL).
  Still TODO: the Maintenance-page PM UI (due board + list/detail + checklist + Complete).
- [ ] **M** Maintenance parts/spares inventory (`PARTS`/`PARTS_SUPPLIERS` — the maintenance spares tables, distinct from the product `part_num`); equipment hierarchy cascade + More-Details; log record-nav + maintenance reports
- [~] **M** Uptime reports + downtime pivots — done (#252): `/reporting/uptime` (groupBy line|shift|day; worked-shift uptime = (shift length − dt_total s)/3600 + scheduled/downtime hrs + uptime %, faithful to `w_report_uptime`) and `/reporting/downtime-pivot` (groupBy cause|job|**part** (#268)|line|shift|day|month|year — the by-part pivot walks ab_job→order_item→part_num, labelled by enduser_part_num). Remaining tail: a dedicated dt-vs-production ratio (uptime % already carries downtime-as-%-of-scheduled).
- [x] **M** Native Excel export — done (#252): dependency-free OOXML `.xlsx` writer (`clientapp/src/xlsx.ts`, STORED zip + CRC32 + inline strings; numbers stay numeric), "Export Excel" on every report next to Export CSV. openpyxl-validated.
- [~] **C/H** Feature-gate the write tags still auth-only. Done for every tag that maps 1:1 to a nav-gated feature (safe — the user who can reach the page already holds it; kiosks/edge use the API key and bypass): **Jobs**→Production Control, **Shipments**/**Stacker**→Warehouse, **CoilOwnership**→Inventory(Coil), **TestResults**/**Recovery**→Quality Control, **ProdFolder**→Production Control, **Downtime**→Downtime report (added to `FeatureByTag`). Still **deferred:** Dies / Sketches / Sales / Accounting / Trucks / Carriers / DAS / ScanLog / OpcLog — their nav pages have NO feature gate, so there's no authoritative feature name to gate the API on without risking a lockout; needs live `security_application` verification.
- [ ] **M** OPC-log collector + item-selection config (viewer is read-only; edge is the producer); source/host/device tree
- [ ] **M** Step-up re-auth popup; in-DB job control (DBMS_SCHEDULER enable/disable/run-now)

## D. Bug / robustness leftovers (from the sweep — verified, low severity)
- [x] **M** Invoice-save duplicate: return **409** not a 500 — done (#260): `CreateInvoiceAsync` now catches the PK violation on the INSERT (the pre-check's TOCTOU race) and re-checks → returns Duplicate (409) instead of a 500.
- [ ] **L** `If-Match` optimistic concurrency: push the version into the UPDATE `WHERE` (true compare-and-swap; today check-then-act) — `WithIfMatch`
- [x] **L** Invoice **tare** bucket — done (#260): `GetInvoiceComputationAsync` tare now excludes voided skids (`skid_sheet_status <> 6`) so it matches `SkidCount`.
- [x] **L** Stacker board — done (#260): `job_status NOT IN (0,3)` → `IN (1,2,4)` (matches its comment; robust to NULL/new codes).
- [x] **L** On-hand-coil + skid-count `IS NULL` guards — done (#260): `OnHandCoilPredicate` + every `skid_sheet_status <> 6` now guard NULL (`IS NULL OR …`).
- [ ] **L** `pollPieceCount`: clear `pieceCurrent` on a transient edge outage so a stale count isn't shown — `das-console.ts`
- [ ] **L** Committed `wwwroot/…/generated/abis-client.js` drifts vs a fresh gen — regen periodically or CI-enforce

## E. Config / turn-on / deploy (user-gated, not code)
- [ ] Redeploy codi-ABIS to **v0.4.18** (dashboard piece count + the client bug fixes)
- [ ] Wire BL110 piece-count tag per DAS station via the 🔎 picker (`stacker110.station1/2_stack_counter`)
- [ ] Enable `Notifications:EdiStall` **after** the data-source cutover (else false alarms on the frozen .230 ledger)
- [ ] Server-console restart button — decide on/off (polkit rule per `docs/SERVER_CONSOLE.md`)
- [ ] BL84 stacker piece-count — **parked ~6 months** (stacker out of service)
