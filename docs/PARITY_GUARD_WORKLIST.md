# Parity Guard Worklist (legacy → modern enforceable rules)

Source: the `parity-guard-discovery` workflow (2026-07-08) mined 21 vendored legacy
subsystems for enforceable business-rule guards, adversarially confirmed each against the
current API (293 gaps), and consolidated them into the 32 ranked items below. Each was
re-verified against the legacy source + current code before implementing.

**Branch:** `claude/parity-integrity-guards` (off `main` @ PR #82). Rhythm: verify each item
vs legacy + current code → implement → test → commit one at a time. Before pushing the batch,
regenerate the OpenAPI snapshot (`cd api && dotnet tool run swagger tofile --output
openapi.snapshot.json src/ABIS.Api/bin/Release/net8.0/ABIS.Api.dll v1`) and open a PR.

Status key: ✅ done · ⬜ todo (offline-now) · ⏸ deferred (needs live Oracle / unbuilt subsystem)

| # | St | Item | Effort/Risk | legacyRef |
|---|----|------|-------------|-----------|
| 1 | ✅ | Coil weight + org-number integrity on POST /coils (req netWt/width + orgNum≥4; default balance=COALESCE) | S/med | w_coil_detail_new:381-391; w_receiving_dock:351 |
| 2 | ✅ | PatchCoil 409 when coil_status ∈ Done(0)/Shipped(10)/Transferred(13) | S/med | w_inv_coil:391-404 |
| 7 | ✅ | Exclude voided skids (status 6) from invoice/folder/stacker skid counts | S/low | w_e_car_folder:701 |
| 3 | ✅ | login_id uniqueness on POST /security/users (case-insensitive COUNT + UNIQUE index) | S/med | w_user_new:111 |
| 4 | ✅ | Derive transfer_performed_by from ResolveLogin(ctx); server-assign coil_abc_num_new via NextId | S/low | w_coil_ownership_transfer:232,530 |
| 5 | ✅ | Warehouse skid PATCH: block shipped(status 0=GONE)/reassigned(has process_partial_skid); status enum {0,1,2,3,4,8,9,10,11} | S/med | w_inv_skid:1096/1083/199; w_wh_business:254/169 |
| 6 | ✅ | PatchJobAsync: block modify when job_status=0 (Done) | S/med | w_stacker_job_details:498 |
| 8 | ⏸ | Validate(OrderItemWrite): require positive theoreticalUnitWt + maxSkidWt; require sector (reject null/-99) — RESHAPED: belongs at order-FINALIZE not item-create (breaks the incremental contract); deferred | S/low | w_stacker_job_details:291/295; w_order_entry:480/561 |
| 9 | ✅ | Grant privilege ∈ {0,1}; default new-user user_status=1 server-side; Validate(SecurityUserWrite) require a name | S/low | d_user_app:9; w_user_new:330/120 |
| 10 | ✅ | Validate(PartWrite): lift the edge-trim block from OrderItemWrite (shared AddEdgeTrimErrors). sheetType/enduserId/gauge/sector deferred (shared full-replace PUT + incremental build) | S/low | w_part_num_new:429/435/441/465/485/497 |
| 11 | ✅ | Validate(SheetSkidWrite): bound tare 0..8000, net 0..30000 (non-zero net); CreateSheetSkid default status=8 | S/low | w_stacker_skid_edit:88/93/514; w_office_skid_entry:2455; w_wh_business:1483/809 |
| 12 | ✅ | Reject duplicate coils on POST /coils by (org,customer,mid) → 409 (CoilExistsByKeyAsync). In-batch (BOL lines) + soft-warn override flags deferred to the mint flow | M/med | w_receiving_dock:494/505; w_inv_coil:712/724/1284 |
| 13 | ⏸ | coil_status enum validation + server-assign create default — NEEDS live Oracle (mint uses 11) | S/med | d_coil_track_qa:10; w_inv_coil:2969 |
| 14 | ✅ | Reject minting a BOL with zero coil lines (400, not 200 Minted=0) | S/low | w_coil_receiving:367 |
| 15 | ✅ | Dimension-check absolute bounds (gauge≤1, width 5..199, square 0..9, len 1..999, PC 1..99). pc_number=MAX+1 derivation deferred | S/low | u_tabpg_skid_dim_check:103/974 |
| 16 | ✅ | Scrap-skid net wt > 0 (+ non-negative tare). Item net-wt + scrap_net=SUM(return_scrap_item) rollup deferred (no return_scrap_item write endpoint yet) | S/low | w_office_skid_entry:5413/5445/5551 |
| 17 | ✅ | Validate(ShiftWrite): require start; endTime < startTime → 400 (end stays optional = open shift). end-shift sign_time sub-rule deferred (no field) | S/low | w_shift_info_new:130; w_daily_production:197 |
| 18 | ✅ | Shift uniqueness per (line, schedule_type, day) → POST /shifts 409 (ShiftExistsAsync, portable [day..next-day) range; Oracle DateTime binding to confirm in the write sweep) | M/med | w_daily_production_modify_schedule:543 |
| 19 | ✅ | Cash-date format+range on receiving coil (MMDDYYYY; mm 1..12, dd 1..31, yr [cur-2,cur]). Part B conditional-required per customer.cash_date_required deferred | M/low | w_coil_receiving:2869/2841; w_coil_detail_new:93/110 |
| 20 | ✅ | Harden Validate(DieWrite): Y/N flag, Max(owner,32), whole-number gross_weight. numOfPartsPerHit/status/location enums deferred (live Oracle) | S/low | w_die_new:88/98; d_die_new:10/14/17 |
| 21 | ✅ | prod-folder note: require existing job (GetJobAsync → 404) before insert; author already resolved from principal | S/low | w_e_car_folder:537/557 |
| 22 | ✅ | Validate(JobWrite): require order refs + positive material_yield; POST /sheet-skids rejects a job w/o order. new-job status default deferred (live Oracle) | M/med | w_stacker_job_details:491/273/1272; w_wh_business:831 |
| 23 | ✅ | Null-out trim fields when trimming!='Y' + derive pieces_skid=Int(max_skid_wt/theo_wt) when unset (shared ITrimNormalizable, parts+order-items) | S/low | w_order_entry:1152; w_part_num_new:562 |
| 24 | ⏸ | Cert-label completeness when customer.coil_cert_label_req; heavy-gauge/small-OD advisory — DEFERRED: part 1 is customer-flag-conditional (like 19 Part B) and part 2 is a soft ADVISORY, not a hard 400 (no warnings channel yet) | M/low | w_order_entry:1832/2972; w_coil_detail_new:393 |
| 25 | ✅ | Trimmed-width override user stamped from the principal (StampTrimOverrideUser on part/order-item POST+PUT). Tolerance config/band + system_log audit deferred (needs a system_log write path) | M/med | w_order_entry:611-632; w_edge_trim_tolearance:161/165 |
| 26 | ⏸ | Model order_coil table + order-entry coil-assignment guards — whole unbuilt subsystem | L/high | w_order_entry:956/1126/3049/… |
| 27 | ⏸ | Suggested piece-weight by shape/alloy density — needs METAL_DENSITY table (live Oracle) | M/med | w_order_entry:694-820 |
| 28 | ⏸ | End-Coil / coil-eval weight subsystem (yield, H/L%, reject/reband) — needs live Oracle weight cols | L/high | u_tabpg_end_coil:* |
| 29 | ⏸ | Recovery scrap-worksheet subsystem — needs live Oracle f_get_coil_*_wt | L/high | w_recovery:763/800/… |
| 30 | ⏸ | Sheet-skid/scrap item CRUD + weigh/reconcile — needs live Oracle (production_sheet_item, sheet_skid_detail) | L/high | w_office_skid_entry:788/861/… |
| 31 | ⏸ | Inbound-EDI receiving + coil_track/coil_quality + status transitions — needs live Oracle; NEVER fire legacy EDI | L/high | w_receiving_dock:307/373/… |
| 32 | ⏸ | Daily-prod scheduler + shift-coil/job + downtime/maintenance derivations — all live-Oracle | L/high | w_daily_prod_new_schedule:184-273 |

**Recommended remaining order:** 6 (finished-job guard, codes verified job 0=Done), 4 (transfer provenance, low-risk), 3+9 (security), 10+20 (part/die validation), 8+11 (order-item/skid bounds), 14 (empty-mint), 15 (dim bounds), then the M-effort ones (12,19,22,24,25). Verify skid status 0=GONE (rank 5) and the SheetSkidWrite bounds against legacy before enforcing.

---

**Progress (2026-07-08).** Landed on `main` via PR #83: ranks 1,2,3,4,5,6,7,9,11,14,15.
On PR #84 (`claude/parity-guards-2`, CI green): ranks **10,12,16,17,18,19,20,21,22,23,25**
— every offline-verifiable guard is now landed. **Only 24 left offline** and it's
**deferred** (part 1 customer-flag-conditional like 19 Part B; part 2 is a soft advisory,
not a hard 400 — no warnings channel yet). **Deferred (live Oracle / bigger):** 8
(order-finalize), 13, 24, and the sub-parts noted per row above; ranks 26-32 are unbuilt
subsystems. When the plant network is steady, run the live-Oracle write sweep to confirm
the deferred value-set enums + the shift/date DateTime binding (rank 18).

**Snapshot regen — two gotchas that bit this batch:** (1) always `rm -rf
src/ABIS.Api/bin/Release src/ABIS.Api/obj/Release` before `swagger tofile` — an
incremental Release build can silently emit a stale contract; (2) run the `git diff --
api/openapi.snapshot.json` verification from the **repo root**, not `api/` (a wrong
relative path shows a falsely-empty diff). Commit the regenerated snapshot; never restore
it on a real contract change. CI's "Verify OpenAPI matches the committed snapshot" is the
backstop.
