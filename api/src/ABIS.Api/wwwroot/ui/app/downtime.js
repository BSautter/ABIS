// ABIS Downtime — the legacy downtime window, restyled to the design system in the shared shell
// (#4 polish). Downtime-instance search (by job / shift) and a full load → edit → save form
// (create or replace). Through the NSwag-generated, compiler-checked client.
//
// Compiled by tsc to wwwroot/ui/app/downtime.js; served at /ui/downtime.html.
import { AbisClient, DowntimeInstanceWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { lineLabel } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
// Format a Date for a datetime-local input / display in the operator's LOCAL time. toISOString() would
// emit UTC wall-clock, which a datetime-local shows and re-parses as local → a whole-timezone-offset shift
// (e.g. a US plant logs downtime 5–6h off, corrupting duration/efficiency reports).
const dtLocal = (d) => (d == null ? '' : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
let editingId = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Production · Downtime</div><h1>Downtime</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Job #</label><input id="fJob" inputmode="numeric" placeholder="ab job" style="width:110px" /></div>
        <div class="fld"><label>Shift #</label><input id="fShift" inputmode="numeric" placeholder="shift" style="width:90px" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Downtime instances</h2></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:460px">
          <thead><tr><th>#</th><th>Job</th><th>Line</th><th>Type</th><th>Start</th><th>Note</th></tr></thead>
          <tbody id="instances"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New downtime instance</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>Job #</label><input id="dJob" inputmode="numeric" style="width:100px" /></div>
            <div class="fld"><label>Line #</label><input id="dLine" inputmode="numeric" style="width:90px" /></div>
            <div class="fld"><label>Shift #</label><input id="dShift" inputmode="numeric" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Start</label><input id="dStart" type="datetime-local" /></div>
            <div class="fld"><label>End</label><input id="dEnd" type="datetime-local" /></div>
          </div>
          <div class="frow" style="margin-top:8px"><div class="fld" style="flex:1;min-width:200px"><label>Note</label><input id="dNote" /></div></div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnSave" type="button">Save</button>
            <button class="btn sm ghost" id="btnNew" type="button">New</button>
            <span id="ok" class="ok-note"></span>
          </div>
        </div>
      </div></div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    const job = v('#fJob') ? Number(v('#fJob')) : undefined;
    const shift = v('#fShift') ? Number(v('#fShift')) : undefined;
    try {
        const page = await client().listDowntime(1, 50, job, shift, undefined, undefined);
        const items = page.items ?? [];
        $('#instances').innerHTML = items.length ? items.map((d) => `
      <tr class="click" data-id="${d.instanceNum}">
        <td class="mono">${esc(d.instanceNum)}</td><td class="mono">${esc(d.abJobNum)}</td><td class="mono">${esc(lineLabel(d.lineNum))}</td>
        <td>${esc(d.downtimeType ?? '—')}</td>
        <td class="mono">${esc(dtLocal(d.startingTime).replace('T', ' '))}</td><td>${esc(d.note)}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="muted">No matching downtime.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#instances tr.click').forEach((tr) => tr.addEventListener('click', () => void loadInstance(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadInstance(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const d = await client().getDowntimeInstance(id);
        editingId = id;
        $('#formTitle').textContent = `Edit downtime #${id}`;
        setV('#dJob', d.abJobNum);
        setV('#dLine', d.lineNum);
        setV('#dShift', d.shiftNum);
        $('#dStart').value = dtLocal(d.startingTime);
        $('#dEnd').value = dtLocal(d.endingTime);
        setV('#dNote', d.note);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newInstance() {
    editingId = null;
    $('#formTitle').textContent = 'New downtime instance';
    ['#dJob', '#dLine', '#dShift', '#dNote'].forEach((id) => setV(id, ''));
    $('#dStart').value = dtLocal(new Date());
    $('#dEnd').value = '';
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new DowntimeInstanceWrite({
        abJobNum: v('#dJob') ? Number(v('#dJob')) : undefined,
        lineNum: v('#dLine') ? Number(v('#dLine')) : undefined,
        startingTime: v('#dStart') ? new Date(v('#dStart')) : undefined,
        endingTime: v('#dEnd') ? new Date(v('#dEnd')) : undefined,
        note: v('#dNote') || undefined,
        shiftNum: v('#dShift') ? Number(v('#dShift')) : undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createDowntimeInstance(body);
            setOk(`✓ Created downtime #${created.instanceNum}.`);
        }
        else {
            await client().updateDowntimeInstance(editingId, body);
            setOk(`✓ Saved downtime #${editingId}.`);
        }
        await search();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'downtime' });
    main.innerHTML = scaffold();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newInstance);
    $('#btnSave').addEventListener('click', () => void save());
    newInstance();
    await search();
})();
