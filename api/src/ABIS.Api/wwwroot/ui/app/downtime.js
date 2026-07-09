// ABIS Downtime — greenfield (Path C) module replacing the legacy downtime
// window. A typed SPA on the Phase-2 API: downtime-instance search (by job /
// shift) and a full load → edit → save form (create or replace). Through the
// NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/downtime.js; served at /ui/downtime.html.
import { AbisClient, DowntimeInstanceWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const dtLocal = (d) => (d == null ? '' : d.toISOString().slice(0, 16));
let editingId = null;
async function search() {
    setErr('');
    setBusy(true);
    const job = v('#fJob') ? Number(v('#fJob')) : undefined;
    const shift = v('#fShift') ? Number(v('#fShift')) : undefined;
    try {
        const page = await client().listDowntime(1, 50, job, shift, undefined, undefined);
        const rows = (page.items ?? []).map((d) => `
      <tr class="click" data-id="${d.instanceNum}">
        <td>${esc(d.instanceNum)}</td>
        <td>${esc(d.abJobNum)}</td>
        <td>${esc(d.lineNum)}</td>
        <td>${esc(d.startingTime?.toString().slice(0, 16).replace('T', ' '))}</td>
        <td>${esc(d.note)}</td>
      </tr>`).join('');
        $('#instances').innerHTML = rows || '<tr><td colspan="5" class="muted">No matching downtime.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#instances tr.click').forEach((tr) => tr.addEventListener('click', () => loadInstance(Number(tr.dataset.id))));
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
    $('#dStart').value = new Date().toISOString().slice(0, 16);
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
function init() {
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newInstance);
    $('#btnSave').addEventListener('click', save);
    newInstance();
    void search();
}
void initShell({ active: 'downtime', adopt: true }).then(init);
