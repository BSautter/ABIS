// ABIS Sketches — master-data for sketch/tooling records (legacy sketch window), restyled to the
// design system in the shared shell (#4 polish). Status-filtered search + load → edit → save
// (create/replace, re-sending the required sketchName). Through the NSwag-generated client.
//
// Compiled by tsc to wwwroot/ui/app/sketches.js; served at /ui/sketches.html.
import { AbisClient, SketchWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const trunc = (s, n = 60) => (s && s.length > n ? s.slice(0, n) + '…' : s ?? '');
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let editingId = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Commercial · Tooling</div><h1>Sketches</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow"><div class="fld"><label>Status</label><input id="fStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div><button class="btn sm" type="submit">Search</button></form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Sketches</h2></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
          <thead><tr><th>Id</th><th>Name</th><th>Status</th><th>Notes</th></tr></thead>
          <tbody id="sketches"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New sketch</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld" style="flex:1;min-width:140px"><label>Name *</label><input id="sName" maxlength="16" placeholder="required" /></div>
            <div class="fld"><label>Status</label><input id="sStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:8px"><div class="fld" style="flex:1;min-width:200px"><label>System note</label><input id="sSysNote" maxlength="255" /></div></div>
          <div class="frow" style="margin-top:8px"><div class="fld" style="flex:1;min-width:200px"><label>Notes</label><textarea id="sNotes" maxlength="1024" rows="3"></textarea></div></div>
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
    const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
    try {
        const page = await client().listSketches(1, 50, status, undefined, undefined);
        const items = page.items ?? [];
        $('#sketches').innerHTML = items.length ? items.map((s) => `
      <tr class="click" data-id="${s.sketchId}">
        <td class="mono">${esc(s.sketchId)}</td><td>${esc(s.sketchName)}</td><td>${chip(s.sketchStatus)}</td><td>${esc(trunc(s.sketchNotes))}</td>
      </tr>`).join('') : '<tr><td colspan="4" class="muted">No matching sketches.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#sketches tr.click').forEach((tr) => tr.addEventListener('click', () => void loadSketch(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadSketch(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const s = await client().getSketch(id);
        editingId = id;
        $('#formTitle').textContent = `Edit sketch #${id}`;
        setV('#sName', s.sketchName);
        setV('#sStatus', s.sketchStatus);
        setV('#sSysNote', s.sketchSysNote);
        setV('#sNotes', s.sketchNotes);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newSketch() {
    editingId = null;
    $('#formTitle').textContent = 'New sketch';
    ['#sName', '#sStatus', '#sSysNote', '#sNotes'].forEach((id) => setV(id, ''));
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new SketchWrite({
        sketchName: v('#sName') || undefined,
        sketchNotes: v('#sNotes') || undefined,
        sketchSysNote: v('#sSysNote') || undefined,
        sketchStatus: v('#sStatus') ? Number(v('#sStatus')) : undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createSketch(body);
            setOk(`✓ Created sketch #${created.sketchId}.`);
        }
        else {
            await client().updateSketch(editingId, body);
            setOk(`✓ Saved sketch #${editingId}.`);
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
    const main = await initShell({ active: 'sketches' });
    main.innerHTML = scaffold();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newSketch);
    $('#btnSave').addEventListener('click', () => void save());
    newSketch();
    await search();
})();
