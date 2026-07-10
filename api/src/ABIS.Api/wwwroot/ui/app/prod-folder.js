// ABIS Production Folder — the legacy w_production_folder, restyled to the design system in the
// shared shell (#4 polish). The job's documentation packet: a folder summary (header + coil/skid/
// note counts) and the job's e-folder notes (job_efolder_notes), with add. Typed NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/prod-folder.js; served at /ui/prod-folder.html.
import { AbisClient, JobFolderNoteWrite } from './generated/abis-client.js';
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
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const dt = (d) => (d == null ? '' : new Date(d).toLocaleString());
let job = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Folder</div><h1>Production folder</h1></div>
      <form id="jobForm" class="frow"><div class="fld"><label>Job #</label><input id="fJob" inputmode="numeric" style="width:120px" /></div><button class="btn sm" type="submit">Open folder</button></form>
    </div>
    <div id="err" class="err" style="margin-bottom:8px"></div>

    <div id="workarea" class="disabled">
      <div class="grid">
        <div class="stack"><div class="card">
          <header><h2>Folder summary</h2></header>
          <div class="body"><div class="kv" id="hdr"></div></div>
        </div></div>
        <div class="stack"><div class="card">
          <header><h2>E-folder notes</h2></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
            <thead><tr><th>Time</th><th>By</th><th>Note</th></tr></thead>
            <tbody id="tNotes"><tr><td colspan="3" class="muted">—</td></tr></tbody>
          </table></div>
          <div class="body">
            <div class="frow">
              <div class="fld"><label>User id</label><input id="nUser" inputmode="numeric" style="width:100px" /></div>
              <div class="fld" style="flex:1;min-width:200px"><label>Note</label><input id="nNotes" maxlength="2048" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnNote" type="button">Add note</button><span id="ok" class="ok-note"></span></div>
          </div>
        </div></div>
      </div>
    </div>
  </div>`;
}
async function loadFolder() {
    setErr('');
    setOk('');
    const id = v('#fJob') ? Number(v('#fJob')) : 0;
    if (!id) {
        setErr('Enter a job number.');
        return;
    }
    setBusy(true);
    try {
        const f = await client().getProductionFolder(id);
        job = id;
        $('#workarea').classList.remove('disabled');
        $('#hdr').innerHTML = `
      <span><b>Job</b>${esc(f.abJobNum)} · ${esc(lineLabel(f.lineNum))} · status ${esc(f.jobStatus)}</span>
      <span><b>Order / PO</b>${esc(f.orderAbcNum)} · ${esc(f.origCustomerPo)}</span>
      <span><b>Customer</b>${esc(f.customerShortName)}</span>
      <span><b>Contents</b>${esc(f.coilCount)} coil(s) · ${esc(f.skidCount)} skid(s) · ${esc(f.noteCount)} note(s)</span>`;
        await loadNotes();
    }
    catch (e) {
        setErr(`Load folder failed: ${e.message}`);
        job = null;
        $('#workarea').classList.add('disabled');
    }
    finally {
        setBusy(false);
    }
}
async function loadNotes() {
    if (job == null)
        return;
    const notes = await client().getJobFolderNotes(job);
    $('#tNotes').innerHTML = (notes ?? []).length ? (notes ?? []).map((n) => `<tr>
    <td class="mono">${esc(dt(n.timestamp))}</td><td>${esc(n.userName)}</td><td>${esc(n.notes)}</td></tr>`).join('')
        : '<tr><td colspan="3" class="muted">No notes yet.</td></tr>';
}
async function addNote() {
    if (job == null) {
        setErr('Load a folder first.');
        return;
    }
    if (!v('#nNotes')) {
        setErr('Enter a note.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    try {
        await client().addJobFolderNote(job, new JobFolderNoteWrite({
            userId: v('#nUser') ? Number(v('#nUser')) : undefined,
            notes: v('#nNotes'),
        }));
        setOk('✓ Note added.');
        setV('#nNotes', '');
        await loadNotes();
    }
    catch (e) {
        setErr(`Add note failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'prod-folder' });
    main.innerHTML = scaffold();
    $('#jobForm').addEventListener('submit', (e) => { e.preventDefault(); void loadFolder(); });
    $('#btnNote').addEventListener('click', () => void addNote());
})();
