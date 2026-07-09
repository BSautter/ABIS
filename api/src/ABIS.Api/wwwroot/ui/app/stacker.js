// ABIS Stacker line board — the legacy stacker_110 / w_110_stacker_read_only, restyled to the
// design system in the shared shell (#4 polish). A line-specific monitor: the jobs running on a
// line (coil/skid counts) and the line/stacker error log, with a quick "log error" action. The
// operator data-entry workflow lives in the DAS console; this is the read-only board + fault log.
//
// Compiled by tsc to wwwroot/ui/app/stacker.js; served at /ui/stacker.html.
import { AbisClient, LineErrorWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
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
        await Promise.all([loadBoard(), loadErrors()]);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadBoard() {
    const rows = await client().getStackerBoard(line ?? undefined);
    $('#tBoard').innerHTML = (rows ?? []).length ? (rows ?? []).map((j) => `<tr>
    <td class="mono">${esc(j.abJobNum)}</td><td class="mono">${esc(j.lineNum)}</td><td>${chip(j.jobStatus)}</td>
    <td class="mono">${esc(j.orderAbcNum)}</td><td class="num">${esc(j.coilCount)}</td><td class="num">${esc(j.skidCount)}</td></tr>`).join('')
        : '<tr><td colspan="6" class="muted">No jobs on this line.</td></tr>';
}
async function loadErrors() {
    const rows = await client().getLineErrors(line ?? undefined, undefined, undefined);
    $('#tErrors').innerHTML = (rows ?? []).length ? (rows ?? []).map((e) => `<tr>
    <td class="mono">${esc(dt(e.evtTime))}</td><td>${chip(e.errorType)}</td><td class="mono">${esc(e.lineId)}</td>
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
