// ABIS shared status/type code → human-label + severity lookup (#4 — "statuses show as raw
// numbers; the words those numbers represent would be drastically better").
//
// Every code→label pair below is AUTHORITATIVE — ported from the legacy PowerBuilder DataWindow
// dddw/DropDownListBox value lists and CHOOSE CASE code (source file:line cited per domain). We do
// NOT invent labels: any code with no legacy mapping falls through to the raw value in a neutral
// chip. Still left raw pending the plant's definitive list (no discoverable legacy value list):
// vehicle (shipment.vehicle_status), sketch, receiving-BOL, and carrier status/type, plus OPC-DA
// quality (opc_log_details.quality — likely 192 Good/64 Uncertain/0 Bad, but unconfirmed). Not codes
// at all (already human text): line-error type (DB-joined error_type label) and maint-log status.
//
// Labels are lightly humanized from the legacy camelCase (InProcess → "In process", QAOnHold →
// "QA on hold"); the original numeric code is preserved in each chip's title tooltip so operators
// used to the numbers can still see them.
//
// Compiled by tsc → wwwroot/ui/app/status-labels.js; imported by the page modules.
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
// ab_job.job_status — code table AB_JOB_STATUS_DESC (verified live, docs/ORACLE_VALIDATION.md:234;
// ddlb d_stacker_job_detail.srd:10). The w_production_folder report CHOOSE CASE (4=Canceled/
// 5=OnHold) is the known-buggy outlier and is deliberately NOT used.
const jobStatus = {
    0: { label: 'Done', tone: 'ok' },
    1: { label: 'In process', tone: 'info' },
    2: { label: 'New', tone: 'mut' },
    3: { label: 'Cancelled', tone: 'crit' },
    4: { label: 'On hold', tone: 'warn' },
};
// coil.coil_status — code table COIL_STATUS. 0–11 from ddlb (d_coil_detail_display_qa.srd:12,
// d_coil_abc_list_by_bol_printing.srd:12); 10=Shipped/13=Transferred/12=Ready-for-transfer from
// PARITY_GUARD_WORKLIST.md:18 + w_inv_coil.srw:1680; 20=WH item from w_wh_business.srw:849.
// (code 9 unused; code 14 = a Novelis reject flow whose label is undocumented → left raw.)
const coilStatus = {
    0: { label: 'Done', tone: 'ok' },
    1: { label: 'In process', tone: 'info' },
    2: { label: 'New', tone: 'mut' },
    3: { label: 'Rejected', tone: 'crit' },
    4: { label: 'On hold', tone: 'warn' },
    5: { label: 'In transit', tone: 'info' },
    6: { label: 'Return', tone: 'warn' },
    7: { label: 'Rebanded', tone: 'warn' },
    8: { label: 'Retry', tone: 'warn' },
    10: { label: 'Shipped', tone: 'ok' },
    11: { label: 'QA on hold', tone: 'warn' },
    12: { label: 'Ready for transfer', tone: 'info' },
    13: { label: 'Transferred', tone: 'ok' },
    20: { label: 'Warehouse item', tone: 'mut' },
};
// sheet_skid.skid_sheet_status — dddw d_skid_status_group_edit.srd:10 / ddlb d_sales_skid_list.srd:13;
// w_office_skid_entry.srw:4623 confirms 7=Partial, 13=Partial re-band.
const skidSheetStatus = {
    0: { label: 'Gone', tone: 'mut' },
    1: { label: 'Process', tone: 'info' },
    2: { label: 'Ready', tone: 'ok' },
    3: { label: 'Cancel', tone: 'crit' },
    4: { label: 'On hold', tone: 'warn' },
    5: { label: 'Pre-recap', tone: 'warn' },
    7: { label: 'Partial', tone: 'warn' },
    12: { label: 'Sample', tone: 'info' },
    13: { label: 'Partial re-band', tone: 'warn' },
};
// scrap_skid.skid_scrap_status — code table SCRAP_SKID_STATUS_DESC; ddlb d_sales_scrap_skid_list.srd:14
// (0–4) + d_office_entry_report_scrap_list.srd:14 (5 Pre-recap, 6 Pallet only).
const skidScrapStatus = {
    0: { label: 'Gone', tone: 'mut' },
    1: { label: 'In process', tone: 'info' },
    2: { label: 'Ready', tone: 'ok' },
    3: { label: 'Cancelled', tone: 'crit' },
    4: { label: 'On hold', tone: 'warn' },
    5: { label: 'Pre-recap', tone: 'warn' },
    6: { label: 'Pallet only', tone: 'info' },
};
// scrap_skid.scrap_type — the full SCRAP_TYPE_DESC code table (verified live on .230, 2026-07-11:
// 1–11). 1–8 also match ddlb d_acct_scrap_type_list.srd:8 + CHOOSE CASE w_invoice.srw:330-347; 9–11
// (Scrap Credit / Full Sheet / Cut Out) come straight from SCRAP_TYPE_DESC — the dashboard "Scrap by
// type" groups on this and showed code 11 (Cut Out). Distinct from the 49-row scrap defect catalog.
const scrapType = {
    1: { label: 'Rej. Sheet-Mill', tone: 'mut' },
    2: { label: 'Accu. Scrap', tone: 'mut' },
    3: { label: 'Others', tone: 'mut' },
    4: { label: 'Trailer', tone: 'mut' },
    5: { label: 'Rej. Sheet-Process', tone: 'mut' },
    6: { label: 'Sample', tone: 'mut' },
    7: { label: 'Tote', tone: 'mut' },
    8: { label: 'Edge Trim', tone: 'mut' },
    9: { label: 'Scrap Credit', tone: 'mut' },
    10: { label: 'Full Sheet', tone: 'mut' },
    11: { label: 'Cut Out', tone: 'mut' },
};
// die.status — ddlb d_die_new.srd:10 / d_die_display.srd:10.
const dieStatus = {
    0: { label: 'Obsolete', tone: 'warn' },
    1: { label: 'Active', tone: 'ok' },
    2: { label: 'Gone', tone: 'mut' },
};
// part_num.item_status — ddlb d_pn_item_detail_new.srd:27,119 (values "Active 1 / Obsoleted 0").
const partItemStatus = {
    0: { label: 'Obsoleted', tone: 'warn' },
    1: { label: 'Active', tone: 'ok' },
};
// shift.shift_data_status — a shift-data CERTIFICATION flag (the operator signed off the shift's data),
// not a rich status list. d_end_shift_operator_signing_sheet.srd:11,28 (checkbox on=1/off=0) +
// u_tabpg_print_shift_summary.sru:51 (blocks printing unless shift_data_status = 1).
const shiftDataStatus = {
    0: { label: 'Not certified', tone: 'mut' },
    1: { label: 'Certified', tone: 'ok' },
};
// security_user.user_status — ABIS-owned table (the legacy ERP had no app users → no legacy value list);
// the convention is the admin create form's own (default 1; hint "0 = inactive"). 1 active / 0 inactive.
const userStatus = {
    0: { label: 'Inactive', tone: 'mut' },
    1: { label: 'Active', tone: 'ok' },
};
// abis_truck_appointment.truck_status — the plant's Excel "location status" legend
// (TRUCK APPTS_TEMPLATE): the shaded truck-company field.
const truckStatus = {
    0: { label: 'Pending arrival', tone: 'mut' },
    1: { label: 'Running late', tone: 'warn' },
    2: { label: 'Parked out back', tone: 'info' },
    3: { label: 'Sent to Bldg 1', tone: 'info' },
    4: { label: 'Sent to Bldg 2', tone: 'info' },
    5: { label: 'Sent to Bldg 3', tone: 'info' },
    6: { label: 'Signed out / gone', tone: 'ok' },
    9: { label: 'Cancelled', tone: 'crit' },
};
// shipment.shipment_status — the plant's dispatch legend (live value set {0,2,3,4}), CONFIRMED by the
// user 2026-07-10. 0 = Shipped/closed (the legacy EDI 856 ASN generator fires on shipment_status = 0,
// f_edi_alcan_gm_856.srf:294; the guided close action keys off 0 = ClosedShipmentStatus). Mirror any
// legend change here + in AbisRepository.cs.
const shipmentStatus = {
    0: { label: 'Shipped', tone: 'ok' },
    2: { label: 'Staged', tone: 'info' },
    3: { label: 'Open', tone: 'mut' },
    4: { label: 'On hold', tone: 'warn' },
};
// pst_test_result.test_type — the mechanical test-report test type (NUMBER, live values 1–5). Authoritative
// labels ported from the legacy EDI-863 send window's radio buttons (w_edi_863.srw:752-767, which stamp
// test_type 1–5). Shown as plain text (statusText), not a severity chip — these are categories, not states.
const testType = {
    1: { label: 'First Test', tone: 'info' },
    2: { label: 'First Test Redo', tone: 'warn' },
    3: { label: 'First Full Test', tone: 'info' },
    4: { label: 'Full Test', tone: 'ok' },
    5: { label: 'Full Test w/o N/R', tone: 'mut' },
};
// process_coil.process_coil_status shares the coil_status code space (accounting.js:19 confirms
// 3=Rejected/7=Rebanded; w_invoice.srw:250/274). receiving coil lines are stamped 2=New / 11=QA
// on hold (receiving.ts mint), also the coil_status space — so both reuse the coilStatus map.
// The stacker conveyor path: where a finished stack is between the stacker head and the end of the
// wrapper line. Ported VERBATIM from the legacy board's value list (d_conveyor_skid.srd:12) — these
// 19 codes are exactly the 19 LINE_CURRENT_STATUS.SHEET_SKID_LOCATION_0..18 columns, i.e. the columns
// ARE the stations of this path, which is why the board can be rendered straight from the line board.
const stackLocation = {
    0: { label: 'Stack complete', tone: 'ok' },
    1: { label: 'Leaving lift table', tone: 'info' },
    2: { label: 'Entering conveyor 1', tone: 'info' },
    3: { label: 'On conveyor 1', tone: 'info' },
    4: { label: 'Leaving conveyor 1', tone: 'info' },
    5: { label: 'On conveyor 2', tone: 'info' },
    6: { label: 'Leaving conveyor 2', tone: 'info' },
    7: { label: 'On conveyor 3', tone: 'info' },
    8: { label: 'At centre conveyor 3', tone: 'info' },
    9: { label: 'Entering wrapper 1', tone: 'info' },
    10: { label: 'Leaving wrapper 1', tone: 'info' },
    11: { label: 'On WP1 unload conveyor', tone: 'info' },
    12: { label: 'At centre WP1 unload', tone: 'info' },
    13: { label: 'Overhead crane', tone: 'warn' },
    14: { label: 'On WP2 load conveyor', tone: 'info' },
    15: { label: 'Entering wrapper 2', tone: 'info' },
    16: { label: 'Leaving wrapper 2', tone: 'info' },
    17: { label: 'On WP2 unload conveyor', tone: 'info' },
    18: { label: 'At end WP2 unload', tone: 'ok' },
};
// The physical zone a stack sits in (legacy d_conveyor_skid.srd:13). Note 0 = the stacker conveyor
// itself, so the codes are NOT a prefix of the location codes above — they are a coarser grouping.
const stackConveyor = {
    0: { label: 'Stacker conveyor', tone: 'info' },
    1: { label: 'Idle conveyor 1', tone: 'mut' },
    2: { label: 'Idle conveyor 2', tone: 'mut' },
    3: { label: 'Idle conveyor 3', tone: 'mut' },
    4: { label: 'Wrapper 1 unload', tone: 'info' },
    5: { label: 'Wrapper 2 load', tone: 'info' },
    6: { label: 'Wrapper 2 unload', tone: 'info' },
    7: { label: 'Overhead crane', tone: 'warn' },
};
/** Stations that no longer physically exist. **WRAPPER 2 HAS BEEN REMOVED FROM THE PLANT** (confirmed
 * by the user 2026-07-25): the belt now runs stacker → wrapper 1 → output, so locations 14–18 (WP2 load
 * conveyor, entering/leaving wrapper 2, WP2 unload conveyor, end of WP2 unload) are dead track. They stay
 * in {@link stackLocation} so a HISTORICAL row carrying one of those codes still decodes to its real
 * meaning — we only drop them from the live path, which should show the line as it is today. */
