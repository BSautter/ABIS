// ABIS Stacker line board — the legacy stacker_110 / w_110_stacker_read_only, restyled to the
// design system in the shared shell (#4 polish). A line-specific monitor: the jobs running on a
// line (coil/skid counts) and the line/stacker error log, with a quick "log error" action. The
// operator data-entry workflow lives in the DAS console; this is the read-only board + fault log.
//
// Compiled by tsc to wwwroot/ui/app/stacker.js; served at /ui/stacker.html.
import { AbisClient, LineErrorWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip, lineLabel, STACK_PATH } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const dt = (d) => (d == null ? '' : new Date(d).toLocaleString());
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let line = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Stacker</div><h1>Stacker board</h1></div>
      <form id="lineForm" class="frow"><div class="fld"><label>Line # (blank = all)</label><input id="fLine" inputmode="numeric" style="width:130px" /></div><button class="btn sm" type="submit">Refresh</button></form>
    </div>
    <div id="err" class="err" style="margin-bottom:8px"></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Line board</h2><span class="sub">jobs running on the line</span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
        <thead><tr><th>Job</th><th>Line</th><th>Status</th><th>Order</th><th class="num">Coils</th><th class="num">Skids</th></tr></thead>
        <tbody id="tBoard"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div class="card" id="pathCard" style="margin-bottom:16px">
      <header><h2>Conveyor path</h2><span class="sub" id="pathSub">where each finished stack is between the stacker and the end of the wrapper line</span></header>
      <div class="body"><div class="stack-path" id="tPath"></div></div>
    </div>

    <div class="card">
      <header><h2>Line / stacker error log</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:680px">
        <thead><tr><th>Time</th><th>Type</th><th>Line</th><th>Job</th><th>Title</th><th>Comment</th><th>User</th></tr></thead>
        <tbody id="tErrors"><tr><td colspan="7" class="muted">Loading…</td></tr></tbody>
      </table></div>
      <div class="body">
        <div class="frow">
          <div class="fld"><label>Type id *</label><input id="eType" inputmode="numeric" style="width:90px" /></div>
          <div class="fld"><label>User *</label><input id="eUser" maxlength="12" style="width:110px" /></div>
          <div class="fld"><label>Line</label><input id="eLine" inputmode="numeric" style="width:80px" /></div>
          <div class="fld"><label>Job</label><input id="eJob" inputmode="numeric" style="width:90px" /></div>
          <div class="fld"><label>Title</label><input id="eTitle" maxlength="50" style="width:160px" /></div>
          <div class="fld" style="flex:1;min-width:180px"><label>Comment</label><input id="eComment" maxlength="255" /></div>
        </div>
        <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnErr" type="button">Log error</button><span id="ok" class="ok-note"></span></div>
      </div>
    </div>
  </div>`;
}
async function load() {
    setErr('');
    setOk('');
    line = v('#fLine') ? Number(v('#fLine')) : null;
    setBusy(true);
    try {
        await Promise.all([loadBoard(), loadPath(), loadErrors()]);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadPath() {
    const box = $('#tPath');
    let lines = [];
    try {
        // authFetch (not the generated client) so an older server without the endpoint degrades to an
        // empty board instead of failing the whole page.
        const r = await authFetch(`/api/das/line-board${line != null ? `?lineNum=${line}` : ''}`);
        lines = r.ok ? (await r.json()) : [];
    }
    catch {
        lines = [];
    }
    if (!lines.length) {
        box.innerHTML = '<span class="muted">No line board data.</span>';
        return;
    }
    // Only the numbered path slots belong here; the two stacker heads are their own thing (the DAS
    // console's station panel), so filter them out rather than mixing them into the path.
    const onPath = (s) => /^\d+$/.test(s.slot);
    const anyStack = lines.some((l) => (l.skids ?? []).some(onPath));
    const onShownPath = new Set(STACK_PATH.map((p) => p.slot));
    box.innerHTML = lines.map((l) => {
        const bySlot = new Map((l.skids ?? []).filter(onPath).map((s) => [s.slot, s]));
        const cells = STACK_PATH.map(({ slot, label }) => {
            const s = bySlot.get(slot);
            return `<div class="stack-cell${s ? ' full' : ''}" title="${esc(label)} (location ${esc(slot)})">
        <div class="sn">${esc(slot)}</div>
        <div class="sl">${esc(label)}</div>
        <div class="sv">${s ? `#${esc(s.sheetSkidDisplayNum ?? s.sheetSkidNum)}${s.abJobNum != null ? `<span>job ${esc(s.abJobNum)}</span>` : ''}` : ''}</div>
      </div>`;
        }).join('');
        // A skid recorded at a station the path no longer shows (wrapper 2 was removed) must NOT vanish —
        // dropping it silently would hide real inventory. Surface it explicitly instead.
        const offPath = (l.skids ?? []).filter((s) => onPath(s) && !onShownPath.has(s.slot));
        const stray = offPath.length
            ? `<div class="stack-stray">⚠ ${offPath.map((s) => `skid #${esc(s.sheetSkidDisplayNum ?? s.sheetSkidNum)} recorded at removed station ${esc(s.slot)} (${esc(statusChip('stackLocation', Number(s.slot)).replace(/<[^>]*>/g, ''))})`).join('; ')}</div>`
            : '';
        return `<div class="stack-line"><div class="sh">${esc(lineLabel(l.lineNum))}</div><div class="stack-cells">${cells}</div>${stray}</div>`;
    }).join('');
    // Be explicit when the path is empty rather than showing a silent row of blanks: on the live DB
    // these columns are unpopulated, so an empty board is expected, not a fault.
    $('#pathSub').textContent = anyStack
        ? 'where each finished stack is between the stacker and the end of the wrapper line'
        : 'no stacks tracked on the conveyor path right now (these positions are written by the stacker automation)';
}
async function loadBoard() {
    const rows = await client().getStackerBoard(line ?? undefined);
    $('#tBoard').innerHTML = (rows ?? []).length ? (rows ?? []).map((j) => `<tr>
    <td class="mono">${esc(j.abJobNum)}</td><td class="mono">${esc(lineLabel(j.lineNum))}</td><td>${statusChip('jobStatus', j.jobStatus)}</td>
    <td class="mono">${esc(j.orderAbcNum)}</td><td class="num">${esc(j.coilCount)}</td><td class="num">${esc(j.skidCount)}</td></tr>`).join('')
        : '<tr><td colspan="6" class="muted">No jobs on this line.</td></tr>';
}
async function loadErrors() {
    const rows = await client().getLineErrors(line ?? undefined, undefined, undefined);
    $('#tErrors').innerHTML = (rows ?? []).length ? (rows ?? []).map((e) => `<tr>
    <td class="mono">${esc(dt(e.evtTime))}</td><td>${chip(e.errorType)}</td><td class="mono">${esc(lineLabel(e.lineId))}</td>
    <td class="mono">${esc(e.abJobNum)}</td><td>${esc(e.title)}</td><td>${esc(e.errorComment)}</td><td>${esc(e.errorUser)}</td></tr>`).join('')
        : '<tr><td colspan="7" class="muted">No errors logged.</td></tr>';
}
async function logError() {
    if (!v('#eType') || !v('#eUser')) {
        setErr('Error type and user are required.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    try {
        await client().createLineError(new LineErrorWrite({
            errorTypeId: Number(v('#eType')),
            errorUser: v('#eUser'),
            lineId: line ?? (v('#eLine') ? Number(v('#eLine')) : undefined),
            abJobNum: v('#eJob') ? Number(v('#eJob')) : undefined,
            title: v('#eTitle') || undefined,
            errorComment: v('#eComment') || undefined,
        }));
        setOk('✓ Error logged.');
        ['#eTitle', '#eComment'].forEach((i) => setV(i, ''));
        await loadErrors();
    }
    catch (e) {
        setErr(`Log error failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'stacker' });
    main.innerHTML = scaffold();
    $('#lineForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
    $('#btnErr').addEventListener('click', () => void logError());
    await load();
})();
