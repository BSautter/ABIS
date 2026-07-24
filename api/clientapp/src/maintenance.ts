// ABIS Maintenance — the legacy maintenance window, restyled to the design system in the shared
// shell (#4 polish). Filterable maintenance-log search and a full load → edit → save form (create
// or replace). The replace path re-sends all writable fields (probDateTime/probDetails/author are
// NOT NULL) so a partial edit can't blank them. Through the NSwag-generated, compiler-checked client.
//
// Compiled by tsc to wwwroot/ui/app/maintenance.js; served at /ui/maintenance.html.
import { AbisClient, MaintLogWrite, PmDefinition, PmWrite, PmActionWrite, PmCompleteWrite } from './generated/abis-client.js';
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
  const tab = (id: string, label: string) => `<button id="tab-${id}" type="button">${label}</button>`;
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Maintenance</div><h1>Maintenance</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="tabs">${tab('due', 'PM due board')}${tab('pms', 'PM schedules')}${tab('logs', 'Logs')}</div>
    <div id="err" class="err" style="margin:8px 0"></div>

    <div id="pane-due">
      <div class="card" style="margin-bottom:16px"><div class="body">
        <form id="dueForm" class="frow" style="align-items:flex-end">
          <div class="fld"><label>Due within (days)</label><input id="dWithin" inputmode="numeric" value="7" style="width:120px" /></div>
          <div class="fld"><label>Dept id</label><input id="dDept" list="deptList" placeholder="any" style="width:90px" /></div>
          <button class="btn sm" type="submit">Refresh</button>
          <span class="muted" style="margin-left:auto">Active PMs only — overdue first. Retired PMs (status 0) never appear.</span>
        </form>
      </div></div>
      <div class="card">
        <header><h2>Due &amp; overdue</h2><span class="sub" id="cDue"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:820px">
          <thead><tr><th>PM#</th><th>Equipment</th><th>What</th><th>Freq</th><th>Due</th><th class="num">Days</th><th>State</th><th></th></tr></thead>
          <tbody id="tDue"><tr><td colspan="8" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div>
    </div>

    <div id="pane-pms" style="display:none">
      <div class="card" style="margin-bottom:16px"><div class="body">
        <form id="pmForm" class="frow" style="align-items:flex-end">
          <div class="fld"><label>Dept id</label><input id="pDept" list="deptList" placeholder="any" style="width:90px" /></div>
          <div class="fld"><label>Status</label><select id="pStatus" style="width:120px"><option value="">any</option><option value="1">active</option><option value="0">retired</option></select></div>
          <button class="btn sm" type="submit">Search</button>
          <button class="btn sm ghost" id="btnNewPm" type="button">New PM</button>
        </form>
      </div></div>
      <div class="grid">
        <div class="stack"><div class="card">
          <header><h2>PM schedules</h2><span class="sub" id="cPms"></span></header>
          <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
            <thead><tr><th>PM#</th><th>Equipment</th><th>What</th><th>Due</th><th>State</th></tr></thead>
            <tbody id="tPms"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
          </table></div>
        </div></div>
        <div class="stack"><div class="card">
          <header><h2 id="pmTitle">New PM</h2><span class="sub" id="pmOk" class="ok-note"></span></header>
          <div class="body" id="pmDetail"></div>
        </div></div>
      </div>
    </div>

    <div id="pane-logs" style="display:none">
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Status</label><input id="fStatus" placeholder="e.g. Completed" style="width:130px" /></div>
        <div class="fld"><label>Dept id</label><input id="fDept" list="deptList" placeholder="id" style="width:90px" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
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

// ---- Preventive maintenance ----------------------------------------------------------

// The server derives dueBucket; the UI only picks a tone for it.
const dueChip = (b: string | undefined): string => {
  const cls = b === 'overdue' ? 'crit' : b === 'due' ? 'warn' : b === 'scheduled' ? 'ok' : 'mut';
  return `<span class="chip ${cls}">${esc(b ?? '—')}</span>`;
};
// A PM's equipment path, deepest level first, skipping levels it doesn't target.
const equipPath = (p: PmDefinition): string =>
  [p.systemEquipment, p.subsystemEquipment, p.itemDevice].filter(Boolean).join(' › ') || '—';
