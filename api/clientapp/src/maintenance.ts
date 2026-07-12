// ABIS Maintenance — the legacy maintenance window, restyled to the design system in the shared
// shell (#4 polish). Filterable maintenance-log search and a full load → edit → save form (create
// or replace). The replace path re-sends all writable fields (probDateTime/probDetails/author are
// NOT NULL) so a partial edit can't blank them. Through the NSwag-generated, compiler-checked client.
//
// Compiled by tsc to wwwroot/ui/app/maintenance.js; served at /ui/maintenance.html.
import { AbisClient, MaintLogWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };
// Local-time formatter for datetime-local inputs — toISOString() would emit UTC and shift the value by
// the whole timezone offset on show/re-save (see downtime.ts).
const dtLocal = (d: Date | undefined): string =>
  (d == null ? '' : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16));
const chip = (s: unknown): string => `<span class="chip mut">${esc(s ?? '—')}</span>`;

let editingId: number | null = null;

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Maintenance · Logs</div><h1>Maintenance</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Status</label><input id="fStatus" placeholder="e.g. Completed" style="width:130px" /></div>
        <div class="fld"><label>Dept id</label><input id="fDept" list="deptList" placeholder="id" style="width:90px" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Maintenance logs</h2></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:460px">
          <thead><tr><th>Log#</th><th>Status</th><th>Dept</th><th>Problem</th><th>Author</th></tr></thead>
          <tbody id="logs"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New maintenance log</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>Status</label><input id="mStatus" placeholder="e.g. Completed" style="width:130px" /></div>
            <div class="fld"><label>Department</label><input id="mDept" list="deptList" style="width:120px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>System</label><input id="mSystem" style="width:120px" /></div>
            <div class="fld"><label>Subsystem</label><input id="mSubsystem" style="width:120px" /></div>
            <div class="fld"><label>Item / device</label><input id="mItem" style="width:120px" /></div>
          </div>
          <div class="frow" style="margin-top:8px"><div class="fld"><label>Problem time</label><input id="mProbDt" type="datetime-local" /></div></div>
          <div class="frow" style="margin-top:8px"><div class="fld" style="flex:1;min-width:200px"><label>Problem details</label><input id="mDetails" /></div></div>
          <div class="frow" style="margin-top:8px"><div class="fld" style="flex:1;min-width:200px"><label>Actions</label><input id="mActions" /></div></div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Author</label><input id="mAuthor" style="width:110px" /></div>
            <div class="fld"><label>Reported by</label><input id="mReportedBy" style="width:110px" /></div>
            <div class="fld"><label>Assigned to</label><input id="mAssignedTo" style="width:110px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Completed by</label><input id="mCompletedBy" style="width:110px" /></div>
            <div class="fld"><label>Labor hours</label><input id="mLabor" type="number" step="0.1" style="width:100px" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnSave" type="button">Save</button>
            <button class="btn sm ghost" id="btnNew" type="button">New</button>
            <span id="ok" class="ok-note"></span>
          </div>
        </div>
      </div></div>
    </div>
  </div>
  <datalist id="deptList"></datalist>`;
}

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const status = v('#fStatus') || undefined;
  const dept = v('#fDept') ? Number(v('#fDept')) : undefined;
  try {
    const page = await client().listMaintLogs(1, 50, status, dept, undefined, undefined);
    const items = page.items ?? [];
    $('#logs').innerHTML = items.length ? items.map((m) => `
      <tr class="click" data-id="${m.maintLogId}">
        <td class="mono">${esc(m.maintLogId)}</td><td>${chip(m.maintLogStatus)}</td><td class="mono">${esc(m.groupDepartmentId)}</td>
        <td>${esc(m.probDetails)}</td><td>${esc(m.author)}</td>
      </tr>`).join('') : '<tr><td colspan="5" class="muted">No matching logs.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#logs tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadLog(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadLog(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const m = await client().getMaintLog(id);
    editingId = id;
    $('#formTitle').textContent = `Edit maintenance log #${id}`;
    setV('#mStatus', m.maintLogStatus); setV('#mDept', m.groupDepartmentId);
    setV('#mSystem', m.systemEquipment); setV('#mSubsystem', m.subsystemEquipment);
    setV('#mItem', m.itemDevice); $<HTMLInputElement>('#mProbDt').value = dtLocal(m.probDateTime);
    setV('#mDetails', m.probDetails); setV('#mActions', m.actions);
    setV('#mAuthor', m.author); setV('#mReportedBy', m.reportedBy);
    setV('#mAssignedTo', m.assignedTo); setV('#mCompletedBy', m.completedBy);
    setV('#mLabor', m.laborHours);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function newLog(): void {
  editingId = null;
  $('#formTitle').textContent = 'New maintenance log';
  ['#mStatus', '#mDept', '#mSystem', '#mSubsystem', '#mItem', '#mDetails', '#mActions',
    '#mAuthor', '#mReportedBy', '#mAssignedTo', '#mCompletedBy', '#mLabor'].forEach((id) => setV(id, ''));
  $<HTMLInputElement>('#mProbDt').value = dtLocal(new Date());
  setOk(''); setErr('');
}

async function save(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
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
    } else {
      await client().updateMaintLog(editingId, body);
      setOk(`✓ Saved log #${editingId}.`);
    }
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'maintenance' });
  main.innerHTML = scaffold();
  // Department dropdown options as <datalist> hints.
  try {
    const depts = await client().listGroupDepartments();
    $('#deptList').innerHTML = (depts ?? []).map((d) =>
      `<option value="${esc(d.groupDepartmentId)}">${esc(d.groupDepartmentName)}</option>`).join('');
  } catch { /* dropdown is best-effort */ }
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newLog);
  $('#btnSave').addEventListener('click', () => void save());
  newLog();
  await search();
})();
