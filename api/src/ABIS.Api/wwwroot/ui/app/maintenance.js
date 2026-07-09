// ABIS Maintenance — greenfield (Path C) module replacing the legacy maintenance
// window. A typed SPA on the Phase-2 API: filterable maintenance-log search and a
// full load → edit → save form (create or replace). The replace path re-sends all
// writable fields (probDateTime/probDetails/author are NOT NULL) so a partial
// edit can't blank them. Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/maintenance.js; served at /ui/maintenance.html.
import { AbisClient, MaintLogWrite } from './generated/abis-client.js';
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
    const status = v('#fStatus') || undefined;
    const dept = v('#fDept') ? Number(v('#fDept')) : undefined;
    try {
        const page = await client().listMaintLogs(1, 50, status, dept, undefined, undefined);
        const rows = (page.items ?? []).map((m) => `
      <tr class="click" data-id="${m.maintLogId}">
        <td>${esc(m.maintLogId)}</td>
        <td>${esc(m.maintLogStatus)}</td>
        <td>${esc(m.groupDepartmentId)}</td>
        <td>${esc(m.probDetails)}</td>
        <td>${esc(m.author)}</td>
      </tr>`).join('');
        $('#logs').innerHTML = rows || '<tr><td colspan="5" class="muted">No matching logs.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#logs tr.click').forEach((tr) => tr.addEventListener('click', () => loadLog(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadLog(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const m = await client().getMaintLog(id);
        editingId = id;
        $('#formTitle').textContent = `Edit maintenance log #${id}`;
        setV('#mStatus', m.maintLogStatus);
        setV('#mDept', m.groupDepartmentId);
        setV('#mSystem', m.systemEquipment);
        setV('#mSubsystem', m.subsystemEquipment);
        setV('#mItem', m.itemDevice);
        $('#mProbDt').value = dtLocal(m.probDateTime);
        setV('#mDetails', m.probDetails);
        setV('#mActions', m.actions);
        setV('#mAuthor', m.author);
        setV('#mReportedBy', m.reportedBy);
        setV('#mAssignedTo', m.assignedTo);
        setV('#mCompletedBy', m.completedBy);
        setV('#mLabor', m.laborHours);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newLog() {
    editingId = null;
    $('#formTitle').textContent = 'New maintenance log';
    ['#mStatus', '#mDept', '#mSystem', '#mSubsystem', '#mItem', '#mDetails', '#mActions',
        '#mAuthor', '#mReportedBy', '#mAssignedTo', '#mCompletedBy', '#mLabor'].forEach((id) => setV(id, ''));
    $('#mProbDt').value = new Date().toISOString().slice(0, 16);
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new MaintLogWrite({
        maintLogStatus: v('#mStatus') || undefined,
        groupDepartmentId: v('#mDept') ? Number(v('#mDept')) : undefined,
        systemEquipment: v('#mSystem') || undefined,
        subsystemEquipment: v('#mSubsystem') || undefined,
        itemDevice: v('#mItem') || undefined,
        probDateTime: v('#mProbDt') ? new Date(v('#mProbDt')) : undefined,
        probDetails: v('#mDetails') || undefined,
        actions: v('#mActions') || undefined,
        author: v('#mAuthor') || undefined,
        reportedBy: v('#mReportedBy') || undefined,
        assignedTo: v('#mAssignedTo') || undefined,
        completedBy: v('#mCompletedBy') || undefined,
        laborHours: v('#mLabor') ? Number(v('#mLabor')) : undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createMaintLog(body);
            setOk(`✓ Created log #${created.maintLogId}.`);
        }
        else {
            await client().updateMaintLog(editingId, body);
            setOk(`✓ Saved log #${editingId}.`);
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
async function init() {
    // Department dropdown options as <datalist> hints.
    try {
        const depts = await client().listGroupDepartments();
        $('#deptList').innerHTML = (depts ?? []).map((d) => `<option value="${esc(d.groupDepartmentId)}">${esc(d.groupDepartmentName)}</option>`).join('');
    }
    catch { /* dropdown is best-effort */ }
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newLog);
    $('#btnSave').addEventListener('click', save);
    newLog();
    await search();
}
void initShell({ active: 'maintenance', adopt: true }).then(init);
