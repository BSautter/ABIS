# ABIS — Remaining Work Backlog

> Living checklist of what's **still open**, generated 2026-07-11 from the parity re-audit (7 surfaces
> re-verified against the current code) + the correctness bug sweep. Work it top-to-bottom.
> The historical full-detail gap report is `PARITY_AUDIT.md` (2026-07-07, mostly closed since).
> What's **done** lives in git history + GitHub releases (current: **v0.5.5**).

**Legend:** `[ ]` open · `[~]` partial · severity **C**ritical / **H**igh / **M**edium / **L**ow.

- [x] **IT holds every feature — done (#372), applied on .230 2026-08-05.** Per the plant's instruction
  the IT group (5 members) now has Write on all **39** features; it was missing five — `Line Employees`,
  `Maintenance_logs`, `Part Number`, `Scheduler Admin`, `Server Admin`. Applied through the app's own
  grant endpoint, and re-applied automatically by `refresh-nonprod.sh` because a Data Pump refresh
  replaces `SECURITY_GROUP_APPLICATION` from prod.
- [x] **Who else holds Parts / Maintenance — settled 2026-08-05: IT assigns as needed.** The plant's
  call: five holders is enough to administer, and IT can grant `Part Number` / `Maintenance_logs` to
  testers and to the people who need them as testing widens. No code change — it is done from
  Admin → Security, which is exactly what that screen is for. Recorded so the next session does not
  re-raise it as an open blocker.
- [x] **C** EDI outbound generation (861 / 870 / 846 / 856) — **DONE + tested + verified live on codi-ABIS**.
  All 17 live-partner profiles on the `abis_edi_partner` backbone map to a built variant: **861** Novelis(1153/1459/2582)/
  Commonwealth(1980)/Constellium(2776)/Arconic(2784); **870** Novelis(1153/1459/2950)/Aleris(1980)/Constellium(2776,
  per-coil); **856** Novelis/Constellium/Arconic; **846** Cleveland-Cliffs(3061). Golden byte-tests where a production
  `.edi` existed (856/861/870). Admin UI for the profiles shipped.
- [~] **Cleveland-Cliffs Outside Processing** — the guides landed 2026-08-20 and the picture changed: Cliffs is not
  one 846, it is a **23-guide program with a 19-case certification plan** (810/846/856/861/863/867/870, both
  directions), and it has **never gone live** — customer 3061 has zero orders and zero coils, the cron entries are
  commented out and marked "TEST ONLY", and every archived output is the empty placeholder. So **no golden exists
  for any Cliffs document** and the guides are the spec. The 846 is now reconciled against the guide (BIA06 action
  code, `N1*MF` not `SU`, heat number, no bare qualifiers); everything else is unbuilt. Full map, the six open
  decisions (starting with: the DUNS we hold for 3061 matches **none** of Cliffs' four works) and the build order:
  **[EDI_CLIFFS.md](EDI_CLIFFS.md)**. Note this is the first work that would need an **inbound** EDI parser.
  Its two hard blockers (the works DUNS, and the ISA/GS envelope for every set but the 846) are in
  **[OPEN_QUESTIONS.md](OPEN_QUESTIONS.md)** along with everything else waiting on a human answer.
- [x] **H** 997 functional-ack matching + aging bell alert (#206/#207) — `Edi997Parser` + `/edi/997/waiting` + `/edi/997/ingest`.

**Deferred out of 0.5 (accepted at close — NOT built; carried on the backlog):**
- [ ] **DEFERRED (data-blocked)** **863 (test-result report) generation** — no production golden; the `.863` file may not
  even be swept/transmitted by GXS. Also depends on the 863 test-result **WRITE** path (§C5) as its data source first.
  Revisit once §C5 exists AND the plant supplies a real 863 golden / confirms it's transmitted.
- [ ] **DEFERRED (data-blocked)** **Inbound 856 (ASN) ingestion** (parse → `inbound_shipment` / `inbound_coil` / status)
  — the only inbound sample is a 2009 **test 850**; no real inbound business doc to validate against. Needs a real golden.
- [ ] **DEFERRED (by policy)** EDI VAN transport (GXS / Inovis SFTP) + postpro — legacy-owned, do NOT build (transmit seam stays no-op).
- [ ] **DEFERRED (operational, → 1.0)** Data-source cutover (codi-ABIS reads the .230 sandbox, not live prod .9) — enables the EDI-stall alert to be meaningful.

- [x] **4x6 skid + scrap tags as ZPL — done (#377).** `SkidTag4x6.SheetSkid` / `.ScrapSkid`, ported
  from the VENDORED DataWindows `legacy/src/da/d_skid_ticket_new.srd` and `d_scrap_skid_ticket_new.srd`
  (these were in `legacy/src/` already — unlike the 6x10, no PBL extraction needed). 4x6in at 203 dpi
  (`^PW812`/`^LL1218`), thermal transfer, and the scrap tag's coil table repeats per contributing coil.
  <br>**Barcode prefixes are load-bearing and NOT interchangeable:** sheet = `S<num>`, scrap =
  `3S<num>` (legacy's own samples are `*S123456*` and `*3S123456*`). The handheld strips ONE leading
  `S`, so a scrap code beginning with `S` would survive that strip as a plausible sheet-skid number and
  resolve to the wrong record. Guarded by a test asserting the scrap prefix does not start with the
  sheet prefix.
  <br>**WIRED (#378)** — `POST /documents/sheet-skid/{n}/print` and `/scrap-skid/{n}/print`, routed to
  the printer at the line that made the skid. `LabelPrinters:LineRouting` maps `"<line>"` and
  `"<line>:<purpose>"`, so BL110's skid and offload printers are both addressable. An unrouted line
  prints NOWHERE by design. Gated on `Inventory(Skid)`.
  <br>**STILL NOT PRINTED.** Both tags need a test print before use, and the config needs real
  `LineRouting` entries — the line_num values are internal CODES, so resolve them from the LINE table
  rather than assuming BL78 = 78. `192.168.9.9` (BL110 skid) did not answer when probed.

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
- [x] **M** Supervisor override PIN — **BUILT** (plant chose option (b), 2026-08-08). Full detail in [docs/SUPERVISOR_PIN.md](SUPERVISOR_PIN.md).
  <br>**What legacy does** (`w_super_validation.srw` / `_offline`, opened from
  `w_da_sheet.wf_super_validation`): compares the typed digits **in plain text**,
  `if parent.st_password.text = is_pw`, where `is_pw` is
  `ProfileString(gs_downtime_ini_file, "OPCItems", "is_shift_super_password", "1234")` — one SHARED
  secret in an INI file on each DAS PC, **defaulting to `1234`**, with unlimited attempts. Its window
  even has a "Shift supervisor" name field, which is never populated and reads `none` forever.
  <br>*Whether* an override is gated is plant behaviour and is kept where legacy puts it; *how* it
  authenticates is replaced by a per-supervisor PIN — hashed (the existing PBKDF2 path), rate-limited
  with lockout, and **attributed**.
  <br>**The gates are the four live call sites**, not the four this backlog entry used to guess at.
  The substantive one is closing a coil whose weights do not balance: legacy computes
  `ir_hl_percent` and above **0.5%** disables the save until a supervisor authorises it
  (`u_tabpg_end_coil.sru:757`). So the PIN's real subject is *who agreed that this coil's missing
  metal could be written off.* The others are the shift-end override, the Operation Panel, and the
  offline sheet.
  <br>**Holding a PIN is the eligibility** — there is no second "is supervisor" flag to drift, and no
  new `SECURITY_APPLICATION` feature was invented (issuing one is gated on the real `User Control`).
  The PIN is a **separate secret from the sign-in password, in its own table**: four digits typed on a
  shared panel in front of an operator must not open an application session.
  <br>**The 0.5% gate is wired**: `GET /das/coils/{coil}/balance` returns the three stored terms and
  the console refuses a plain save above tolerance, leaving the supervisor override as the only way
  through — legacy's disabled-Save-plus-Override-button. Server-side enforcement of the threshold is
  deliberately absent, as it is in legacy, whose check is client-side.
  <br>**Still owed:** none of it has run on a plant panel, and the balance SQL has never run against
  Oracle.
- [~] **M** Serial scale zero command + scrap-scale/gauge separation — **zero command BUILT; separation
  still open.** `POST /scale/zero` on the edge sends legacy's single `'a'`
  (`w_da_sheet.wf_zero_scale`), exposed on the DAS console as a confirmed **⌫ Zero** button.
  <br>**The one behaviour deliberately NOT ported:** legacy returns *success* when its scale is not
  connected (`if not ib_scrap_scale_connected then return 0`). An operator told the scale zeroed
  weighs against a tare that was never cleared, and every skid on that scale is then wrong by the
  same amount with nothing downstream able to see it. The edge answers three distinct ways instead —
  sent (200), this device cannot be commanded (409, the normal answer where skid weight is an OPC
  tag), port not open (503).
  <br>Reading is left PASSIVE rather than switched to legacy's poll-with-`'b'`; the plant's readings
  have been tuned against the passive form (#338, #341) and changing it unverified would risk the
  weights themselves.
  <br>**Still open:** the scrap-scale/gauge separation, which needs the plant's actual device layout
  — and none of this has been exercised against a real scale.
- [x] **M** Live job sheet / e-folder — **BUILT.** `GET /prod-folder/jobs/{job}/job-sheet` returns the
  whole PRODUCTION ORDER (spec, shape dimensions with tolerances, coil totals, partial-skid usage,
  both edge-trim warnings), rendered on the production folder and on the DAS console where it
  re-reads on every job change. Printable. Ported from `coil_eval/u_tabpg_job_sheet.sru` +
  `downtime2/d_report_prod_order.srd`; every query verified against live `.230`.
  <br>**Two things it turned up.** The job screens were serving drawings out of the retired `sketch`
  table — 31,048 live jobs got no drawing and **3,420 got a different part's** — because the port was
  taken from `da/w_da_sheet.srw`, whose `wf_show_job_sheet` is entirely commented out. And
  `order_item.sh_toleranc_minus` is spelled without its final `e` while its own `sh_tolerance_plus`
  keeps it and `part_num` spells both in full.
  <br>Still open: the sheet is read-only. Legacy's tab has a Print button and nothing else, so this
  is parity — but the e-folder's *notes* remain a separate card rather than part of the sheet.

## C. Buildable feature gaps (no blocker)

### C1. Commercial — order entry / parts / quoting / customers / accounting
- [ ] **SHELVED 2026-08-04 — do not build until sales decides they want it in ABIS.** Quoting is done
  **in an Excel sheet today** (plant, 2026-08-04), outside ABIS entirely. Whether it should move into
  ABIS at all is a sales decision that has not been made, so porting the model now risks building the
  largest remaining Critical for nobody.
  **Unresolved tension to settle first:** the plant confirmed on 2026-07-25 that **SheetPro is still
  used**, which does not sit easily with quoting being done in Excel. Either the PB tools still do the
  *calculation* while Excel is the *document*, or SheetPro is vestigial and the spreadsheet replaced
  it. That decides what a port would even target — `w_quotation_new.srw`, or the spreadsheet.
  **If it is ever picked up, it is blocked on two things that cannot be inferred:** the real burdened
  labour rate and productive-minutes allowance (hard-coded `40` and `45` — a rate frozen at $40/hr
  quotes every job wrong and nothing in the output shows it), and two or three worked quotes with
  inputs and accepted outputs. The decode is done and keeps: `docs/QUOTE_PRICING.md`.
  Original entry follows.
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
  **SheetPro DECODED too (2026-07-25)** — it is `w_quotation_new.srw` (no `w_sheetpro.srw` exists; identified
  by its `d_report_sheetpro` report object), same transliterated-BASIC lineage, 166 variables. It is the more
  COMPLETE commercial model: cost, mark-up, price/lb, invoice, plus an actual-vs-computed profit analysis.
  The whole thing is four formulas.
  ⚠ **Two hard-coded constants must NOT be ported as literals: `40` is a labour rate ($/hr) and `45` is
  assumed productive minutes per hour.** A rate frozen at 1990s levels quotes every job wrong and nothing in
  the output would reveal it — they must become configuration, confirmed by the plant before any quote is issued.
  ⚠ **`hi` is a MARGIN divisor, not cost-plus** — entering 30 gives `price = cost/0.70` (a 42.9% markup);
  reading it as cost-plus understates every price by ~9%.
  ⚠ **Gating task: real worked quotes (inputs + accepted outputs) from the plant.** Without goldens this is a
  re-derivation of a pricing engine, not a port, and errors surface as wrong prices to customers rather than
  as anything the UI would show. Also watch `Int(x + 0.5)` — round-half-up, NOT .NET banker's rounding.
- [ ] **C** Quote editor (`PUT /sales/quotes` + tabbed spec/pricing/inventory/shipment body) + save/reload + print + email
- [x] **H** Order edit-in-UI — done (#249): order-detail Edit toggle wires the existing `PUT /orders/{o}` + item PUT (editable header + per-line part/alloy/sheet/gauge/qty; full-replace-safe via spread)
- [x] **H** Assign customer coils to an order — done (#253): `GET/POST /orders/{id}/coils` + `DELETE /orders/{id}/coils/{coil}` + `GET /orders/{id}/available-coils` (legacy `ORDER_COIL` / `w_order_entry_coil_list`). Re-adding to the same order is blocked; a coil already on another order needs `confirm=true` (the dup-org warning, `otherOrderAbcNum`). Order-entry detail gained an assigned-coils panel + available-coil picker.
- [~] **H** Part revisions (version + re-point open items) — still TODO; **routing sequences per part — done (#258)**: `GET/POST /parts/{id}/routings` + `DELETE /parts/{id}/routings/{seq}/{line}/{die}/{shape}` over legacy `ROUTING` (line/die/shape + SPM & efficiency standards + edge-trim/stacker; all-column PK → edit = delete + re-add). Routings travel with a part copy and are cleared on part delete. Routing panel on the Parts page.
- [~] **M** **Part copy/delete + obsolete-in-use guard — done (#256)**: `POST /parts/{id}/copy` (part + blank geometry, INSERT…SELECT) + `DELETE /parts/{id}` refused with 409 when referenced by any order line (order_item.part_num_id), with Duplicate/Delete buttons on the Parts page. **Order copy/duplicate — done (#255)**: `POST /orders/{id}/copy` clones header + items + geometry. **Order-entry part picker — done (#265)**: the New-order line's Part # field autocompletes from the customer's parts (datalist) and, on selection, prefills alloy/sheet/gauge/pieces + tags the line with its part_num_id. Still TODO here: end-user change cascade (largely covered by order-edit's enduserId)
- [x] **H/M** Sector consistency validation; edge-trim tolerance gate + override + `f_add_system_log_tran` audit.
  **Both halves now DONE.**
  <br>**Sector (#—, 2026-08-20).** Legacy states the whole rule in one comment: *"Column sector must be
  populated, and sector for all items should be the same."* A missing sector is a hard error (400); a mix
  is a **question**, not an error — 409 `mixed-sectors` naming the sectors, clearing on `confirm: true`,
  because legacy's box is Yes/No defaulting to No. Measured on live data before porting the block, and it
  reconciles exactly (unlike the end-coil balance gate): sector became mandatory in **2017** and has been
  populated on **every** line since — 0 nulls in ~15,000 items over nine years — and a mix occurs on only
  **15 of 48,314** orders. Added `GET /lookups/sectors` (the domain had no endpoint) and an order-entry
  picker; a blank sector is deliberately NOT counted as a distinct value, so editing a pre-2017 order whose
  other lines predate the rule does not raise a spurious mix warning.
  <br>**The gate was already here and was wrong.** `AddEdgeTrimErrors` hardcoded the band as
  `1.5"–12"` — the value legacy falls back to when it *cannot read* `edge_trim_tolearance`. The live
  band on `.230` is **0.75"–12.00"**, so the shipped code demanded an override on every trim between
  0.75" and 1.5" the plant accepts. The band is now read from the table, for order items **and** part
  masters. (Note the table's spelling: `tolearance`. Correcting it raises ORA-00942.)
  <br>**Two behaviours added:** an override now writes `system_log` (legacy `f_add_system_log_tran`)
  naming who, how far out, and against which order; and a line coming **back inside** the band has its
  override **cleared** — without that, a line overridden once keeps the flag forever and the job sheet
  prints "CONTACT FOREMAN BEFORE RUNNING" in red on an item somebody already corrected.
  <br>**Still open:** the sector-consistency warning ("Unusual combination of sectors detected" — a mix
  of sectors in one order, Yes/No to continue).
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
  - [x] **Combi form — DONE (#355 + closed out #357).** `GET /documents/combi/{packingList}` renders the
    header plus all three detail sections (sheets / accumulated scrap / rejected coil), every weight in
    **lb and kg**, with a Print combi button on the Shipping page. The sheet grain is the **production
    item**, not the skid — one skid contributes a row per item.
    **The customer rule is carried, as configuration not a literal:** `Documents:TheoreticalWeightCustomers`
    (seeded `[2802]`). The rendered form STATES when the theoretical basis was used — legacy substitutes
    it silently, and a weight certificate that swapped basis without saying so would be indefensible.
    **Two facts found during the build that the sizing note did not have:**
    (a) the lb→kg factor in the combi DETAIL reports is **`0.45359`**, while `u_default_combi_1999*`
    in the same feature uses **`0.453592`** — legacy is internally inconsistent, and the detail
    reports' figure is the one the customer has been receiving, so that is what was carried;
    (b) there are `u_default_combi_1999_actual` and `_theo` **variant objects**, so "bill on
    theoretical weight" exists in legacy in TWO places — the SQL `CASE` on 2802 and a whole separate
    document object. **Which one the plant actually uses is unresolved** and should be settled before
    the per-customer layout variants are built.
    **The 16 layout variants do not need building — there are only 4 reachable objects, and the
    per-customer dimension is two flags (2026-08-04).** My earlier sizing counted `.srd` files in a
    folder rather than what the code selects. What legacy actually chooses at runtime:
    | selection | when | status |
    |-----------|------|--------|
    | `_display_t` | `customer_id = 2802` (Toyota Tsusho) | **built** — theoretical weight, as config |
    | `_display_pn` | `f_get_use_package_num_4shipment(...)` | **dormant** — see below |
    | `_display` | everything else | **built** |
    | `_input` | the on-screen form (`is_objectname`), not a print layout | n/a |
    The remaining **11** (`alcan`, `alcoa`, `alcoa_pn`, `kaiser`, `novelis`, `novelis_cd`, `twb`,
    `twb_cd`, `sm`, `input_twb`, `input_detail`) are **referenced nowhere in the vendored source** —
    library artifacts, never selected.
    **The package-number path is dormant on live `.230`:** the rule keys on `customer.use_package_num`
    (legacy made *this* one a proper customer flag, unlike the hardcoded 2802) and it is **NULL for all
    1,976 customers**, with **zero rows** in `sheet_skid_package`. So `_display_pn` is unreachable with
    today's data. Its function `f_get_use_package_num_4shipment` is not vendored, so the flag's exact
    semantics are inferred from the column, not read.
    Also resolves half the earlier `_actual`/`_theo` question: **both objects select the SAME print
    layout** (`_display`), so that distinction is about which weights are loaded, not presentation.
    Still open for the plant: whether any customer has joined or left the theoretical-weight
    arrangement since 2802 was hard-coded, and whether package numbers are ever intended to be turned
    on (the schema supports it; nothing uses it).
  - [x] ~~**Combi form — SIZED 2026-07-25, own session.**~~ 16 layout variants in `legacy/src/rpabco`
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
- [~] **H** Sketch image storage (`sketch_view` LONG RAW) — **read path done (#349)**:
  `GET /sketches/{id}/image` serves the stored BMP untouched, 404 when the sketch has no image.
  Live facts discovered on `.230`: **128 sketches, every one carrying an image**, uncompressed BMP,
  417,078 bytes each (one is 211,006), linked from `ab_job.sketch_id`. Legacy read it the same way
  (`SELECTBLOB sketch_view` → a `.bmp` file for a picture control, `w_da_sheet.srw:909`).
  Two constraints worth knowing before touching this: **LONG RAW cannot be aggregated or wrapped in a
  function** (`COUNT(sketch_view)` raises `ORA-00997`; `IS NOT NULL` is fine), and ODP.NET truncates a
  LONG **silently** — guarded by `SketchImageTests` asserting the BMP's self-declared length matches
  its byte count, a property the live images satisfy at both sizes.
  **Correction to this entry: there is no part linkage to build.** `sketch_id` exists only on `ab_job`
  — `part_num` and `order_item` have no sketch column on the live schema.
  **Display done (#350):** the Production folder shows the drawing for the loaded job, with its name
  and the job-specific `sketch_job_note`, and opens full size in a new tab.
  **Trap worth knowing before adding it anywhere else:** the image endpoint sits behind the same auth
  as everything under `/api`, and a browser **cannot** put `X-Api-Key` or a bearer on an `<img src>`
  request — a plain `src` gets a **401** and leaves a broken-image icon on a production screen. It must
  be fetched through `authFetch` and rendered from an object URL (revoked on replace; these are 417 KB
  each). The day-long `Cache-Control` still applies, since the fetch is an ordinary GET.
  Still TODO **and parity-complete without it**: image **upload is NOT a parity gap** — legacy ABIS
  *never writes sketches*. There is no `INSERT`/`UPDATE`/`UPDATEBLOB`/`DELETE` against the table
  anywhere in the vendored source, and all seven `sketch_view` references are `SELECTBLOB`. The earlier
  note here claiming "legacy imported BMPs" was wrong: the `.bmp` file legacy wrote was an **output**
  for a picture control to display, not an import. So the 128 live images arrived by some route outside
  the application, and **not having upload matches legacy**.
  Caveat on that: `w_sketch_viewer` is referenced (`w_stacker_job_details.srw:1790`) but **not
  vendored**, so the source is incomplete here. Its name says viewer, but it cannot be ruled out as a
  writer without seeing it.
  Open question for the plant before any upload is built: **how does a new sketch get into ABIS
  today?** If that needs a DBA, an upload screen is a real improvement — but it is new capability, not
  parity, and should be chosen deliberately.
  **DAS console render done (#352)** — a collapsed "Sketch" panel on the operator console, matching
  legacy showing the drawing there (`w_da_sheet.srw:909`). Collapsed by default because that console is
  dense and the running controls must not move; loaded fire-and-forget so a 417 KB bitmap never delays
  the coil/skid/scrap reads the operator is waiting on.
  The rendering now lives in one place — `clientapp/src/sketch.ts` — precisely so the `<img src>` 401
  trap above is not rediscovered per screen. It also revokes the previous object URL, which matters
  most on the kiosk: a console left open all shift would otherwise hold 417 KB for every job looked at.
  **Sketches are parity-complete.** What is left is upload, which is new capability, not parity.
- [~] **H** Die → shape mapping — done (#254): `GET/POST /line-die-shapes` + `DELETE /line-die-shapes/{shape}/{line}/{die}` over `LINE_DIE_4SHEET_TYPE` (composite PK), so scheduling can resolve the eligible line/die for a shape (filter by sheetType/lineNum/dieId; add guards line/die-exist + dup). Dies page gained a mapping panel. **Die report print — done (#353)**: `GET /documents/die-report`
  renders the legacy `d_die_print` report (opened from `w_report_die_tool`) with its columns exactly,
  including the two a DataWindow comment records as added in 2022 — `engineered_scrap_y_n` and
  `num_of_parts_per_hit` — plus a 🖨 button on the Dies page that carries the page's status filter
  through, as that window's filter did. Status prints as words; a printed `2` means nothing away from
  the screen. All 134 live dies fit one page, so it is not paged.
  **Note on the entry's wording:** this was listed as "die label/report print", but there is no per-die
  *label* in legacy — `d_die_print` is a list report of every die. Nothing else in `die_tool/` prints a
  single die.
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
  real corrections. Fixture gained `coil.cash_date`.
  **DELETE done (#318):** `DELETE /warehouse/skids/{n}` removes the links, items and skid, then
  **garbage-collects the status-20 shell** when nothing else references it (legacy `wf_coil_used_by_others`)
  — the shell exists only while something hangs off it, or warehousing leaves orphan coils forever.
  **Guard ADDED over legacy: only a status-20 coil is ever collected.** In this module the coil is always
  the shell so legacy never checked, but a path that deletes from `coil` is not somewhere to trust
  "can't happen" — a real coil would take everything hanging off it. `coilKeptReason` says why a coil
  survived. **UI wired (#319):** the Warehouse page gained a "Warehouse in a skid" form (job + customer coil #
  + lot, weights, ticket) and a Delete action on the selected skid; both surface the server's own reason
  rather than a bare status, and a weight warning is shown as "saved, but note…" so it can't read as a
  failure.
  **MODIFY done (#320):** `PUT /warehouse/skids/{n}` updates weights/pieces/date/status/provenance, and
  changing the customer coil number or lot **re-points the skid at a different shell** (minting one if
  needed) and collects the PREVIOUS shell when the move empties it.
  ⚠ **This deliberately does NOT reproduce a legacy bug.** Legacy's modify branch tests whether the
  ORIGINAL coil is orphaned (`wf_coil_used_by_others(wf_orig_item_coil_id(item), item)`) but then deletes
  **`ll_icoil` — the shell it just minted** — leaving `production_sheet_item.coil_abc_num` dangling and
  stranding the real orphan. The evident intent (collect the ORIGINAL) is implemented instead: reproducing
  a delete that removes the row the caller now depends on is data corruption, not a quirk worth keeping.
  **ITEM EDITOR done (#329) — this item is now COMPLETE.** `POST /warehouse/skids/{n}/items` (legacy
  action 2) and `DELETE /warehouse/skids/{n}/items/{item}` (action 3). The item carries its OWN
  customer coil number + lot, so one skid can hold material from several coils — each resolving or
  minting its own shell under the same cert/cash-date refusal. Adding may restate the skid header
  (legacy re-weighs on add); a total disagreeing with the item sum is a WARNING, since a weighed skid
  legitimately differs from the arithmetic. Removing the last item collects the shell but KEEPS the
  skid — an empty skid is re-stockable, and cascading would destroy a pallet's record over one
  corrected line. A coil that is not a status-20 shell is never collected, anywhere in this module.
- [x] **H** Coil-ownership transfer mint semantics — done (#224): mints a NEW `coil_abc_num` (status 2, from-cust set) + original → status 13; cert carries the new id
- [x] **H** Bulk "Change status → Ready for transfer" (status 12) — done (#240 `POST /coils/ready-for-transfer` with eligibility guards; #241 picker `readyOnly` filter + coil-ownership mark-ready UI)
- [~] **H** Scrap-skid + sheet-skid guarded DELETE done (#243). **Return-scrap done** (#XXX): POST /scrap-skids/{n}/return faithfully ports the live F_CONVERT_BACK_TO_SHEET proc — copies the scrapped mirror rows (scraped_sheet_skid/production_sheet_item/process_partial_skid/detail) back to the live tables, deletes the mirrors + scrap_skid(+detail) + credits back the linked return_scrap_item rows. **Sheet-skid modify + weight/piece reconciliation — done (#354)**: `PATCH /sheet-skids/{n}`
  ports legacy `w_office_skid_entry` CASE 4, setting the seven columns that UPDATE writes — net wt,
  tare, pieces, date, status, theoretical wt, on-hold reason. Two of those (`sheet_theoretical_wt`,
  `onhold_reason_code`) were absent from the model entirely. Correction panel on the Skids page.
  Every field is optional, applied with COALESCE. That is not just convenience: `sheet_net_wt` and
  `sheet_tare_wt` are **NOT NULL** on Oracle, so a partial update writing a null would raise
  `ORA-01400` rather than clearing the field.
  Totals are reconciled against the skid's items but **never corrected**, matching the warehouse paths:
  a weighed skid legitimately differs from the arithmetic, which is why legacy asks rather than
  silently reconciling. A skid with no items raises no warning.
  **Deliberately not ported:** legacy's CASE 4 also updates the selected `production_sheet_item` in the
  same transaction. Doing both from one call would make it impossible to fix a mis-keyed skid weight
  without also restating an item, and the item paths already exist separately.
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
- [x] **ZPL transport over TCP - done (#373), verified against the plant's two printers.**
  `TcpCoilLabelPrinter` sends raw ZPL and probes with a real connect (ICMP says the box is powered on,
  not that the print server is listening). Registered only when `LabelPrinters:Printers` is non-empty,
  so an unconfigured deployment keeps the NoOp and mints nothing.
  <br>**Port 9100, NOT legacy's 6101.** Measured 2026-08-05: the 6x10 (192.168.10.53) answers on both,
  the 4x6 (192.168.9.14) answers on **9100 only**. Hardcoding 6101 as the backlog said would have left
  the 4x6 permanently unreachable - and since reachability gates minting, receiving would have refused
  to mint. A per-printer `host:port` still overrides.
- [x] **H** **The 6x10 shipping label + cert label content** — BUILT and DEPLOYED (v0.8.2); the only
  thing outstanding is test prints, detailed at the end of this entry. The transport is ready and both printers
  answer; what is missing is the label BODY. `ZplLabels` currently holds only the ~2x1 inch coil ABC
  label, which fits neither stock, and the legacy layouts are PowerBuilder DataWindows that are **not
  vendored** - `u_default_barcode.sru` is the only file in `legacy/src/rpabco/`. Porting them needs
  either the DataWindow export or a sample of each printed label to work from.
  <br>**The rule is known** (`u_default_barcode.sru:619-631`): per skid, **2 shipping labels** - two
  separate `Print()` calls - and **1 cert label**, the cert only when `f_coil_cert_label_req` says the
  customer requires it. A `sleep_ms(f_get_ship_print_delay())` precedes every print (added 2019,
  "Ship_Print_Delay").
  <br>**Where the layout actually is.** 761 `.srd` DataWindows ARE vendored, including barcode ones
  (`da/d_report_coil_barcode_zebra*`, `coil_receiving/d_coil_barcode`, `inv_coil/d_report_coil_barcode`)
  - so start by checking whether one of those is the 6x10. The SHIPPING label is not among them:
  `u_default_barcode` prints through an inherited `idw_requestor`, assigned at runtime by its caller,
  and the ancestor lives in the **`silverdome*` / `aaaa` core libraries**, deliberately excluded from
  vendoring for size (~1.1 GB with binaries - see `legacy/src/README.md`). The same exclusion is why
  `f_suppress_barcode_print` and `f_print_cert_label` have call sites but no bodies here: only 26
  `.srf` are vendored.
  <br>**BODY WRITTEN (#375) and VERIFIED ON PAPER 2026-08-06.** `ShippingLabel6x10.Build` — 6x10in at
  300 dpi (`^PW1800`/`^LL3000`), thermal transfer, 8 Code 39 barcodes with AIAG identifiers, the
  numbered captions, and the metric variant. Print #4 came out correct in every respect after three
  defects found on paper across prints 1-3 (overprint, missing AIAG prefixes, barcode running into the
  address). **Scanner-verified the same day** — correct on paper and machine-readable.
  <br>**CORRECTED (#382): there is ONE layout, and print #4 was the wrong half of it.** Photographs of
  real Novelis output showed `7-LGTH./THEO.WT` rather than `7-GROSS WT`, size numbered 9 on three lines
  in mm, alloy numbered 10, and an `11-LOT NO.` table. I first read that as a second per-customer
  variant; the source says otherwise. Across all five barcode user objects `theo_t` is populated 15
  times and `gross_t` **zero**, with no object filling a dock field — the gross captions are dead
  artwork in a shared DataWindow. Two real defects came out of the same pass: weights are stored in
  POUNDS and must be multiplied by `0.45359` (the port had been relabelling only, a 2.2x overstatement),
  and field 7 is switched OFF by default (`ib_theo_on = FALSE`), which is why it was blank on paper.
  Full detail in **`docs/LABEL_6X10_NOVELIS.md`**.
  <br>**WIRED (#385).** `GET /documents/shipping-label/{skid}.zpl` renders without printing;
  `POST /documents/shipping-label/{skid}/print` is the plant's per-skid REPRINT; and
  `POST /shipments/{packingList}/print-labels` prints every skid on a shipment, two copies each. There
  is deliberately no shipment-level REPRINT — legacy's loop over the shipment's skids is commented out
  and replaced by a single-skid call, so that is not a feature to invent. No print-dialog step either:
  legacy's "click Print, then Print again" is `PrintSetup()`, and a socket needs no dialog.
  <br>**Config:** the shipping printer routes through `LabelPrinters:DeviceRouting:shipping-6x10`
  (env: `LabelPrinters__DeviceRouting__shipping-6x10=<printer name>`), NOT `LineRouting` — a shipping
  label is produced at the dock for a shipment, not at the line that made the skid.
  <br>**STILL NOT PRINTED in its corrected form.** Prints 1–4 were the old body. The lot table's column
  scale is derived rather than read (the sub-report uses different units from the outer label) and is
  the first thing to check on the next test print.
  <br>**The CERT is specified but NOT built** — see `docs/CERT_LABEL.md`. It needs the duplicate-863
  narrowing resolved first: 483 coils on `.230` have more than one 863 row and legacy errors on >1.
- [x] **H** ~~Rework the 6x10 as a per-customer VARIANT system~~ — **the premise was wrong** (#382).
  There is no variant system to build: the 6x10 has ONE layout. The artwork carries two caption sets
  under the same control names, but across all five barcode user objects `theo_t` is populated 15 times
  and `gross_t` ZERO, with no object filling a dock field — the gross captions are dead artwork, and the
  first port implemented the half nothing prints. Per-customer variation IS real, for the COIL scale
  label (`d_report_barcode_hayes`/`_johnstown`/`_ogihara`), which is a different document.
  <br>Two real defects came out of the same pass: weights are stored in POUNDS and must be multiplied by
  `0.45359` (the port had been relabelling only — a 2.2x overstatement on every skid), and field 7 is
  switched OFF by default (`ib_theo_on = FALSE`), which is why it was blank on the photographs.
- [x] **H** **The Certificate of Conformance** — done (#390, v0.8.2). `CertLabel6x10` +
  `GET/POST /documents/cert-label/{skid}`, one per coil, on the same 6x10 stock inline with the shipping
  labels. Geometry RECOVERED from `d_863_cert` + `d_863_cert_sub_chem` (`silverdome5.pbl`, units=0 at
  378/in), which confirms from the artwork the alternating 16-slot mechanical layout that had only been
  derived from photographs — while the chemical block keeps a FIXED 4x3 grid. Two opposite rules on one
  document.
  <br>**Both "blocked on" unknowns are answered.** Everything comes from `DBO.DATA_IN_863`, column
  `<code>_F_M2` — NOT `_F_M1`, which is populated 0 times in 11,696 rows — and `_M2` is pipe-delimited
  `value|YYYYMMDD`. Units come from `unit_of_measure.uom_abbrev` by the element's `_F_UOM` code (code 69
  is blank, which is why R Value prints unitless). Born Date = `cash_date`, ABC Serial =
  `coil.coil_abc_num`, Cntry of Cast = `coil.cntry_of_cast`, Spec = `order_item.spec`, ship-to = the
  DESTINATION customer's short name (the `cert_label_shipto_name` table is dead code, commented out per
  Novelis in 2019). Only the second Spec token is still unlocated.
  <br>**The 32-vs-3 gap resolved too:** `customer.coil_cert_label_req='Y'` on 32 customers while only 3
  have an element list. Legacy REFUSES for the rest — there is no default set — so the port refuses the
  same way rather than inventing one.
- [x] **M** **The pre-print 863 gate** — done (#390). `DATA_IN_863` is the certificate's only data
  source, so a coil with no inbound 863 cannot be certified. A refusal returns **409 with the reason**,
  never an empty 200 — "no certificates" and "this skid must not be certified" are different answers.
  <br>**The duplicate-863 blocker is solved.** 483 coils carry more than one 863 row and legacy treats
  more than one as an error, yet certificates print for them: 469 of the 483 are the SAME 863 received
  twice, differing only by `edi_file_id`, so `SELECT DISTINCT` over the certificate's own columns
  collapses them. The other 14 hold genuinely different measurements and still refuse.
  <br>**RECOVERED (#374).** `tools/pbl_extract.py` reads object source straight out of a `.pbl`, and the
  layout is now written up in `docs/LABEL_6X10_LAYOUT.md`: 59 controls with exact x/y/w/h, fonts, and
  the Code 39 barcode font that becomes ZPL `^B3`. Artwork is 5.11in x 9.64in on the 6x10 INCH stock
  (plant-confirmed).
  <br>**WHAT REMAINS FOR THE WHOLE LABEL SUBSYSTEM IS PAPER.** All four documents are built, tested and
  deployed (v0.8.2), but only the 6x10 shipping label has been printed — eight times, on the test
  printer `192.168.10.53`. **The two 4x6 tags and the certificate have never been printed at all.**
  Every defect worth finding in this batch was found by looking at physical output — rules struck
  through text, a clipped header, a barcode printing its value twice, stray dividers — and none of them
  failed a test first. The runs still owed:
  <br>&nbsp;&nbsp;1. `10.53` — a 6x10 and its certificate back to back, because the cert prints INLINE
  with the labels and that sequencing is the one thing a preview cannot show.
  <br>&nbsp;&nbsp;2. A 4x6 line printer — one skid tag for a **two-coil** skid, so the repeating detail
  band and its per-row underline both do something. A single-coil skid looks identical to the old
  broken single-row version. These are PRODUCTION printers on `192.168.9.x`; `tools/labelprint` refuses
  them without `--allow-production`.
- [ ] **DO NOT PORT: `SUPPRESS_BARCODE_PRINT` (86 rows).** It suppresses the **first** of the two
  shipping-label prints for a given (workstation MAC, customer, ship-to, user) - so a matching
  combination prints ONE label instead of two. It is keyed on **MAC address**, which is the tell: the
  problem it works around was the *machine*, not the data - certain PCs printed every job twice
  (the reported symptom was 4 shipping labels and 2 certs instead of 2 and 1).
  <br>The modern path prints **raw ZPL over a TCP socket**: no Windows spooler, no per-workstation
  driver, so the duplication it compensates for cannot occur. Porting it would give those users ONE
  label where the correct output is two.
  <br>Legacy itself has been backing it out: `u_default_barcode.sru:757` has the call **commented out
  and hardcoded `False`** since 2025-03-25 ("2341_Always_Reprint_2Labels") on the single-skid reprint
  path. It is still live on the bulk-shipment path at line 1505.

- [x] **H** Single-scan ABC mint — done (#312): `POST /receiving/scan/mint` draws the next
  `coil_abc_num` and stamps `inbound_coil_status`. **The printer is checked BEFORE anything is minted**
  (legacy pings first): unreachable → 503 and nothing minted, because an ABC number with no printed label
  leaves a coil untagged on the dock with nothing to reconcile it against. Unknown coil → 409, and no
  sequence value burned. Legacy's UPDATE is unscoped (`WHERE COIL_NUMBER = …`) so minting again
  OVERWRITES and orphans the earlier label — preserved faithfully, but `replacedAbcNum` reports it
  instead of it being silent.
- [~] **H** Lookup by scanned customer coil (`coil_org_num`); QR capture → `BARCODE_STRING` upsert.
  **QR capture DONE:** `POST/GET /receiving/scan/qr` stores and reads the mill's QR against an inbound
  coil (legacy `addqrcode`, `coil_receiving.pl:495`). The barcode goes through the SAME parse as the
  coil scan, so one gun read serves both.
  <br>**Legacy's three acceptance rules are ported verbatim** — payload longer than 67 chars, contains
  a `$`, coil number longer than 2 — because they are a shape test for the mill's own payload and
  nothing in the source explains where 67 came from. Loosening one on a guess lets a mis-scan be
  stored as a coil's certificate reference; tightening one rejects scans made every day.
  <br>Two deliberate departures: an over-long scan is **refused rather than truncated** (the column is
  `VARCHAR2(4000)`, and a cut-short QR string still looks like a code), and the refusal **names which
  rule failed** where legacy says only "Invalid QR Code" — an operator holding a scanner needs to know
  whether to rescan, reposition or call someone.
  <br>Legacy's UPDATE is scoped to the coil number alone and the same number can appear on several BOL
  lines; that is preserved, but `rowsUpdated` is returned so a scan that stamped four rows says four.
  <br>**BOTH HALVES NOW DONE — and the backlog line conflated two different stores.** Legacy keeps
  **two** QR stores, discovered by reading `w_qr_manual` after porting the CGI:
  <br>&nbsp;&nbsp;• `inbound_coil_status.barcode_string` — a **column** on the BOL line, written by the
  handheld CGI (`addqrcode`). **7,080** populated on `.230`.
  <br>&nbsp;&nbsp;• `barcode_string` — a **table** of (coil_org_num, barcode_string), written by the
  PowerBuilder desktop (`w_qr_manual`). **6,162** rows. Reached at
  `PUT/GET /coils/org/{coilOrgNum}/barcode`.
  <br>They are near-mirrors — **5,996 of the table's 6,162 coils also carry the column** — so code that
  writes one and reads the other looks correct on almost every coil and is wrong on the rest. They are
  kept as separate methods, each faithful to the path that owns it. Making the handheld write both
  would be a behaviour change and is the plant's call, not a refactor.
- [ ] **M** S-header strip+validate; coil-defect email notification
- *(Done: scan→verify→label handheld page + HTML coil label.)*

### C5. Quality
- [x] **C** 863 mechanical test-result WRITE (`PST_TEST_RESULT`) — done (#225 API + #226 Quality "Test results" page); the read-only list can finally populate
- [x] **C** Coil QA-hold workflow (status 11) + `COIL_TRACK_QA` audit + search/browse console — done (#227: qa-hold/qa-release/qa-history endpoints + Quality "QA hold" console)
- [x] **C** Dimension-check tolerance validation vs part-shape spec (nominal ± tol) — the real QC gate. **DONE via WinSPC.** WinSPC (the plant SPC system of record) owns the measured value + LSL/target/USL + pass/fail per characteristic, tied to ABIS by the "Job #"/"Coil #" tag. Phase 1: #229 read-only connector + #230 Dimensional QC page + #233 trend chart. Phase 2: #234 — `POST /coil-eval/skids/{n}/dimension-checks` validates the submitted measurements against WinSPC's authoritative LSL/USL and sets `in_spec` from that (falls back to the supplied flag when WinSPC has no data/disabled). Validated against live data (job 124346). The legacy `d_skid_dim_check` rule stays un-reconstructable but is moot. Live-wiring: abis_ro read-only SQL login on RSEDAM-PC (192.168.10.143,1433) + WinSpc:Enabled on ABIS.
- [ ] **H** Instron `.ASC` test-file import & parse (up to 9 samples). **Audited 2026-08-04: the
  "9 samples" is grounded** (`for i = 1 to 9`, five times in the source) — but the parser lives inside
  **`w_edi_863.srw`**, the EDI 863 window, and nowhere else. It is not a standalone quality import, so
  it is coupled to 863 — which is already deferred as data-blocked in §A. Listing it separately
  **double-counts the work**; do them together or not at all.
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
- [ ] **NOT PARITY — there is no data to view (audited 2026-08-04).** The legacy OPC-log module reads a
  table literally named **`opc_log`** (`d_opc_current_log`: `WHERE opc_log.active_status = 1`), and that
  table **does not exist** on live `.230` — not as a table, view or synonym, in any schema visible to
  the ABIS user. So a "viewer" would have nothing to read. The modern edge already serves live tag
  values from `/tags`, which is what the viewer existed to show. Building a collector is **new
  capability**, not a parity gap; decide it deliberately like quoting and sketch upload.
  ~~OPC-log collector + item-selection config; source/host/device tree~~
- [ ] **M** Step-up re-auth popup. **The in-DB job control half is misdirected (audited 2026-08-04):**
  live `.230` has **0 `DBMS_SCHEDULER` jobs**, so enable/disable/run-now would control nothing. The
  plant's scheduling is the **crontab on the DB host**, already inventoried in
  [[abis-230-cron-inventory]] and already surfaced read-only by the server-console cron card. Retarget
  or drop; do not build DBMS_SCHEDULER controls.

### §C audit, 2026-08-04 — how these entries went wrong

Three entries in a row (die "label" print, sketch "job/part linkage", the combi "16 layout variants")
described something legacy does not have. A mechanical check of every legacy identifier the backlog
cites found **32 of 32 real** (only `w_sheetpro`, already explained in `QUOTE_PRICING.md`) — so the
citations were never the problem. **The names were right and the scope was assumed.** A viewer was
assumed to have data, a report was assumed to be a label, files in a folder were assumed to be
reachable layouts.

The audit above found three more of the same shape. What catches them is cheap and worth doing before
building anything from this list: **check that the thing has data and that the code selects it**, not
just that it exists.

Verified sound in the same pass, so they are not re-checked: `BARCODE_STRING` (6,162 rows),
`PARTS`/`PARTS_SUPPLIERS` (762 each), `SYSTEM_LOG` (148,126 — the audit target; the backlog names the
function `f_add_system_log_tran`, whose body is not vendored), and the Instron "9 samples".

## D. Bug / robustness leftovers — **SWEEP CLOSED 2026-08-04**

> **The correctness sweep is closed.** Every cluster was worked to a verdict; what remains below is
> documented, mostly **L**, and several classes are now guarded in CI so they cannot regress silently
> (`OracleBindNameTests`, `OracleNotNullInsertTests`, `WriteEndpointGateTests`, `MaxIdTableTests`).
>
> **Do not mine this list further.** Its severity labels proved unreliable — two findings examined in
> depth were already handled (the RBAC "vulnerability" was guarded on both write paths; the
> omitted-NOT-NULL class was already clean), and one whole property it suggested (multi-statement
> writes without a transaction) dissolved on inspection into upserts and ternaries.
>
> **What actually found bugs was a method, not a list:** diff the port against the legacy PowerBuilder,
> then check the claim against live `.230`. Everything expensive came out that way — the 856 ASN
> overstating weight on ~91k multi-item skids, production throughput reporting rejected remnant instead
> of throughput, invoice offal omitting rebanded (the larger term), and the DAS Pull button weighing
> skids on the scrap scale against a mock device.
>
> **Carry that forward as a habit, not a backlog:** whenever a money path or a plant-floor path is
> built or touched, read the legacy source for it first and verify the result against live data. It is
> cheap at the time and expensive afterwards — there is **no user-feedback loop before 1.0**, so
> anything wrong ships into alpha and is found by a customer or an invoice.


- [x] **MAX+1 id minting: swept, no corruption risk, and now guarded** — done (#346).
  Most tables mint ids from an Oracle sequence (atomic). The **14** in `Database:MaxIdTables` use
  `SELECT COALESCE(MAX(id),0)+1` on Oracle too, which is a real race — two transactions can read the
  same MAX before either commits.
  **It is deliberate, and that rationale was not written down anywhere:** the legacy PowerBuilder app
  still writes 11 of those 14 and assigns ids the same way, so minting from a sequence in the modern
  stack would hand out ids legacy is about to reuse. Only `abis_truck_appointment`,
  `abis_scheduled_job` and `abis_job_run` are ABIS-owned and *could* take a sequence.
  **Live-verified on `.230`: all 14 have a primary key**, so a collision is a clean `ORA-00001` — a
  failed request, not two rows sharing an id. `MaxIdTableTests` now fails if a table is added to that
  list without one, which is the case that would turn this from a visible error into silent
  corruption.
- [~] **L** Residual: a concurrent create on any of those 14 tables. **Half done (branch
  `fix/duplicate-key-409`, PR open):** a PK collision now answers **409, not 500**, via a single
  `DuplicateKeyExceptionHandler` matched on the provider error NUMBER (ORA-00001 / SQLITE_CONSTRAINT),
  never on message text. A 500 said the server broke and the request may not be worth retrying — both
  wrong, since nothing was written and retrying verbatim will very likely succeed.
  <br>**Still open: the retry itself.** Retrying inside the request needs the mint+insert re-run as a
  unit across ~14 create paths.
  <br>**Also still open: an end-to-end test.** The obvious one (create a `security_user` twice) passes
  WITHOUT the handler, because that endpoint has its own duplicate guard (`ApiEndpoints.cs:4042`) —
  a green test that proves nothing. Proving the pipeline half needs a create path with no explicit
  guard that can be made to collide.

- [x] **H** **Invoice offal omitted rebanded weight — the larger half** — done (#345).
  `OffalWt` summed processed + scrap + **rejected** + unapplied − net. Legacy's `ll_rejnet`
  accumulates over BOTH lists — rejected (`process_coil_status` 3) **and** rebanded (7) —
  in `w_production_folder.srw:1176-1187`, which is why `d_rej_reband_coil_list_for_invoice`
  selects `IN (3,7)`. On live `.230` rebanded is much the larger term:

  | status | coils | billed weight |
  |--------|-------|---------------|
  | 3 rejected | 3,866 | 52,547,246 lb |
  | **7 rebanded** | **30,708** | **217,972,266 lb** |

  Offal is the **loss** figure, so dropping it hid loss. Note `w_invoice`'s own copy of the sum cannot
  be used as the reference — its two accumulation lines are commented out (`w_invoice.srw:425,430`),
  leaving `ll_rejnet` stubbed at 0, which is how the original port came to omit it.
  **Checked and found CLEAN in the same pass:** the four weight buckets (`net`/`unapplied`/`processed`/
  `scrap`) match `w_invoice` exactly; `RejectedWt` and `RebandedWt` are both reported as legacy does;
  and the `skid_sheet_status <> 6` guard is faithful to `w_e_car_folder:701` even though 6 is absent
  from the legacy legend and from all 618,174 live skids.

- [x] **M** **The floor board reported a failed read as "nothing running"** — done (#344).
  `fetchLineBoard` returned `[]` for a network failure, a 5xx and a 404 alike, and `Running` /
  `Stopped` / `Open shifts` are all derived from that. A request that never arrived rendered as
  **`Running 0 · Stopped 0 · Open shifts 0`** — a confident claim that the plant is idle, on a display
  read from across the floor. It now reports whether the read succeeded, those three KPIs show `—`
  when it did not, and a banner says so. Coil/skid counts come from the stacker board and keep their
  values rather than being blanked for a fault they did not have. Verified in a browser by failing
  only that one request: `1 / 0 / 1` → `— / — / —` + banner, recovering on the next 15s tick.

- [x] **H — REDEPLOY THE EDGE** — **verified DONE live 2026-08-09.** `GET /conveyor?line=4` on `.170`
  now answers `configured:false` with **no cells** for BL78, and `line=6` answers `configured:true`
  with BL110's real stacker tags. `/reading` reports `"simulated": true`. Both edge hosts are up on
  `:8090` and serving `/counters`, `/stacker`, `/run-state`, `/line-status`. The redeploy happened at
  some point between 2026-08-02 and now; this entry was stale.
  <br>**But the same live check found a DIFFERENT per-line leak** — see the next item.
- [x] **H — the DAS console showed BL110's production counters on every line** — found and fixed
  2026-08-09 while verifying the above. `/counters` and `/run-state` are **tag-parameterised, not
  line-parameterised**: they take `?good=&reject=&stroke=&feed=` / `?tag=`, and answer with the edge's
  CONFIGURED DEFAULTS when given none. Both plant hosts default to `PLC5-BL110`.
  <br>`fetchRunState`, `fetchPieceCount` and `fetchLineStatus` all passed tags. **`fetchCounters` did
  not** — so an operator on BL78 or BL84 read BL110's good/reject/stroke/feed, and the per-coil-run
  delta was computed from another line's counter. Display-only (`counterDelta` feeds only the render),
  so nothing wrong was ever saved.
  <br>Fixed by deriving the four tags from the line's run tag, whose PLC prefix already identifies the
  line (`PLC5-BL84.strokecnt` → `.goodpartcnt` / `.rejectpartcnt` / `.feedlength`). No tags → no query,
  which keeps the old default behaviour for a single-line box.
  <br>**Method note:** the first probe (`/counters?line=4`) looked like the SAME bug as the conveyor
  one, because it returned BL110 tags. It was not — `line` is simply not a parameter that endpoint
  accepts. Reading the endpoint's signature before reporting turned a false alarm into the real,
  narrower defect one layer up.

- [x] **C** **The plant edge was serving SIMULATED weights, and the DAS console saved them** — done (#340).
  `Edge:Scale:Provider` defaults to `Mock`, and both plant edge hosts configure only `Edge:Opc` (their
  skid scale is the OPC tag `ScaleSkidWt`, not a serial device). So `/reading` answered with MockScale's
  invented ~1234.5 lb and the console's Pull button wrote it into `sheet_net_wt`.
  **Confirmed live on .170 (2026-07-29):** `{"status":"ok","scale":"mock-scale"}` and
  `value 1234.7, stable False, mode GS, raw "US,GS,+1234.7 LB"`.
  Mitigating: the deployed app runs on the **non-prod .230 sandbox**, so no real invoice or ASN was
  built from a fabricated weight — this is a cutover blocker, not billing damage.
  `IScale.Simulated` is now part of the contract, `/reading` publishes `device` + `simulated`, the edge
  logs a startup warning, and the console refuses a simulated reading outright.
- [x] **H — the Pull button was wired to the WRONG SCALE (plant-confirmed)** — done (#341). Legacy split three ways and
  the modern console inverted it:
  | | legacy | modern today |
  |---|---|---|
  | finished skid net wt | **conveyor scale** (OPC `ScaleSkidWt`) → `update_sheet_skid_wt` | serial `/reading` ❌ |
  | scrap weight | serial scrap scale (`ib_scrap_scale_connected`) → `RETURN_ITEM_NET_WT` | typed by hand ❌ |
  | (floor scale, `w_scale_skid`) | reads **GROSS** → `net = gross − tare` | n/a |
  Plant confirmed 2026-07-29: "the scrap scale screen is for weighing the scrap, the conveyor scale is
  for measuring finished product skids." So Pull must read the **conveyor** scale — already fetched and
  merely *displayed* at `das-console.ts:938` — treated as **NET** (a bare stack has no pallet under it).
  To port with it: legacy's plausibility band (`ll_nw < 10 or > 39000` → "Invalid weight!!"), enabling
  it only where a stacker scale exists (BL110 only; BL84's tags read quality Bad), and legacy's
  positional precondition — it only read the scale with the stack at conveyor location **3 or 4**
  ("Stack not on Conveyor1!, Can not read scale."), which needs confirming against the belt as it
  stands since wrapper 2 was removed.
  Note the modern edge also **streams** readings where legacy **polled on demand** (`SioPutc('b')` =
  Print), which is why stability has to be checked now and did not then.

- [x] **M** **Write endpoints under an unmapped tag were authenticated but never feature-gated** — done (#339).
  The sweep flagged "PLC fault-code PUT/DELETE ungated". True, but it is not an endpoint-level slip: the
  `f_security_door` parity gate is applied by the `/api` group's endpoint filter, which looks the
  endpoint's **first tag** up in `FeatureByTag`. A tag nobody mapped means every write under it is
  writable by any user who can sign in — and it looks identical to a gated endpoint in review.
  **Audited all 154 write endpoints.** Three tags mapped cleanly to features that exist on live
  `SECURITY_APPLICATION` and are broadly granted: `Carriers` → Carrier Information (22 users),
  `Sketches` → Production Sketch (24), `Lookups` → Production Control (27). `WriteEndpointGateTests`
  now fails any new write whose tag is unmapped and unlisted.
- [ ] **M — needs a PLANT decision, not a code change: 5 tags still ungated on purpose.**
  Listed in `WriteEndpointGateTests.UngatedByDecision` so they are visible rather than invisible:
  - **DAS (12 endpoints)** — shift lifecycle, coil runs, change-job, reverse, line queue. The obvious
    mapping is Shift Control / Production Control, but **Shift Control is held by only 10 users on
    live**, so gating the shift lifecycle on it would stop every operator outside that ten from
    starting a shift. Confirm who should be allowed before gating; see [[abis-phantom-rbac-features]].
  - **Accounting (1)** — invoice creation. No obvious match among the 39 live features.
  - **Sales (3)** — quotes. Legacy splits into Quotation(Sheet) and Quotation(Circle); one tag cannot
    express both, so the endpoints likely need splitting or explicit gates.
  - **Trucks (5)** — a NEW ABIS feature replacing a spreadsheet; no legacy feature exists to map.
  - **Dies (4)** — plausibly Production Control, unverified against how the plant assigns it.
  Correctly ungated and NOT open questions: `/auth/login`, `/auth/change-password` (the caller's own),
  `/calculator/piece-weight` (computes, persists nothing), and `ScanLog` (append-only handheld telemetry
  on the API key, which bypasses the gate by rollout policy).

- [x] **H** **DAS scale pull ignored the reading's stable flag, unit and gross/net mode** — done (#338).
  The edge `/reading` returns the full `WeightReading` — `value`, `unit`, `stable`, `mode` — and the DAS
  console read only `value` and `unit`, displayed the unit, and dropped the rest. Each dropped field is
  a way to write a wrong weight into `sheet_net_wt`, which feeds **the invoice and the 856 ASN**:
  `stable=false` is the scale still settling (a number in motion, not a measurement); `mode="GS"` is a
  **gross** reading — it includes the skid tare — being written into the **net** field; `unit` is
  whatever the indicator is set to, so a KG reading stored as pounds is a 2.2× error.
  Now refuses rather than warns (a warning on an already-filled box gets dismissed), converts gross to
  net when the tare is known, and leaves manual entry available in every branch. A bare reading with no
  status prefix still parses as stable with a null mode, so the existing plant path is unchanged.
- [x] **M** **The web client now has a unit-test harness** — done (#359). vitest + jsdom,
  `npm --prefix clientapp test`, wired into CI beside the build. **30 tests** over the logic that was
  previously verified only by driving a browser by hand: `sketch.ts` (the `<img src>` 401 trap, the
  404-vs-failure split, object-URL revocation, escaping), `status-labels.ts` (line identity, the plant
  board order, decommissioned-vs-unlisted), and `edge.ts` (primary→fallback failover, the per-line
  scale tag, null-is-unknown-never-zero, unreadable conveyor cells).
- [x] **M** **The DAS console's weight rules are now testable — done (#362).** The guards deciding
  what lands in `sheet_net_wt` moved out of `pullWeight` into `clientapp/src/skid-weight.ts` as a pure
  decision, with **12 tests**. `pullWeight` keeps only the fetching and the DOM.
  Each rule is a way the button could otherwise record a number the scale never gave: the conveyor
  cell 3/4 precondition (an idle BL 110 reads `ScaleSkidWt = 0` with every cell clear — verified live,
  and without the guard that 0 becomes a skid's net weight), an unreadable cell counting as *not* on
  the scale, null staying unknown rather than becoming zero, the per-line scale tag so one line cannot
  read another's, and legacy's `10–39,000 lb` band at both bounds.
  The refusal ORDER is pinned too: with no stack *and* no reading, the operator is told "no stack yet"
  rather than "the scale did not answer", which would send them hunting a fault.

- [x] **Omitted-NOT-NULL-column class: swept, clean, and now guarded in CI** — done (#337).
  Live `.230` has **955** NOT NULL columns; **190** of them are nullable in `SqliteFixture`, so CI's DDL
  is far laxer than production and an `INSERT` omitting a required column would pass CI and raise
  `ORA-01400` on the plant floor. This class has bitten twice already (`ERROR_EVT.ERROR_USER` /
  `ERROR_TYPE_ID`, `sheet_tare_wt`), each found only by running against a real Oracle.
  **Swept the whole repository: zero app INSERTs omit a required column.** The scary version of this —
  a table whose every write is a guaranteed `ORA-01400` — does not exist. `OracleNotNullInsertTests`
  now locks that in against a committed schema snapshot (`oracle-not-null.tsv`), with no exemption list
  because there is nothing to exempt.
- [x] **CI's schema now matches Oracle's NOT NULL — done (#363).** All **190** columns that are NOT
  NULL on live are NOT NULL in `SqliteFixture`, so a write CI accepts is a write Oracle accepts. The
  60 failures this surfaced were mostly sloppy test seeds, as measured — but not only:
  **Two tests were asserting states production forbids**, and only passed because the fixture was
  laxer than the real schema. `Refuses_a_job_with_no_order_behind_it` seeded a job with no order, but
  `ab_job.order_abc_num` is NOT NULL *and* carries a composite FK to `order_item`; the guard's other
  path (job absent) is the reachable one and is what it now tests. `Unknown_packing_list_or_no_bill_of_lading`
  seeded `bill_of_lading = NULL`, also NOT NULL on Oracle — that half is removed.
- [x] **A bad request body is answered as a bad request — done (#364), and made to reach PRODUCTION
  (#370).** #364's finding was real but its *scope* was overstated, and verifying a deploy is what
  exposed that: `RouteHandlerOptions.ThrowOnBadRequest` defaults to **true in Development and false
  everywhere else**, so the 105 endpoints answering 500 were doing so only in the test/dev
  environment. In Production the framework already returned a bare 400 and never threw, so the
  handler never ran. Every test passed while the improvement did not exist on the deployed server.
  #370 turns the flag on in all environments and guards it with a test that runs the app in
  **Production**, which is the environment the original tests never exercised.
- [x] **The original #364 change.** The follow-up recorded above
  was wrong about *where* the hole was, and checking rather than building on it is what found the real
  one. `orig_customer_po`, `sheet_type` and both job refs are already required at the endpoints
  (`Validate(CustomerOrderWrite)`, `Validate(OrderItemWrite)`, `Validate(JobWrite)`); the tests that
  failed in #363 were repository-level, calling `UpdateOrderAsync` directly and bypassing that layer.
  Sweeping all 124 write endpoints instead of trusting the guess found a much larger defect:
  **105 of them answered a malformed body with a 500**, because minimal-API binding raises a
  `BadHttpRequestException` carrying its own 400 and nothing read it. Fixed with one exception handler
  and guarded by a sweep of the route table, so a new endpoint is covered the day it is added.
- [x] **DAS writes validated against live Oracle — done (#368), RUN and PASSED on .230 2026-08-05.**
  All 15 checks green: the three id sequences ahead of their table max, coil-run idempotence,
  `process_wt` = begin − end and floored at 0, the coil roll-through, reverse refusing a produced run,
  one job Running, the rate reset, `coil_status_from_line` surviving a drop, `dt_total` = 900 seconds,
  and `end_time` on the plant clock. Ran on line 1 against finished job 124342 / coil 234212; the board
  and coil were snapshotted and restored, and the sandbox verified clean afterwards.
  <br>It is a **.NET 8 console tool**, not the PowerShell script first written for it: these boxes have
  Windows PowerShell 5.1 (.NET Framework), which cannot `Add-Type` the .NET 8 ODP.NET at all. Re-run it
  any time after a Data Pump refresh — that is exactly when sequence drift appears.

- [x] **The DAS write paths now have coverage — done (#365).** The item above was misdiagnosed twice
  over. No seeded shift was needed: line 110 is *already* seeded running (shift 7701, job 1001, coil
  5001). The sweep was probing **line 4, which the fixture does not seed at all**, so all 15 endpoints
  404'd at `LineExistsAsync` and never reached a shift check. Correcting the id took the sweep to
  120/124.
  <br>That left the real gap, which the wrong diagnosis had been hiding: **every DAS write path had
  zero tests** — shift start/end, coil-run start/end, change-job, reverse, current-job, current-coil,
  queue. The line board had read tests and nothing exercised the mutations that fill it. 22 tests now
  hold the legacy rules a refactor would quietly drop: the cross-shift carry at both ends, `process_wt`
  floored so a heavy re-weigh cannot record a negative pass, `current_wt IS NULL` meaning "never run"
  rather than "spent", `dt_total` in seconds, and `end_time` on the plant clock rather than UTC.
  <br>The remaining 4 unreached endpoints answer 404 correctly: an empty body names no coil, no
  shipment item and no customer/route pair, so there is nothing to find.

- [x] **M** **Effective privilege now follows the signed-in identity** — done (#336). The sweep claimed
  the RBAC gate "unions grants across duplicate logins". Partly wrong: **both** modern write paths
  already reject a colliding login with a 409 (create and the rename, exclude-self), and live `.230`
  has **no** duplicates — so this was never exploitable through the app.
  What was real is that the two halves of the auth bridge disagreed. The signed-in identity resolves to
  the **lowest** `user_id` matching the login; the privilege lookup matched the login itself and took
  `MAX` over **every** row sharing it. `security_user` has no unique constraint on `login_id` on Oracle
  (its only constraints are the `user_id` PK and a NOT NULL check), the legacy application writes the
  same table, and the API guard is check-then-act — so a duplicate can arrive from outside. Privilege
  is now resolved for the same single user the identity resolves to, so the two cannot diverge.
  **Worth knowing:** the SQLite fixture declares `ux_security_user_login UNIQUE … COLLATE NOCASE` —
  a constraint **Oracle does not have**. CI is *stricter* than production here, which is the inverse of
  the usual trap and is why this went unseen. The test drops the index to reach the case the real
  schema permits.


- [x] **H** **"Processed wt" on the production reports was the remnant, not the throughput** — done (#335).
  `GetProductionSummaryAsync` and `GetLineEfficiencyAsync` reported `SUM(process_coil.process_end_wt)`
  as `ProcessedWt`. That column is the metal **left on** the coil at end of job: legacy
  `wf_rejected_coil_wt` substitutes `coil.net_wt_balance` for it when NULL, and the legacy DataWindows
  label it "End of Job WT". Only rejected and rebanded coils carry a remnant, so the reported figure
  tracked **rejected weight** — on live `.230` it is NULL or zero for **91%** of 183,776 rows and
  averages 1,570 against a 17,819 average coil net weight.
  Now ports legacy `w_production_folder.srw:1262` — `processed = coilnet − unprocessednet − rejnet`,
  as one shared `ProcessedWtPerJob` constant used by both roll-ups. Verified on live Oracle:

  | line | was | now |
  |------|-----|-----|
  | BL 24 | 0 | 93,951 |
  | BL 36 | 33,665 | 939,580 |
  | BL 78 | 606,477 | 41,506,349 |
  | BL 108 | 1,010,347 | 9,171,278 |
  | BL 110 | 559,729 | 3,341,667 |
  | BL 84 | 2,228,841 | 50,948,522 |

- [x] **M** Invoice-save duplicate: return **409** not a 500 — done (#260): `CreateInvoiceAsync` now catches the PK violation on the INSERT (the pre-check's TOCTOU race) and re-checks → returns Duplicate (409) instead of a 500.
- [x] **M** **`If-Match` optimistic concurrency was dormant — no client ever sent one** — done (#342).
  The old entry here ("push the version into the UPDATE `WHERE`") was doubly wrong: there is no version
  column (the ETag is a hash of the serialized entity), and the check-then-act race it named was
  unreachable, because **the client never sent `If-Match` at all** — zero occurrences of `etag` in
  client code. The server half was complete and tested the whole time; every edit was simply
  last-write-wins, so two people with the same job open silently overwrote each other.
  `authFetch` is the single choke point for all client HTTP (every page builds
  `new AbisClient('', { fetch: authFetch })`), so capture-and-replay wired there turns it on app-wide.
  A write with no stored validator sends nothing and behaves exactly as before.
- [x] **L→M** **`WithIfMatch`'s check-then-act window — closed (#360).** Read, compare and update are
  now one indivisible step per resource, keyed on the request path, so all **17 call sites across 16
  entities** are covered without touching any of them.
  **The earlier note here was wrong on the key point.** It said an in-process lock would be "false
  comfort" because legacy writes the same tables. Legacy *does* (12 write sites against `coil`, 9
  against `shipment`) — but **legacy never sends `If-Match`**. It overwrites unconditionally, so even a
  DB-level row lock would only *order* those writes, not prevent the loss. `If-Match` is a contract
  between ABIS clients, and that is the race that was reachable and is now closed.
  Measured before fixing: with the guard removed, 8 concurrent saves holding the same validator all
  succeeded — **4, then 5, then 8 winners across three runs**, each silently overwriting the last.
  **Two limits, in `ResourceLock`'s own docs and worth knowing:** it is per process (ABIS is a single
  systemd service, so it covers the deployment today — but scaling out silently breaks it, and would
  need a DB-level compare-and-swap first), and it does not defend against the legacy app, which nothing
  does.
- [x] **L** Invoice **tare** bucket — done (#260): `GetInvoiceComputationAsync` tare now excludes voided skids (`skid_sheet_status <> 6`) so it matches `SkidCount`.
- [x] **L** Stacker board — done (#260): `job_status NOT IN (0,3)` → `IN (1,2,4)` (matches its comment; robust to NULL/new codes).
- [x] **L** On-hand-coil + skid-count `IS NULL` guards — done (#260): `OnHandCoilPredicate` + every `skid_sheet_status <> 6` now guard NULL (`IS NULL OR …`).
- [x] **L→M** `pollPieceCount` stale count — **DONE.** Graded L as a display nit; it was not. The
  stale reading does not merely mislead the indicator, it **auto-fills the skid's piece count on
  save** (`skidPieces: typed ? … : (auto ?? undefined)`), and skid pieces reach the customer on a
  packing ticket and the 856 ASN and feed invoicing. A count minutes old could be written as a real
  one. `pieceCurrent` is now cleared on an unreachable edge.
  <br>A second bug found alongside it: the baseline advanced only `if (pieceCurrent != null)`, so a
  skid saved during an outage kept the OLD zero point and the **next** skid's delta spanned both,
  over-counting by roughly a whole skid — silently, in the direction that over-bills. The baseline now
  advances to whatever the counter reads, null included, which re-baselines on the next good read.
  <br>Rules extracted to `clientapp/src/piece-count.ts` with tests, following `skid-weight.ts`.
- [x] **L** Committed `wwwroot/…/generated/abis-client.js` drift — **CI-ENFORCED.** It had already
  drifted: the committed bundle was **218 lines behind** a fresh generation, missing `getCoilBalance`
  and `getSupervisorPinCoverage`, because a PR that ran only `npm run build` recompiled a stale
  `abis-client.ts`. Nothing broke — both callers use `authFetch` directly — but the typed client
  silently lacked methods its own API had.
  <br>CI already regenerated and rebuilt the bundle; it never checked the result. It does now, and the
  failure message carries the exact commands to fix it. The guard covers **all** of `wwwroot/ui/app`,
  not just the generated client, since the whole directory is served build output.

- [x] **M** **DAS floor board: plant card order + BL 60 hidden — done (#356).**
  The board ordered by `line_num`; the plant reads the floor as `BL 84, BL 78, BL 110, BL 108, BL 36,
  BL 24` (`line_num` 7, 4, 6, 5, 2, 1), and **BL 60 no longer exists** (user, 2026-08-04).
  Both live in configuration (`Board:LineOrder`, `Board:DecommissionedLines`) and ride to the client on
  `/lookups/lines`. The order is not numeric, alphabetical or activity-based, so a hardcoded sequence
  is what a later reader "corrects" into ascending without realising it meant something.
  **Two settings, not one, on purpose:** if a line vanished merely by being absent from `LineOrder`,
  forgetting to list a newly added line would hide it. An unplaced line sorts *after* the placed ones.
  `/lookups/lines` still returns **every** line, unfiltered — it answers "what does this `line_num`
  mean", which a job that ran on BL 60 still needs. Same split as `line_num = 0`: identity display
  keeps it, floor enumeration drops it.
- [x] **BL 60 fully decommissioned — done (#369).** Hiding it from the floor board was the easy half;
  nothing stopped new work being booked to it. The downtime form's line field is a free-text number,
  and the shift, downtime and job writes validated `line_num` not at all — "3" was accepted, and so
  was "999". Now a retired line refuses new work: no job, shift or downtime instance, and the DAS
  operations that assign work answer 409.
  <br>**Deliberately not blocked:** winding down. `shift/end`, `coil-run/end`, `reverse` and queue
  removal stay available, because BL 60 left **9 open shift rows and a queued job** behind on live and
  that state has to be clearable from the app. **History is untouched** — 1,163 jobs on live keep
  saying they ran on BL 60, the lookup still returns the line, and an existing record on it can still
  be edited (only a *change onto* a retired line is refused, since PUT is a full replace).
  <br>Verified `line_num 3 = BL 60` against the live LINE table before relying on it, and seeded it
  into the fixture so the guard is tested against the number production uses.

## E. Config / turn-on / deploy (user-gated, not code)
- [x] ~~Redeploy codi-ABIS to **v0.4.18**~~ — **long superseded.** `.110` runs **v0.9.1** as of 2026-08-19, verified end to end (version, health, ready, endpoints, and all four served UI bundles byte-identical to the tag).
- [ ] Wire BL110 piece-count tag per DAS station via the 🔎 picker (`stacker110.station1/2_stack_counter`)
- [ ] Enable `Notifications:EdiStall` **after** the data-source cutover (else false alarms on the frozen .230 ledger)
- [ ] Server-console restart button — decide on/off (polkit rule per `docs/SERVER_CONSOLE.md`)
- [ ] BL84 stacker piece-count — **parked ~6 months** (stacker out of service)
