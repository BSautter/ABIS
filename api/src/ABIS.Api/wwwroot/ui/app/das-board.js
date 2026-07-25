// ABIS DAS Board — the shop-floor kiosk surface (#4c). A standalone, always-dark, glanceable
// production-floor status board (NOT the desktop shell, NOT the operator data-entry console). It
// answers "what's every line doing right now?" at a glance from across the floor, and auto-refreshes.
//
// Per line it derives a Running / Idle / Stopped light. The AUTHORITATIVE source is the live line
// board (legacy LINE_CURRENT_STATUS — the row the DAS station rewrites as it runs); the job list is
// the fallback for a line with no board row:
//   Stopped (red)  — a line/stacker error logged in the last 12 h
//   Running (green)— the board has a coil loaded on an open shift (else: an in-process job)
//   Idle (amber)   — an open shift with no coil, or staged/new jobs with none running
// Data: GET das/line-board (shift/job/coil + the 19 floor skid positions and 2 stacker heads),
// GET stacker-board (jobs per line) + GET line-errors. Read-only.
//
// Compiled by tsc to wwwroot/ui/app/das-board.js; served at /ui/das-board.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { statusChip, lineLabel, loadLineNames, isProductionLine } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const n0 = (n) => (n == null ? '0' : Number(n).toLocaleString());
const REFRESH_MS = 15000;
const FAULT_WINDOW_MS = 12 * 60 * 60 * 1000;
const STATE_LABEL = { run: 'Running', idle: 'Idle', stop: 'Stopped', offline: 'Offline' };
/**
 * What each lamp is actually based on. Spelled out on hover because this board reads like a live
 * mimic panel and is NOT one: the state comes from the shift, coil and job RECORDS, never from the
 * press itself. Only BL 78, BL 84 and BL 110 have a PLC feed at all (confirmed by the plant
 * 2026-07-25) — the other four lines have no hardware to ask, so no amount of wiring would make this
 * lamp a press signal for them. A line can therefore read "Running" while the press is stopped, if
 * nobody has closed the shift.
 */