const REMOVED_STATIONS = new Set([14, 15, 16, 17, 18]);
/** The stacker conveyor path in order — the stations a stack passes through that still exist, matching
 * the LINE_CURRENT_STATUS.SHEET_SKID_LOCATION_n columns the line board exposes as slots "0".."18".
 * Wrapper 2's stations are omitted (see {@link REMOVED_STATIONS}), so the board shows the real line
 * rather than legacy's 19 — that is an intentional divergence from the legacy board, not an omission. */
export const STACK_PATH = Array.from({ length: 19 }, (_, i) => i)
    .filter((i) => !REMOVED_STATIONS.has(i))
    .map((i) => ({ slot: String(i), label: stackLocation[i].label }));
export const STATUS_MAPS = {
    jobStatus,
    coilStatus,
    processCoilStatus: coilStatus,
    receivingCoilStatus: coilStatus,
    skidSheetStatus,
    skidScrapStatus,
    scrapType,
    dieStatus,
    partItemStatus,
    shiftDataStatus,
    userStatus,
    truckStatus,
    shipmentStatus,
    testType,
    stackLocation,
    stackConveyor,
};
// The plant's real line names (BL78, BL84, BL110, BL108, BL60) live in the LINE table (line_num →
// line_desc) and are NOT derivable from the number: `line_num` is an internal code (e.g. 4, 7), so the
// old `BL${num}` guess rendered bogus BL4 / BL7. Callers load this map once from GET /api/lookups/lines
// (the shell + the standalone DAS kiosks do); lineLabel then resolves the real name, falling back to the
// raw code only when the map isn't loaded or the line isn't in it.
let lineNames = new Map();
// The floor board's view of each line, carried on the same lookup as the names: where the plant wants
// the card, and whether the line still exists on the floor. Kept beside the names because every page
// that needs one already loads the other.
let lineBoardOrder = new Map();
let decommissionedLines = new Set();
/** Populate the line_num → name map from ProductionLine rows (GET /api/lookups/lines). */
export function setLineNames(lines) {
    const m = new Map();
    const ord = new Map();
    const gone = new Set();
    for (const l of lines ?? []) {
        const n = Number(l.lineNum);
        if (!Number.isFinite(n))
            continue;
        const name = (l.lineDesc ?? '').trim();
        if (name)
            m.set(n, name);
        if (l.displayOrder != null)
            ord.set(n, Number(l.displayOrder));
        if (l.decommissioned)
            gone.add(n);
    }
    lineNames = m;
    lineBoardOrder = ord;
    decommissionedLines = gone;
}
/**
 * Sort key for the floor board. The plant's order is not numeric, alphabetical or activity-based, so
 * it comes from configuration; a line the plant has not placed sorts AFTER the placed ones rather
 * than vanishing, because a new line must never become invisible through an omission.
 */