const dOnly = (d: Date | undefined): string => (d == null ? '' : d.toLocaleDateString());
const dInput = (d: Date | undefined): string =>
  (d == null ? '' : new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10));

let pmEditingId: number | null = null;

async function loadDue(): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const within = v('#dWithin') ? Number(v('#dWithin')) : 7;
    const dept = v('#dDept') ? Number(v('#dDept')) : undefined;
    const rows = await client().getPmsDue(within, dept);
    $('#tDue').innerHTML = rows.length ? rows.map((p) => `<tr>
      <td class="mono">${esc(p.pmId)}</td><td>${esc(equipPath(p))}</td><td>${esc(p.pmNotice)}</td>
      <td>${esc(p.maintFreq)}</td><td class="mono">${esc(dOnly(p.nextDueDate))}</td>
      <td class="num mono">${esc(p.daysUntilDue)}</td><td>${dueChip(p.dueBucket)}</td>
      <td><button class="btn xs ghost" type="button" data-open="${esc(p.pmId)}">Open</button></td></tr>`).join('')
      : '<tr><td colspan="8" class="muted">Nothing due in this window.</td></tr>';
    const overdue = rows.filter((r) => r.dueBucket === 'overdue').length;
    $('#cDue').textContent = `${rows.length} due · ${overdue} overdue`;
    document.querySelectorAll<HTMLButtonElement>('#tDue [data-open]').forEach((b) =>
      b.addEventListener('click', () => { showTab('pms'); void loadPm(Number(b.dataset.open)); }));
  } catch (e) { setErr(`Due board failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadPms(): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const dept = v('#pDept') ? Number(v('#pDept')) : undefined;
    const st = $<HTMLSelectElement>('#pStatus').value;
    const page = await client().listPms(1, 100, dept, st === '' ? undefined : Number(st), undefined, undefined, undefined);
    const items = page.items ?? [];
    $('#tPms').innerHTML = items.length ? items.map((p) => `
      <tr class="click" data-id="${p.pmId}">
        <td class="mono">${esc(p.pmId)}</td><td>${esc(equipPath(p))}</td><td>${esc(p.pmNotice)}</td>
        <td class="mono">${esc(dOnly(p.nextDueDate))}</td><td>${dueChip(p.dueBucket)}</td></tr>`).join('')
      : '<tr><td colspan="5" class="muted">No PMs match.</td></tr>';
    $('#cPms').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#tPms tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadPm(Number(tr.dataset.id))));
  } catch (e) { setErr(`PM list failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// The detail pane is rendered as one block per PM (edit form + checklist + complete + history)
// so switching PMs can't leave stale rows behind.
function pmForm(p: PmDefinition | null): string {
  const val = (x: unknown) => esc(x ?? '');
  return `
    <div class="frow">
      <div class="fld"><label>What (notice)</label><input id="xNotice" style="min-width:220px" value="${val(p?.pmNotice)}" /></div>
      <div class="fld"><label>Frequency</label><input id="xFreq" style="width:110px" value="${val(p?.maintFreq)}" /></div>
      <div class="fld"><label>Shift</label><input id="xShift" list="shiftList" style="width:90px" value="${val(p?.pmshift)}" /></div>
      <div class="fld"><label>Status</label><select id="xStatus" style="width:110px">
        <option value="1"${p?.pmStatus !== 0 ? ' selected' : ''}>active</option>
        <option value="0"${p?.pmStatus === 0 ? ' selected' : ''}>retired</option></select></div>
    </div>
    <div class="frow" style="margin-top:8px">
      <div class="fld"><label>System</label><select id="xSys" style="min-width:150px"></select></div>
      <div class="fld"><label>Subsystem</label><select id="xSub" style="min-width:150px"></select></div>
      <div class="fld"><label>Item / device</label><select id="xItem" style="min-width:150px"></select></div>
    </div>
    <div class="frow" style="margin-top:8px">
      <div class="fld"><label>Dept id</label><input id="xDept" list="deptList" style="width:90px" value="${val(p?.groupDepartmentId)}" /></div>
      <div class="fld"><label>Craft</label><select id="xCraft" style="min-width:130px"></select></div>
      <div class="fld"><label>Assigned to</label><input id="xGroup" style="width:130px" value="${val(p?.assignedToGroup)}" /></div>
      <div class="fld"><label>Reference</label><input id="xRef" style="width:130px" value="${val(p?.pmReference)}" /></div>
    </div>
    <div class="frow" style="margin-top:8px">
      <div class="fld"><label>Days between</label><input id="xBetween" type="number" step="1" style="width:110px" value="${val(p?.daysBetween)}" /></div>
      <div class="fld"><label>Times / year</label><input id="xPerYear" type="number" step="1" style="width:110px" value="${val(p?.numOfTimesPerYear)}" /></div>
      <div class="fld"><label>Next due</label><input id="xNextDue" type="date" value="${dInput(p?.nextDueDate)}" /></div>
      <div class="fld"><label>Mins/unit</label><input id="xMins" type="number" step="1" style="width:100px" value="${val(p?.minsPerUnit)}" /></div>
      <div class="fld"><label>Units</label><input id="xUnits" type="number" step="1" style="width:90px" value="${val(p?.numOfUnits)}" /></div>
    </div>
    <div class="frow" style="margin-top:10px;align-items:center">
      <button class="btn sm" id="btnSavePm" type="button">${p ? 'Save PM' : 'Create PM'}</button>
      ${p ? '<button class="btn sm ghost" id="btnDeletePm" type="button">Delete</button>' : ''}
      <span class="muted">${p ? `Last done ${esc(dOnly(p.pmCompleted)) || '—'} by ${esc(p.completedBy) || '—'}` : 'Interval drives the next due date when a completion is recorded.'}</span>
    </div>`;
}

function completeAndChecklist(p: PmDefinition): string {
  return `
    <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:18px 0 8px">Record a completion</h3>
    <div class="frow" style="align-items:flex-end">
      <div class="fld"><label>Completed by</label><input id="cBy" style="width:130px" /></div>
      <div class="fld"><label>On</label><input id="cOn" type="date" value="${dInput(new Date())}" /></div>
      <div class="fld"><label>Next due (optional)</label><input id="cNext" type="date" /></div>
      <div class="fld" style="flex:1;min-width:160px"><label>Notes</label><input id="cNotes" /></div>
      <button class="btn sm" id="btnComplete" type="button">Complete</button>
    </div>
    <p class="muted" style="margin:6px 0 0">Leave <em>next due</em> blank to advance automatically from ${
      p.daysBetween ? `days-between (${esc(p.daysBetween)})` : p.numOfTimesPerYear ? `${esc(p.numOfTimesPerYear)}×/year` : 'the interval — this PM has none, so the stored date is kept'}.</p>

    <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:18px 0 8px">Checklist</h3>
    <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
      <thead><tr><th>Action</th><th>Details</th><th></th></tr></thead>
      <tbody id="tActions"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody>
    </table></div>
    <div class="frow" style="margin-top:8px;align-items:flex-end">
      <div class="fld"><label>Action</label><input id="aItem" style="width:170px" /></div>
      <div class="fld" style="flex:1;min-width:160px"><label>Details</label><input id="aDetails" /></div>
      <button class="btn sm ghost" id="btnAddAction" type="button">Add</button>
    </div>

    <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:18px 0 8px">Completion history</h3>
    <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
      <thead><tr><th>Completed</th><th>By</th><th>Notes</th></tr></thead>
      <tbody id="tHistory"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody>
    </table></div>`;
}

async function loadPm(id: number | null): Promise<void> {
  setErr(''); $('#pmOk').textContent = ''; setBusy(true);
  try {
    const p = id == null ? null : await client().getPm(id);
    pmEditingId = p?.pmId ?? null;
    $('#pmTitle').textContent = p ? `PM #${p.pmId}` : 'New PM';
    $('#pmDetail').innerHTML = pmForm(p) + (p ? completeAndChecklist(p) : '');
    await fillEquipmentPickers(p);
    $('#btnSavePm').addEventListener('click', () => void savePm());
    document.querySelector('#btnDeletePm')?.addEventListener('click', () => void deletePm());
    if (p && pmEditingId != null) {
      const pmId = pmEditingId;
      $('#btnComplete').addEventListener('click', () => void completePm());
      $('#btnAddAction').addEventListener('click', () => void addAction());
      await Promise.all([loadActions(pmId), loadHistory(pmId)]);
    }
  } catch (e) { setErr(`PM load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Cascading pickers: system -> subsystem -> item. Each level reloads its children on change.
async function fillEquipmentPickers(p: PmDefinition | null): Promise<void> {
  const opt = (id: unknown, label: unknown, sel: unknown) =>
    `<option value="${esc(id)}"${String(id) === String(sel ?? '') ? ' selected' : ''}>${esc(label)}</option>`;
  const blank = '<option value="">—</option>';
  try {
    const [systems, crafts] = await Promise.all([client().getSystemEquipment(undefined), client().getTitleCrafts()]);
    $('#xSys').innerHTML = blank + systems.map((s) => opt(s.sysEquipmentId, s.systemEquipmentName, p?.sysEquipmentId)).join('');
    $('#xCraft').innerHTML = blank + crafts.map((c) => opt(c.titleCraftId, c.titleCraftName, p?.titleCraftId)).join('');
    const fillSubs = async (sysId: string, selSub?: unknown, selItem?: unknown) => {
      const subs = sysId ? await client().getSubsystemEquipment(Number(sysId)) : [];
      $('#xSub').innerHTML = blank + subs.map((s) => opt(s.subsysEquipmentId, s.subsystemEquipmentName, selSub)).join('');
      await fillItems($<HTMLSelectElement>('#xSub').value, selItem);
    };
    const fillItems = async (subId: string, selItem?: unknown) => {
      const items = subId ? await client().getItemDevices(Number(subId)) : [];
      $('#xItem').innerHTML = blank + items.map((i) => opt(i.itemDeviceId, i.itemDeviceName, selItem)).join('');
    };
    await fillSubs(String(p?.sysEquipmentId ?? ''), p?.subsysEquipmentId, p?.itemDeviceId);
    $('#xSys').addEventListener('change', () => void fillSubs($<HTMLSelectElement>('#xSys').value));
    $('#xSub').addEventListener('change', () => void fillItems($<HTMLSelectElement>('#xSub').value));
  } catch { /* pickers are best-effort; the PM still saves without them */ }
}

function pmBody(): PmWrite {
  const numOr = (id: string) => (v(id) ? Number(v(id)) : undefined);
  return new PmWrite({
    pmNotice: v('#xNotice') || undefined,
    maintFreq: v('#xFreq') || undefined,
    pmshift: v('#xShift') || undefined,
    pmStatus: Number($<HTMLSelectElement>('#xStatus').value),
    sysEquipmentId: numOr('#xSys'), subsysEquipmentId: numOr('#xSub'), itemDeviceId: numOr('#xItem'),
    groupDepartmentId: numOr('#xDept'), titleCraftId: numOr('#xCraft'),
    assignedToGroup: v('#xGroup') || undefined, pmReference: v('#xRef') || undefined,
    daysBetween: numOr('#xBetween'), numOfTimesPerYear: numOr('#xPerYear'),
    nextDueDate: v('#xNextDue') ? new Date(v('#xNextDue')) : undefined,
    minsPerUnit: numOr('#xMins'), numOfUnits: numOr('#xUnits'),
  });
}

async function savePm(): Promise<void> {
  setErr(''); setBusy(true);
  try {
    if (pmEditingId == null) {
      const created = await client().createPm(pmBody());
      $('#pmOk').textContent = `✓ Created PM #${created.pmId}.`;
      await Promise.all([loadPms(), loadPm(created.pmId ?? null)]);
    } else {
      await client().updatePm(pmEditingId, pmBody());
      $('#pmOk').textContent = `✓ Saved PM #${pmEditingId}.`;
      await Promise.all([loadPms(), loadDue()]);
    }
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function deletePm(): Promise<void> {
  if (pmEditingId == null) return;
  if (!confirm(`Delete PM #${pmEditingId} and its checklist? (Refused if completions exist — retire it instead.)`)) return;
  setErr(''); setBusy(true);
  try {
    await client().deletePm(pmEditingId);
    await loadPm(null);
    await Promise.all([loadPms(), loadDue()]);
  } catch (e) { setErr(`Delete failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function completePm(): Promise<void> {
  if (pmEditingId == null) return;
  if (!v('#cBy')) { setErr('Completed by is required.'); return; }
  setErr(''); setBusy(true);
  try {
    const r = await client().completePm(pmEditingId, new PmCompleteWrite({
      completedBy: v('#cBy'),
      completedDate: v('#cOn') ? new Date(v('#cOn')) : undefined,
      nextDueDate: v('#cNext') ? new Date(v('#cNext')) : undefined,
      completedNotes: v('#cNotes') || undefined,
    }));
    // Say how the schedule moved and why, rather than making the user re-read the form.
    const how = r.advanceBasis === 'none' ? 'no interval, date unchanged' : `via ${esc(r.advanceBasis)}`;
    $('#pmOk').textContent = `✓ Completed. Next due ${dOnly(r.nextDueDate) || '—'} (${how}).`;
    await Promise.all([loadPm(pmEditingId), loadPms(), loadDue()]);
  } catch (e) { setErr(`Complete failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadActions(pmId: number): Promise<void> {
  const rows = await client().getPmActions(pmId);
  $('#tActions').innerHTML = rows.length ? rows.map((a) => `<tr>
    <td>${esc(a.actionItems)}</td><td>${esc(a.itemDetails)}</td>
    <td><button class="btn xs ghost" type="button" data-del-action="${esc(a.pmActionId)}">Remove</button></td></tr>`).join('')
    : '<tr><td colspan="3" class="muted">No checklist items.</td></tr>';
  document.querySelectorAll<HTMLButtonElement>('#tActions [data-del-action]').forEach((b) =>
    b.addEventListener('click', () => void removeAction(pmId, Number(b.dataset.delAction))));
}

async function addAction(): Promise<void> {
  if (pmEditingId == null || !v('#aItem')) { setErr('Action text is required.'); return; }
  setErr('');
  try {
    await client().addPmAction(pmEditingId, new PmActionWrite({ actionItems: v('#aItem'), itemDetails: v('#aDetails') || undefined }));
    setV('#aItem', ''); setV('#aDetails', '');
    await loadActions(pmEditingId);
  } catch (e) { setErr(`Add failed: ${(e as Error).message}`); }
}

async function removeAction(pmId: number, actionId: number): Promise<void> {
  try { await client().deletePmAction(pmId, actionId); await loadActions(pmId); }
  catch (e) { setErr(`Remove failed: ${(e as Error).message}`); }
}

async function loadHistory(pmId: number): Promise<void> {
  const rows = await client().getPmCompletions(pmId);
  $('#tHistory').innerHTML = rows.length ? rows.map((h) => `<tr>
    <td class="mono">${esc(dOnly(h.completedDate))}</td><td>${esc(h.completedBy)}</td>
    <td>${esc(h.completedNotes)}</td></tr>`).join('')
    : '<tr><td colspan="3" class="muted">Never completed.</td></tr>';
}

function showTab(name: string): void {
  ['due', 'pms', 'logs'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
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
  // PM shift codes as a datalist hint (best-effort, like the departments).
  try {
    const shifts = await client().getPmShifts();
    const dl = document.createElement('datalist');
    dl.id = 'shiftList';
    dl.innerHTML = (shifts ?? []).map((s) => `<option value="${esc(s)}"></option>`).join('');
    document.body.appendChild(dl);
  } catch { /* best-effort */ }
  ['due', 'pms', 'logs'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $<HTMLFormElement>('#dueForm').addEventListener('submit', (e) => { e.preventDefault(); void loadDue(); });
  $<HTMLFormElement>('#pmForm').addEventListener('submit', (e) => { e.preventDefault(); void loadPms(); });
  $('#btnNewPm').addEventListener('click', () => void loadPm(null));
  $('#btnNew').addEventListener('click', newLog);
  $('#btnSave').addEventListener('click', () => void save());
  newLog();
  showTab('due');
  await Promise.all([loadDue(), loadPms(), search(), loadPm(null)]);
})();
