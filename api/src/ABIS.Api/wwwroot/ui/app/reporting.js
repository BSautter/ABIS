// ABIS Reporting — greenfield (Path C) production reporting (legacy daily_prod /
// silverdome3 w_report_production_*). A typed SPA on the Phase-2 API: a report selector
// over an optional date window, rendered into a dynamic-column table with CSV export.
// Reports: production summary, line efficiency (+ downtime), monthly production, downtime
// events, on-time delivery — all aggregated server-side. Through the NSwag client.
//
// Compiled by `tsc` to wwwroot/ui/app/reporting.js; served at /ui/reporting.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip, statusText, lineLabel } from './status-labels.js';
import { exportXlsx } from './xlsx.js';
const $ = (sel) => document.querySelector(sel);
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const dv = (id) => { const s = $(id).value.trim(); return s ? new Date(s) : undefined; };
const num = (n, dp = 0) => (n == null ? '' : Number(n).toLocaleString(undefined, { maximumFractionDigits: dp }));
// A stored yield RATIO (0–1) rendered for a "%" column: 0.9 → "90", 1 → "100". Null stays blank.
const pct = (n, dp = 1) => (n == null ? '' : num(Number(n) * 100, dp));
const dt = (d) => (d == null ? '' : new Date(d).toLocaleString());
// The uptime / downtime-pivot reports are served raw (not through the NSwag client) — a small
// authFetch helper with the shared from/to window + a groupBy discriminator.
async function loadJson(path) {
    const r = await authFetch(path);
    if (!r.ok)
        throw new Error(`HTTP ${r.status}`);
    return (await r.json()) ?? [];
}
const qwin = (f, t, extra = {}) => {
    const q = new URLSearchParams(extra);
    if (f)
        q.set('from', f.toISOString());
    if (t)
        q.set('to', t.toISOString());
    const s = q.toString();
    return s ? `?${s}` : '';
};
// Shared column set for the three uptime groupings — a leading label column + the numeric hours.
const uptimeCols = (h, first) => [
    { h, f: first },
    { h: 'Shifts', num: true, f: (r) => num(r.shiftCount), raw: (r) => r.shiftCount },
    { h: 'Scheduled hrs', num: true, f: (r) => num(r.scheduledHours, 1), raw: (r) => r.scheduledHours },
    { h: 'Downtime hrs', num: true, f: (r) => num(r.downtimeHours, 1), raw: (r) => r.downtimeHours },
    { h: 'Uptime hrs', num: true, f: (r) => num(r.uptimeHours, 1), raw: (r) => r.uptimeHours },
    { h: 'Uptime %', num: true, f: (r) => num(r.uptimePct, 1), raw: (r) => r.uptimePct },
];
// Shared column set for the downtime pivots — a leading bucket column + occurrences + minutes.
const pivotCols = (h) => [
    { h, f: (r) => esc(r.bucket) },
    { h: 'Occurrences', num: true, f: (r) => num(r.occurrences), raw: (r) => r.occurrences },
    { h: 'Downtime min', num: true, f: (r) => num(r.downtimeMinutes, 1), raw: (r) => r.downtimeMinutes },
];
const REPORTS = {
    summary: {
        note: 'Per-line roll-up over line ⋈ ab_job ⋈ process_coil (legacy daily_prod).',
        load: (f, t) => client().getProductionSummary(f, t),
        cols: [
            { h: 'Line', f: (r) => esc(lineLabel(r.lineNum)) },
            { h: 'Jobs', num: true, f: (r) => num(r.jobCount), raw: (r) => r.jobCount },
            { h: 'Avg yield %', num: true, f: (r) => pct(r.avgYield), raw: (r) => r.avgYield == null ? null : Number(r.avgYield) * 100 },
            { h: 'Processed wt', num: true, f: (r) => num(r.processedWt), raw: (r) => r.processedWt },
        ],
    },
    efficiency: {
        note: 'Per-line jobs, processed weight, avg yield, and downtime (events + minutes).',
        load: (f, t) => client().getLineEfficiency(f, t),
        cols: [
            { h: 'Line', f: (r) => esc(lineLabel(r.lineNum)) },
            { h: 'Jobs', num: true, f: (r) => num(r.jobCount), raw: (r) => r.jobCount },
            { h: 'Processed wt', num: true, f: (r) => num(r.processedWt), raw: (r) => r.processedWt },
            { h: 'Avg yield %', num: true, f: (r) => pct(r.avgYield), raw: (r) => r.avgYield == null ? null : Number(r.avgYield) * 100 },
            { h: 'DT events', num: true, f: (r) => num(r.downtimeEvents), raw: (r) => r.downtimeEvents },
            { h: 'DT minutes', num: true, f: (r) => num(r.downtimeMinutes, 1), raw: (r) => r.downtimeMinutes },
        ],
    },
    monthly: {
        note: 'Production rolled up by month (YYYY-MM): jobs touched + processed weight.',
        load: (f, t) => client().getMonthlyProduction(f, t),
        cols: [
            { h: 'Month', f: (r) => esc(r.month) },
            { h: 'Jobs', num: true, f: (r) => num(r.jobCount), raw: (r) => r.jobCount },
            { h: 'Processed wt', num: true, f: (r) => num(r.processedWt), raw: (r) => r.processedWt },
        ],
    },
    downtime: {
        note: 'Individual downtime events with computed duration (minutes).',
        load: (f, t) => client().getProductionDowntime(f, t, undefined),
        cols: [
            { h: 'Line', f: (r) => esc(lineLabel(r.lineNum)) },
            { h: 'Job', f: (r) => esc(r.abJobNum) }, { h: 'Start', f: (r) => esc(dt(r.startingTime)) },
            { h: 'End', f: (r) => esc(dt(r.endingTime)) },
            { h: 'Minutes', num: true, f: (r) => num(r.durationMinutes, 1), raw: (r) => r.durationMinutes },
            { h: 'Note', f: (r) => esc(r.note) },
        ],
    },
    ontime: {
        note: 'Per-line on-time delivery: jobs finished on/before due date.',
        load: (f, t) => client().getOnTimeDelivery(f, t),
        cols: [
            { h: 'Line', f: (r) => esc(lineLabel(r.lineNum)) },
            { h: 'Finished', num: true, f: (r) => num(r.finishedJobs), raw: (r) => r.finishedJobs },
            { h: 'On time', num: true, f: (r) => num(r.onTime), raw: (r) => r.onTime },
            { h: 'Late', num: true, f: (r) => num(r.late), raw: (r) => r.late },
            { h: 'On-time %', num: true, f: (r) => num(r.onTimePct, 1), raw: (r) => r.onTimePct },
        ],
    },
    'customer-shipments': {
        note: 'Per-customer shipment roll-up (total / shipped / open + last ship date).',
        load: (f, t) => client().getCustomerShipments(f, t),
        cols: [
            { h: 'Customer', f: (r) => esc(r.customerShortName) }, { h: 'Id', f: (r) => esc(r.customerId) },
            { h: 'Shipments', num: true, f: (r) => num(r.shipments), raw: (r) => r.shipments },
            { h: 'Shipped', num: true, f: (r) => num(r.shipped), raw: (r) => r.shipped },
            { h: 'Open', num: true, f: (r) => num(r.open), raw: (r) => r.open },
            { h: 'Last sent', f: (r) => esc(dt(r.lastSent)) },
        ],
    },
    'open-shipments': {
        note: 'Open (not-yet-sent) shipments with customer, carrier, and scheduled date.',
        load: () => client().getOpenShipments(),
        cols: [
            { h: 'Packing list', f: (r) => esc(r.packingList) }, { h: 'Customer', f: (r) => esc(r.customerShortName) },
            { h: 'Carrier', f: (r) => esc(r.carrierId) },
            { h: 'Status', f: (r) => statusChip('shipmentStatus', r.shipmentStatus), raw: (r) => statusText('shipmentStatus', r.shipmentStatus) },
            { h: 'Scheduled', f: (r) => esc(dt(r.shipmentScheduledDateTime)) },
            { h: 'Vehicle', f: (r) => esc(r.vehicleId) }, { h: 'Notes', f: (r) => esc(r.shipmentNotes) },
        ],
    },
    'customer-orders': {
        note: 'Customer orders with PO / sales-order references.',
        load: () => client().getCustomerOrdersReport(undefined),
        cols: [
            { h: 'Order', f: (r) => esc(r.orderAbcNum) }, { h: 'Customer', f: (r) => esc(r.customerShortName) },
            { h: 'Customer PO', f: (r) => esc(r.origCustomerPo) }, { h: 'Enduser PO', f: (r) => esc(r.enduserPo) },
            { h: 'Sales order', f: (r) => esc(r.salesOrder) }, { h: 'Created', f: (r) => esc(dt(r.createdDate)) },
        ],
    },
    'customer-skid-count': {
        note: 'Per-customer finished sheet-skid counts + total net weight.',
        load: () => client().getCustomerSkidCount(),
        cols: [
            { h: 'Customer', f: (r) => esc(r.customerShortName) }, { h: 'Id', f: (r) => esc(r.customerId) },
            { h: 'Skids', num: true, f: (r) => num(r.skidCount), raw: (r) => r.skidCount },
            { h: 'Total net wt', num: true, f: (r) => num(r.totalNetWt), raw: (r) => r.totalNetWt },
        ],
    },
    'coil-inventory': {
        note: 'Coil inventory by alloy: count + total net / balance weight.',
        load: () => client().getCoilInventory(undefined),
        cols: [
            { h: 'Alloy', f: (r) => esc(r.coilAlloy2) },
            { h: 'Coils', num: true, f: (r) => num(r.coilCount), raw: (r) => r.coilCount },
            { h: 'Total net wt', num: true, f: (r) => num(r.totalNetWt), raw: (r) => r.totalNetWt },
            { h: 'Total balance', num: true, f: (r) => num(r.totalBalance), raw: (r) => r.totalBalance },
        ],
    },
    'coil-on-hold': {
        note: 'On-hold coils (coil_status = 3) with location, owner, and balance weight.',
        load: () => client().getOnHoldCoils(),
        cols: [
            { h: 'Coil#', f: (r) => esc(r.coilAbcNum) }, { h: 'Org num', f: (r) => esc(r.coilOrgNum) },
            { h: 'Alloy', f: (r) => esc(r.coilAlloy2) }, { h: 'Temper', f: (r) => esc(r.coilTemper) },
            { h: 'Location', f: (r) => esc(r.coilLocation) }, { h: 'Customer', f: (r) => esc(r.customerId) },
            { h: 'Balance wt', num: true, f: (r) => num(r.netWtBalance), raw: (r) => r.netWtBalance },
            { h: 'Notes', f: (r) => esc(r.coilNotes) },
        ],
    },
    'skid-inventory': {
        note: 'Finished sheet-skid inventory by status: count + total net weight.',
        load: () => client().getSkidInventory(),
        cols: [
            { h: 'Status', f: (r) => statusChip('skidSheetStatus', r.skidSheetStatus), raw: (r) => statusText('skidSheetStatus', r.skidSheetStatus) },
            { h: 'Skids', num: true, f: (r) => num(r.skidCount), raw: (r) => r.skidCount },
            { h: 'Total net wt', num: true, f: (r) => num(r.totalNetWt), raw: (r) => r.totalNetWt },
        ],
    },
    'unmatched-coils': {
        note: 'Coils not referenced by any process_coil — orphan / unmatched inventory.',
        load: () => client().getUnmatchedCoils(),
        cols: [
            { h: 'Coil#', f: (r) => esc(r.coilAbcNum) }, { h: 'Org num', f: (r) => esc(r.coilOrgNum) },
            { h: 'Alloy', f: (r) => esc(r.coilAlloy2) },
            { h: 'Status', f: (r) => statusChip('coilStatus', r.coilStatus), raw: (r) => statusText('coilStatus', r.coilStatus) },
            { h: 'Location', f: (r) => esc(r.coilLocation) }, { h: 'Customer', f: (r) => esc(r.customerId) },
            { h: 'Balance wt', num: true, f: (r) => num(r.netWtBalance), raw: (r) => r.netWtBalance },
        ],
    },
    'scrap-summary': {
        note: 'Scrap by type (code/defect) with skid count + total net weight.',
        load: () => client().getScrapSummary(),
        cols: [
            { h: 'Type', f: (r) => statusChip('scrapType', r.scrapType), raw: (r) => statusText('scrapType', r.scrapType) }, { h: 'Code', f: (r) => esc(r.scrapCode) },
            { h: 'Defect', f: (r) => esc(r.scrapDefect) },
            { h: 'Skids', num: true, f: (r) => num(r.skidCount), raw: (r) => r.skidCount },
            { h: 'Total net wt', num: true, f: (r) => num(r.totalNetWt), raw: (r) => r.totalNetWt },
        ],
    },
    'scrap-by-job': {
        note: 'Scrap by job: skid count + total net weight.',
        load: () => client().getScrapByJob(),
        cols: [
            { h: 'Job', f: (r) => esc(r.scrapAbJobNum) },
            { h: 'Skids', num: true, f: (r) => num(r.skidCount), raw: (r) => r.skidCount },
            { h: 'Total net wt', num: true, f: (r) => num(r.totalNetWt), raw: (r) => r.totalNetWt },
        ],
    },
    // ---- Uptime (legacy w_report_uptime): worked shifts only, uptime = (shift length − dt_total)/3600 ----
    'uptime-line': {
        note: 'Line uptime over worked shifts: scheduled vs downtime hours + uptime %. (legacy w_report_uptime, by line)',
        load: (f, t) => loadJson(`/api/reporting/uptime${qwin(f, t, { groupBy: 'line' })}`),
        cols: uptimeCols('Line', (r) => esc(lineLabel(r.lineNum))),
    },
    'uptime-shift': {
        note: 'Uptime by shift (1st / 2nd / 3rd): scheduled vs downtime hours + uptime %.',
        load: (f, t) => loadJson(`/api/reporting/uptime${qwin(f, t, { groupBy: 'shift' })}`),
        cols: uptimeCols('Shift', (r) => esc(r.bucket)),
    },
    'uptime-day': {
        note: 'Uptime by day: scheduled vs downtime hours + uptime %.',
        load: (f, t) => loadJson(`/api/reporting/uptime${qwin(f, t, { groupBy: 'day' })}`),
        cols: uptimeCols('Day', (r) => esc(r.bucket)),
    },
    // ---- Downtime pivots (legacy daily-prod dt_* pivots): occurrences + minutes along one dimension ----
    'dt-by-cause': {
        note: 'Downtime minutes + occurrences by cause code (dt_instance_detail.instance_item).',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'cause' })}`),
        cols: pivotCols('Cause code'),
    },
    'dt-by-job': {
        note: 'Downtime minutes + occurrences by job.',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'job' })}`),
        cols: pivotCols('Job'),
    },
    'dt-by-line': {
        note: 'Downtime minutes + occurrences by line.',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'line' })}`),
        cols: pivotCols('Line'),
    },
    'dt-by-shift': {
        note: 'Downtime minutes + occurrences by shift (1st / 2nd / 3rd).',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'shift' })}`),
        cols: pivotCols('Shift'),
    },
    'dt-by-day': {
        note: 'Downtime minutes + occurrences by day.',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'day' })}`),
        cols: pivotCols('Day'),
    },
    'dt-by-month': {
        note: 'Downtime minutes + occurrences by month (YYYY-MM).',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'month' })}`),
        cols: pivotCols('Month'),
    },
    'dt-by-year': {
        note: 'Downtime minutes + occurrences by year.',
        load: (f, t) => loadJson(`/api/reporting/downtime-pivot${qwin(f, t, { groupBy: 'year' })}`),
        cols: pivotCols('Year'),
    },
};
let current = [];
let currentKey = 'summary';
async function run() {
    setErr('');
    setBusy(true);
    currentKey = $('#report').value;
    const rep = REPORTS[currentKey];
    $('#repNote').textContent = rep.note;
    $('#head').innerHTML = rep.cols.map((c) => `<th${c.num ? ' style="text-align:right"' : ''}>${esc(c.h)}</th>`).join('');
    try {
        current = (await rep.load(dv('#from'), dv('#to'))) ?? [];
        $('#rows').innerHTML = current.map((r) => `<tr>${rep.cols.map((c) => `<td${c.num ? ' style="text-align:right"' : ''}>${c.f(r)}</td>`).join('')}</tr>`).join('')
            || `<tr><td colspan="${rep.cols.length}" class="muted">No data.</td></tr>`;
        $('#totals').textContent = `${current.length} row(s)`;
    }
    catch (e) {
        setErr(`Report failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// The raw (un-styled) value of a cell for export: numbers stay numeric via `raw`; otherwise the
// rendered cell has its HTML stripped and basic entities decoded back to text.
const rawCell = (c, r) => c.raw ? c.raw(r)
    : c.f(r).replace(/<[^>]*>/g, '').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
// CSV export of the current report (raw numeric values, quoted text).
function exportCsv() {
    const rep = REPORTS[currentKey];
    const cell = (v) => { const s = String(v ?? ''); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
    const header = rep.cols.map((c) => cell(c.h)).join(',');
    const lines = current.map((r) => rep.cols.map((c) => cell(rawCell(c, r))).join(','));
    const csv = [header, ...lines].join('\r\n');
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = `${currentKey}-report.csv`;
    a.click();
    URL.revokeObjectURL(url);
}
// Native .xlsx export of the current report — same columns as CSV, but numbers stay numeric so
// Excel sums/sorts them directly (legacy reports were Excel/PDF; CSV alone lost the typing).
function exportExcel() {
    const rep = REPORTS[currentKey];
    const headers = rep.cols.map((c) => c.h);
    const rows = current.map((r) => rep.cols.map((c) => rawCell(c, r)));
    exportXlsx(`${currentKey}-report`, currentKey, headers, rows);
}
async function init() {
    $('#repForm').addEventListener('submit', (e) => { e.preventDefault(); void run(); });
    $('#btnCsv').addEventListener('click', exportCsv);
    $('#btnXlsx').addEventListener('click', exportExcel);
    await initShell({ active: 'reporting', adopt: true });
    await run();
}
void init();