export function lineBoardRank(lineNum) {
    const i = lineBoardOrder.get(lineNum);
    return i == null ? Number.MAX_SAFE_INTEGER : i;
}
/**
 * True when the line no longer exists on the floor (BL 60 as of 2026-08-04).
 *
 * Use this ONLY where the question is "what is running now". A decommissioned line keeps every job it
 * ever ran, so filtering it out of reporting would silently restate the past — the same split already
 * applied to `line_num = 0`: identity display keeps it, floor enumeration drops it.
 */
export function isDecommissionedLine(lineNum) {
    return decommissionedLines.has(lineNum);
}
/** Best-effort: fetch the LINE lookup and populate the map. Pass authFetch (kept auth-agnostic here). */
export async function loadLineNames(fetchFn) {
    try {
        const r = await fetchFn('/api/lookups/lines');
        if (r.ok)
            setLineNames(await r.json());
    }
    catch { /* best-effort — lineLabel falls back to the raw code */ }
}
/**
 * `line_num = 0` is the plant's **"no line assigned" sentinel**, not a press. The LINE table carries it
 * as `line_desc = 'NONE'` alongside the seven real lines (BL 24/36/60/78/84/108/110), and ~1,300 jobs
 * that were never put on a line point at it.
 *
 * Keep showing it wherever a single record's line is displayed — on a job row, "NONE" is the truthful
 * answer to "which line?". Exclude it anywhere the question is "what is on the floor", because there
 * is no BL 0 to walk up to, and its ~1,300 historical jobs otherwise swamp the real lines' counts.
 */