const STATE_BASIS = {
    run: 'From the shift and job records — a coil is loaded on an open shift (or a job is in process). NOT a live signal from the press.',
    idle: 'From the shift and job records — no coil loaded, or no open shift. NOT a live signal from the press.',
    stop: 'A fault was logged against this line in the last hour (ERROR_EVT), not a live signal from the press.',
    offline: 'No open shift and no jobs on this line.',
};
function scaffold() {
    return `
  <div class="das">
    <div class="das-top">
      <div class="logo">AB</div>
      <div><h1>Production Floor<small>ABIS · DAS Board</small></h1></div>
      <div class="sp"></div>
      <div><div class="clock" id="clock">—</div><div class="updated" id="updated"></div></div>
    </div>
    <div class="das-refresh" id="bar"></div>
    <div class="das-strip" id="strip"></div>
    <div class="das-grid" id="grid"><div class="das-empty">Loading the floor…</div></div>
  </div>`;
}
// The live board wins when the line has a line_current_status row: a coil loaded on an open shift
// IS the line running, whatever the job list says. Falls back to the job-derived light otherwise.
function stateOf(jobs, fault, live) {
    if (fault)
        return 'stop';
    if (live?.shiftNum != null)
        return live.coilAbcNum != null ? 'run' : 'idle';
    if (jobs.some((j) => j.jobStatus === 1))
        return 'run';
    if (jobs.length)
        return 'idle';
    return 'offline';
}
function buildLines(board, errs, live, metrics) {
    const now = Date.now();
    const recentByLine = new Map();
    for (const e of errs) {
        if (e.lineId == null || e.evtTime == null)
            continue;
        const t = new Date(e.evtTime).getTime();
        if (now - t > FAULT_WINDOW_MS)
            continue;
        const cur = recentByLine.get(e.lineId);
        if (!cur || new Date(e.evtTime).getTime() > new Date(cur.evtTime).getTime())
            recentByLine.set(e.lineId, e);
    }
    const liveByLine = new Map();
    for (const l of live)
        if (l.lineNum != null)
            liveByLine.set(l.lineNum, l);
    const metricsByLine = new Map();
    for (const m of metrics)
        if (m.lineNum != null)
            metricsByLine.set(m.lineNum, m);
    const byLine = new Map();
    // A line with a board row but no active job still belongs on the floor (it is between jobs,
    // not absent), so seed the map from the live board first.
    //
    // The "no line assigned" sentinel (line_num 0, LINE_DESC 'NONE') is NOT a press and never belongs
    // here. It had been rendering as an eighth tile carrying every job never put on a line — ~1,300 of
    // them, which showed as "905 jobs on line", 2,021 coils and 2,595 skids, and swamped the floor
    // totals so "coils on floor" read 2,110 when the real lines held 89.
    for (const l of liveByLine.keys())
        if (isProductionLine(l))
            byLine.set(l, []);
    for (const r of board) {
        if (r.lineNum == null || !isProductionLine(r.lineNum))
            continue;
        let arr = byLine.get(r.lineNum);
        if (!arr) {
            arr = [];
            byLine.set(r.lineNum, arr);
        }
        arr.push(r);
    }
    const lines = [];
    for (const [line, jobs] of byLine) {
        const lcs = liveByLine.get(line);
        // Prefer the job the LINE says it is running; fall back to the in-process job.
        const active = (lcs?.abJobNum != null ? jobs.find((j) => j.abJobNum === lcs.abJobNum) : undefined)
            ?? jobs.find((j) => j.jobStatus === 1) ?? jobs[0]
            ?? (lcs?.abJobNum != null ? { abJobNum: lcs.abJobNum, lineNum: line, jobStatus: lcs.jobStatus, orderAbcNum: lcs.orderAbcNum } : {});
        const fault = recentByLine.get(line);
        lines.push({
            line, jobs, active,
            coils: jobs.reduce((s, j) => s + (j.coilCount ?? 0), 0),
            skids: jobs.reduce((s, j) => s + (j.skidCount ?? 0), 0),
            state: stateOf(jobs, fault, lcs), fault, live: lcs, metrics: metricsByLine.get(line),
        });
    }
    return lines.sort((a, b) => a.line - b.line);
}
// The coil currently on the mandrel, as the line reports it: id, spec, and the weight left to run.
function coilLine(b) {
    if (b.coilAbcNum == null)
        return '<div class="das-live"><span class="k">Coil</span><span class="v dim">No coil loaded</span></div>';
    const spec = [b.coilAlloy2, b.coilGauge != null ? Number(b.coilGauge).toFixed(3) + '"' : null,
        b.coilWidth != null ? Number(b.coilWidth).toFixed(3) + '" wide' : null].filter(Boolean).join(' · ');
    const left = b.coilNetWtBalance != null ? `${n0(b.coilNetWtBalance)} lb left` : '';
    return `<div class="das-live"><span class="k">Coil</span><span class="v">#${esc(b.coilAbcNum)}${b.coilOrgNum ? ' · ' + esc(b.coilOrgNum) : ''}</span>
    ${spec || left ? `<span class="v dim">${esc(spec)}${spec && left ? ' · ' : ''}${esc(left)}</span>` : ''}</div>`;
}
// The physical board: the 19 numbered floor positions plus the two stacker heads. Only occupied
// slots come back from the API, so an empty strip means the line has been cleared.
function skidStrip(skids) {
    if (!skids.length)
        return '<div class="das-live"><span class="k">Skids</span><span class="v dim">Board clear</span></div>';
    const cell = (s) => `<span class="das-slot" title="${esc(s.sheetSkidDisplayNum ?? s.sheetSkidNum)}${s.skidPieces != null ? ` · ${n0(s.skidPieces)} pcs` : ''}">
    <b>${esc(s.slot.startsWith('STACKER') ? 'S' + s.slot.slice(-1) : s.slot)}</b>${esc(s.sheetSkidNum)}</span>`;
    return `<div class="das-live"><span class="k">Skids</span><span class="das-slots">${skids.map(cell).join('')}</span></div>`;
}
function tile(l) {
    const a = l.active ?? {};
    const b = l.live;
    const fault = l.fault
        ? `<div class="das-fault">⚠ <span>${esc(l.fault.title || l.fault.errorComment || 'Fault logged')}</span></div>` : '';
    // The live block only renders for a line that actually has a board row.
    const shift = b?.shiftNum != null
        ? `Shift ${esc(b.shiftNum)}${b.shiftOperatorInitial ? ' · ' + esc(b.shiftOperatorInitial) : ''}`
        : 'No open shift';
    const live = b
        ? `<div class="das-livebox">
         <div class="das-live"><span class="k">Shift</span><span class="v${b.shiftNum == null ? ' dim' : ''}">${shift}</span></div>
         ${coilLine(b)}
         ${skidStrip(b.skids ?? [])}
       </div>` : '';
    return `
  <div class="das-tile ${l.state}">
    <div class="thead">
      <span class="lineno">${esc(lineLabel(l.line))}</span>
      <span class="das-dot ${l.state}" title="${esc(STATE_BASIS[l.state])}"><i></i>${STATE_LABEL[l.state]}</span>
    </div>
    <div class="das-job">${a.abJobNum != null ? '#' + esc(a.abJobNum) : '—'} ${statusChip('jobStatus', a.jobStatus)}</div>
    <div class="das-sub">${a.orderAbcNum != null ? 'Order ' + esc(a.orderAbcNum) : 'No active job'}${l.jobs.length > 1 ? ` · ${l.jobs.length} jobs on line` : ''}</div>
    <div class="das-metrics">
      <div class="m"><div class="mn">${n0(l.coils)}</div><div class="ml">Coils</div></div>
      <div class="m"><div class="mn">${n0(l.skids)}</div><div class="ml">Skids</div></div>
      ${b?.coilProcessRate != null ? `<div class="m"><div class="mn">${n0(b.coilProcessRate)}</div><div class="ml">Rate</div></div>` : ''}
      ${l.metrics?.efficiencyPct != null ? `<div class="m"><div class="mn${l.metrics.downtimeOpen ? ' warn' : ''}">${l.metrics.efficiencyPct.toFixed(0)}%</div><div class="ml">Effic</div></div>` : ''}
      ${l.metrics?.coilYieldPct != null ? `<div class="m"><div class="mn${l.metrics.yieldBelowTarget ? ' bad' : ''}">${l.metrics.coilYieldPct.toFixed(1)}%</div><div class="ml">Yield</div></div>` : ''}
    </div>
    ${l.metrics?.coilFinishPct != null
        ? `<div class="das-finish" title="How far through the loaded coil this line is"><i style="width:${Math.min(100, l.metrics.coilFinishPct).toFixed(1)}%"></i><span>${l.metrics.coilFinishPct.toFixed(0)}% of coil run</span></div>`
        : ''}
    ${live}
    ${fault}
  </div>`;
}
function renderStrip(lines) {
    const running = lines.filter((l) => l.state === 'run').length;
    const stopped = lines.filter((l) => l.state === 'stop').length;
    const coils = lines.reduce((s, l) => s + l.coils, 0);
    const skids = lines.reduce((s, l) => s + l.skids, 0);
    const openShifts = lines.filter((l) => l.live?.shiftNum != null).length;
    const kpi = (n, l, cls = '') => `<div class="das-kpi"><div class="n ${cls}">${n}</div><div class="l">${l}</div></div>`;
    $('#strip').innerHTML =
        kpi(String(lines.length), 'Active lines') +
            kpi(String(running), 'Running', 'run') +
            kpi(String(stopped), 'Stopped', stopped ? 'stop' : '') +
            kpi(String(openShifts), 'Open shifts') +
            kpi(n0(coils), 'Coils on floor') +
            kpi(n0(skids), 'Skids on floor');
}
// The live board is fetched with authFetch (not the generated client) so an older server that
// predates the endpoint degrades to the job-derived board instead of blanking the floor.
async function fetchLineBoard() {
    try {
        const r = await authFetch('/api/das/line-board');
        if (!r.ok)
            return [];
        return (await r.json());
    }
    catch {
        return [];
    }
}
// Metrics are per line, so they are fetched once the board says which lines exist. One failure
// (or an older server) costs the percentages, not the board.
async function fetchLiveMetrics(lines) {
    const reads = lines.filter((l) => l.lineNum != null).map(async (l) => {
        try {
            const r = await authFetch(`/api/das/lines/${l.lineNum}/live`);
            return r.ok ? (await r.json()) : null;
        }
        catch {
            return null;
        }
    });
    return (await Promise.all(reads)).filter((m) => m != null);
}
async function load() {
    try {
        const [board, errs, live] = await Promise.all([
            client().getStackerBoard(undefined),
            client().getLineErrors(undefined, undefined, undefined),
            fetchLineBoard(),
        ]);
        const metrics = await fetchLiveMetrics(live ?? []);
        const lines = buildLines(board ?? [], errs ?? [], live ?? [], metrics);
        renderStrip(lines);
        $('#grid').innerHTML = lines.length ? lines.map(tile).join('') : '<div class="das-empty">No active lines on the floor.</div>';
        $('#updated').textContent = `Updated ${new Date().toLocaleTimeString()}`;
        // restart the refresh-countdown bar
        const bar = $('#bar');
        bar.style.animation = 'none';
        void bar.offsetWidth;
        bar.style.animation = '';
    }
    catch (e) {
        $('#grid').innerHTML = `<div class="das-empty">Board unavailable: ${esc(e.message)}</div>`;
    }
}
function tick() { $('#clock').textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }
(async () => {
    document.body.innerHTML = scaffold();
    tick();
    setInterval(tick, 1000);
    await loadLineNames(authFetch); // real line names (LINE table) for the tile headers
    await load();
    setInterval(() => void load(), REFRESH_MS);
})();
