// ABIS Production Jobs — the heart of ABIS (slitting / cut-to-length job processing), restyled to
// the design system in the shared shell (#4 polish). Two lists now: "Uncomplete jobs" (active work —
// in-process / new / on-hold, never Done or Cancelled) and a searchable "Completed jobs" card, plus a
// shared job detail (processed coils / finished skids / scrap / partials / scans), an operational
// patch, and a create form. Typed calls via the NSwag client; the two lists use authFetch so they can
// pass the completed/search filters the generated client doesn't carry.
//
// Compiled by tsc to wwwroot/ui/app/jobs.js; served at /ui/jobs.html.
import { AbisClient, JobWrite, JobPatch } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip, lineLabel } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const num = (n) => (n == null ? '' : n.toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleString());
// Local-time formatter for the datetime-local "Finished" input — toISOString() would emit UTC and shift
// the value by the whole timezone offset on show/re-save (see downtime.ts).
const dLocal = (d) => (d == null ? '' : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
// The two lists come off raw fetch, so dates arrive as ISO strings, not Date objects.
const dText = (s) => {
    if (s == null || s === '')
        return '';
    const dt = s instanceof Date ? s : new Date(s);
    return Number.isNaN(dt.getTime()) ? String(s) : dt.toLocaleString();
};
let selectedJob = null;
async function fetchJobs(params) {
    const qs = new URLSearchParams({ page: '1', pageSize: '50' });
    for (const [k, val] of Object.entries(params))
        qs.set(k, String(val));
    const res = await authFetch(`/api/jobs?${qs.toString()}`);
    if (!res.ok)
        throw new Error(`HTTP ${res.status}`);
    return res.json();
}
function jobRowsHtml(items, lastCol) {
    return items.map((j) => `
    <tr class="click" data-id="${j.abJobNum}">
      <td class="mono">${esc(j.abJobNum)}</td><td class="mono">${esc(lineLabel(j.lineNum))}</td>
      <td>${statusChip('jobStatus', j.jobStatus)}</td><td class="num">${esc(num(j.materialYield))}</td>
      <td class="mono">${esc(lastCol(j))}</td>
    </tr>`).join('');
}
function wireRows(sel) {
    document.querySelectorAll(`${sel} tr.click`).forEach((tr) => tr.addEventListener('click', () => void loadJob(Number(tr.dataset.id))));
}
function scaffold() {
    const tab = (id, label) => `<button id="tab-${id}" type="button">${label}</button>`;
    const pane = (id, head) => `<div id="pane-${id}"><div style="overflow-x:auto"><table class="tbl" style="min-width:520px"><thead><tr>${head}</tr></thead><tbody id="t${id[0].toUpperCase()}${id.slice(1)}"><tr><td class="muted">Select a job.</td></tr></tbody></table></div></div>`;
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Jobs</div><h1>Jobs &amp; floor</h1></div>
      <div class="shift-tag" id="count">—</div>
    </div>

    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Uncomplete jobs</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:460px">
          <thead><tr><th>Job</th><th>Line</th><th>Status</th><th class="num">Yield</th><th>Due</th></tr></thead>
          <tbody id="jobs"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card disabled" id="detail">
        <header><h2 id="detailTitle">Job detail</h2></header>
        <div class="body">
          <div class="kv" id="hdr"></div>
          <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:16px 0 8px">Update</h3>
          <div class="frow">
            <div class="fld"><label>Status</label><input id="pStatus" inputmode="numeric" style="width:90px" /></div>
            <div class="fld"><label>Men used</label><input id="pMen" inputmode="numeric" style="width:90px" /></div>
            <div class="fld"><label>Finished</label><input id="pFinished" type="datetime-local" /></div>
            <div class="fld" style="flex:1;min-width:180px"><label>Notes</label><textarea id="pNotes"></textarea></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnPatch" type="button">Save update</button><span id="ok" class="ok-note"></span></div>
        </div>
      </div></div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>Completed jobs</h2><span class="sub" id="completedSub"></span></header>
      <div class="body">
        <form id="completedForm" class="frow">
          <div class="fld"><label>Search job / order #</label><input id="fSearch" inputmode="numeric" style="width:170px" placeholder="job or order #" /></div>
          <button class="btn sm" type="submit">Search</button>
          <button class="btn sm ghost" id="btnRecent" type="button">Recent</button>
        </form>
      </div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:460px">
        <thead><tr><th>Job</th><th>Line</th><th>Status</th><th class="num">Yield</th><th>Finished</th></tr></thead>
        <tbody id="completedJobs"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>Job contents</h2></header>
      <div class="body">
        <div class="tabs">${tab('coils', 'Coils')}${tab('skids', 'Finished skids')}${tab('scrap', 'Scrap')}${tab('partials', 'Partials')}${tab('scans', 'Scans')}</div>
        ${pane('coils', '<th>Coil</th><th>Status</th><th>Processed</th><th class="num">End wt</th><th class="num">Qty</th><th>Alloy</th><th class="num">Gauge</th><th class="num">Width</th>')}
        ${pane('skids', '<th>Skid</th><th>Display</th><th class="num">Net wt</th><th class="num">Tare</th><th class="num">Pieces</th><th>Date</th>')}
        ${pane('scrap', '<th>Scrap</th><th>Alloy</th><th>Temper</th><th>Type</th><th class="num">Net wt</th><th>Location</th><th>Status</th>')}
        ${pane('partials', '<th>Skid</th><th class="num">Net wt</th><th class="num">Pieces</th><th>Location</th><th>Date</th>')}
        ${pane('scans', '<th>Scan</th><th>When</th><th>Station</th><th>Note</th>')}
      </div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>New job</h2><span class="sub">from an order line</span></header>
      <div class="body">
        <div class="frow">
          <div class="fld"><label>Order #</label><input id="nOrder" inputmode="numeric" style="width:100px" /></div>
          <div class="fld"><label>Item #</label><input id="nItem" inputmode="numeric" style="width:90px" /></div>
          <div class="fld"><label>Line #</label><input id="nLine" inputmode="numeric" style="width:90px" /></div>
          <div class="fld"><label>Status</label><input id="nStatus" inputmode="numeric" style="width:90px" /></div>
          <div class="fld"><label>Yield</label><input id="nYield" type="number" step="0.0001" style="width:100px" /></div>
          <div class="fld"><label>Men</label><input id="nMen" inputmode="numeric" style="width:80px" /></div>
          <div class="fld"><label>Sketch id</label><input id="nSketch" inputmode="numeric" style="width:100px" /></div>
          <div class="fld"><label>Due date</label><input id="nDue" type="date" style="width:150px" /></div>
          <div class="fld" style="flex:1;min-width:180px"><label>Notes</label><textarea id="nNotes"></textarea></div>
        </div>
        <div class="frow" style="margin-top:10px"><button class="btn sm" id="btnCreate" type="button">Create job</button></div>
      </div>
    </div>
  </div>`;
}
// The "Uncomplete jobs" card: active work only (completed=false → NOT Done, NOT Cancelled).
async function loadUncomplete() {
    setErr('');
    setBusy(true);
    try {
        const page = await fetchJobs({ completed: false });
        const items = page.items ?? [];
        $('#jobs').innerHTML = jobRowsHtml(items, (j) => dText(j.dueDate)) || '<tr><td colspan="5" class="muted">No active jobs.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} active`;
        $('#listSub').textContent = `${items.length} shown`;
        wireRows('#jobs');
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// The "Completed jobs" card: Done jobs (completed=true), optionally narrowed by an exact job / order #.
async function loadCompleted() {
    setErr('');
    setBusy(true);
    const search = v('#fSearch');
    try {
        const page = await fetchJobs(search ? { completed: true, search } : { completed: true });
        const items = page.items ?? [];
        $('#completedJobs').innerHTML = jobRowsHtml(items, (j) => dText(j.timeDateFinished))
            || `<tr><td colspan="5" class="muted">${search ? 'No completed job matches that number.' : 'No completed jobs.'}</td></tr>`;
        $('#completedSub').textContent = search ? `${items.length} match` : `${(page.totalCount ?? 0).toLocaleString()} total`;
        wireRows('#completedJobs');
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadJob(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const j = await client().getJob(id);
        selectedJob = id;
        $('#detail').classList.remove('disabled');
        $('#detailTitle').textContent = `Job #${id}`;
        $('#hdr').innerHTML = [
            ['Order', `${esc(j.orderAbcNum)} / ${esc(j.orderItemNum)}`], ['Line', esc(lineLabel(j.lineNum))],
            ['Yield', esc(num(j.materialYield))], ['Sketch', esc(j.sketchId)],
            ['Created', esc(dShow(j.createDate))], ['Due', esc(dShow(j.dueDate))],
            ['Started', esc(dShow(j.timeDateStarted))], ['Finished', esc(dShow(j.timeDateFinished))],
        ].map(([k, val]) => `<span><b>${k}</b>${val}</span>`).join('');
        setV('#pStatus', j.jobStatus);
        setV('#pMen', j.numberOfMenUsed);
        $('#pFinished').value = dLocal(j.timeDateFinished);
        setV('#pNotes', j.jobNotes);
        await loadChildren(id);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadChildren(id) {
    const c = client();
    const [coils, skids, scrap, partials, scans] = await Promise.all([
        c.getJobCoils(id), c.getJobSkids(id), c.getJobScrap(id), c.getJobPartialSkids(id), c.getJobScans(id),
    ]);
    const fill = (sel, html, cols, empty) => { $(sel).innerHTML = html || `<tr><td colspan="${cols}" class="muted">${empty}</td></tr>`; };
    fill('#tCoils', (coils ?? []).map((x) => `<tr><td class="mono">${esc(x.coilAbcNum)}</td><td>${statusChip('processCoilStatus', x.processCoilStatus)}</td>
    <td class="mono">${esc(dShow(x.processDate))}</td><td class="num">${esc(num(x.processEndWt))}</td><td class="num">${esc(num(x.processQuantity))}</td>
    <td>${esc(x.coilAlloy2)}</td><td class="num">${esc(x.coilGauge)}</td><td class="num">${esc(x.coilWidth)}</td></tr>`).join(''), 8, 'No coils.');
    fill('#tSkids', (skids ?? []).map((x) => `<tr><td class="mono">${esc(x.sheetSkidNum)}</td><td>${esc(x.sheetSkidDisplayNum)}</td>
    <td class="num">${esc(num(x.sheetNetWt))}</td><td class="num">${esc(num(x.sheetTareWt))}</td><td class="num">${esc(x.skidPieces)}</td>
    <td class="mono">${esc(dShow(x.skidDate))}</td></tr>`).join(''), 6, 'No finished skids.');
    fill('#tScrap', (scrap ?? []).map((x) => `<tr><td class="mono">${esc(x.scrapSkidNum)}</td><td>${esc(x.scrapAlloy2)}</td>
    <td>${esc(x.scrapTemper)}</td><td>${statusChip('scrapType', x.scrapType)}</td><td class="num">${esc(num(x.scrapNetWt))}</td>
    <td>${esc(x.scrapLocation)}</td><td>${statusChip('skidScrapStatus', x.skidScrapStatus)}</td></tr>`).join(''), 7, 'No scrap.');
    fill('#tPartials', (partials ?? []).map((x) => `<tr><td class="mono">${esc(x.sheetSkidNum)}</td><td class="num">${esc(num(x.partialSheetNetWt))}</td>
    <td class="num">${esc(x.partialSkidPieces)}</td><td>${esc(x.partialSkidLocation)}</td><td class="mono">${esc(dShow(x.partialSkidDate))}</td></tr>`).join(''), 5, 'No partials.');
    fill('#tScans', (scans ?? []).map((x) => `<tr><td class="mono">${esc(x.scanId)}</td><td class="mono">${esc(dShow(x.scanDatetime))}</td>
    <td>${esc(x.scanStation)}</td><td>${esc(x.note)}</td></tr>`).join(''), 4, 'No scans.');
}
// A patch can flip a job Done → refresh both lists so it moves from Uncomplete to Completed.
async function patch() {
    if (selectedJob == null)
        return;
    setErr('');
    setOk('');
    setBusy(true);
    const body = new JobPatch({
        jobStatus: v('#pStatus') ? Number(v('#pStatus')) : undefined,
        numberOfMenUsed: v('#pMen') ? Number(v('#pMen')) : undefined,
        timeDateFinished: v('#pFinished') ? new Date(v('#pFinished')) : undefined,
        jobNotes: v('#pNotes') || undefined,
    });
    try {
        await client().patchJob(selectedJob, body);
        setOk(`✓ Updated job #${selectedJob}.`);
        await Promise.all([loadUncomplete(), loadCompleted()]);
    }
    catch (e) {
        setErr(`Update failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function createJob() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new JobWrite({
        orderAbcNum: v('#nOrder') ? Number(v('#nOrder')) : undefined,
        orderItemNum: v('#nItem') ? Number(v('#nItem')) : undefined,
        lineNum: v('#nLine') ? Number(v('#nLine')) : undefined,
        jobStatus: v('#nStatus') ? Number(v('#nStatus')) : undefined,
        materialYield: v('#nYield') ? Number(v('#nYield')) : undefined,
        numberOfMenUsed: v('#nMen') ? Number(v('#nMen')) : undefined,
        sketchId: v('#nSketch') ? Number(v('#nSketch')) : undefined,
        dueDate: v('#nDue') ? new Date(v('#nDue')) : undefined,
        jobNotes: v('#nNotes') || undefined,
    });
    try {
        const created = await client().createJob(body);
        setOk(`✓ Created job #${created.abJobNum}.`);
        await loadUncomplete();
        if (created.abJobNum != null)
            await loadJob(created.abJobNum);
    }
    catch (e) {
        setErr(`Create failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function showTab(name) {
    ['coils', 'skids', 'scrap', 'partials', 'scans'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
}
(async () => {
    const main = await initShell({ active: 'jobs' });
    main.innerHTML = scaffold();
    $('#completedForm').addEventListener('submit', (e) => { e.preventDefault(); void loadCompleted(); });
    $('#btnRecent').addEventListener('click', () => { setV('#fSearch', ''); void loadCompleted(); });
    $('#btnPatch').addEventListener('click', () => void patch());
    $('#btnCreate').addEventListener('click', () => void createJob());
    ['coils', 'skids', 'scrap', 'partials', 'scans'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    showTab('coils');
    await Promise.all([loadUncomplete(), loadCompleted()]);
})();