export const NO_LINE_NUM = 0;
/** Is this a real production line (a press on the floor), rather than the "no line" sentinel? */
export function isProductionLine(lineNum) {
    const n = Number(lineNum);
    return Number.isFinite(n) && n !== NO_LINE_NUM;
}
/** A production line's real display name (from the LINE table), or the raw code if unmapped. */
export function lineLabel(lineNum) {
    if (lineNum == null || lineNum === '')
        return '—';
    const n = Number(lineNum);
    if (Number.isFinite(n) && lineNames.has(n))
        return lineNames.get(n);
    return String(lineNum).trim();
}
/**
 * Normalize the plant's inconsistently-entered coil_location values to "Building N" for display. The
 * floor recorded the same building many ways — a bare number, or BLD / BLG / BLDG / "Building" (± space)
 * + N, case-insensitive — all meaning the same place (user 2026-07-10: "1", "BLD 1", "blg 1", "BLDG 1"
 * are all Building 1; "BLD 3", "BLDG 3" are Building 3). Anything that isn't one of those number-based
 * variants (e.g. a yard/warehouse code) passes through unchanged, so non-building locations aren't mangled.
 */
export function buildingLabel(location) {
    if (location == null)
        return '';
    const s = String(location).trim();
    const m = s.match(/^(?:building|bldg|bld|blg)?\s*(\d+)$/i);
    return m ? `Building ${m[1]}` : s;
}
function lookup(domain, code) {
    if (code == null || code === '')
        return undefined;
    const n = Number(code);
    return Number.isFinite(n) ? STATUS_MAPS[domain]?.[n] : undefined;
}
/** Human label for a status/type code, or the raw value (as a string) if the domain/code is unknown. */
export function statusText(domain, code) {
    if (code == null || code === '')
        return '—';
    return lookup(domain, code)?.label ?? String(code);
}
/**
 * Chip HTML for a status/type code: the decoded label with a severity tone, or — when the code is
 * not in the (authoritative) map — a neutral chip showing the raw value. The numeric code is kept
 * in the title tooltip.
 */
export function statusChip(domain, code) {
    if (code == null || code === '')
        return '<span class="chip mut">—</span>';
    const entry = lookup(domain, code);
    return entry
        ? `<span class="chip ${entry.tone}" title="code ${esc(code)}">${esc(entry.label)}</span>`
        : `<span class="chip mut">${esc(code)}</span>`;
}
