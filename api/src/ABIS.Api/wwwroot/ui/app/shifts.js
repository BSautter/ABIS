// ABIS Shifts — shift records (legacy shift / shift_scheduler), restyled to the design system in
// the shared shell (#4 polish). Line-filtered search → load → edit → save (create/replace). Typed
// calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/shifts.js; served at /ui/shifts.html.
import { AbisClient, ShiftWrite } from './generated/abis-client.js';
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
const dtLocal = (d) => (d == null ? '' : d.toISOString().slice(0, 16));
const dtShow = (d) => (d == null ? '' : d.toLocaleString());
let editingId = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Shifts</div><h1>Shifts</h1></div>
      <div class="shift-tag" id="count">—</div>
    </div>

    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Line #</label><input id="fLine" inputmode="numeric" style="width:110px" placeholder="any" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Shift records</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
          <thead><tr><th>Shift</th><th>Start</th><th>Line</th><th>Operator</th><th class="num">DT total</th><th>Status</th></tr></thead>
          <tbody id="shifts"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New shift</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>Start time</label><input id="hStart" type="datetime-local" /></div>
            <div class="fld"><label>End time</label><input id="hEnd" type="datetime-local" /></div>
            <div class="fld"><label>Line #</label><input id="hLine" inputmode="numeric" style="width:90px" /></div>
            <div class="fld"><label>Schedule type</label><input id="hSchedule" inputmode="numeric" style="width:110px" /></div>
            <div class="fld"><label>Downtime total</label><input id="hDt" type="number" step="0.01" style="width:120px" /></div>
            <div class="fld"><label>Operator initials</label><input id="hOper" maxlength="10" style="width:120px" /></div>
            <div class="fld"><label>Data status</label><input id="hStatus" inputmode="numeric" style="width:100px" /></div>
            <div class="fld" style="flex:1;min-width:180px"><label>Note</label><textarea id="hNote" maxlength="1024"></textarea></div>
          </div>
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
    const lineNum = v('#fLine') ? Number(v('#fLine')) : undefined;
    try {
        const page = await client().listShifts(1, 50, lineNum, 'startTime', 'desc');
        const items = page.items ?? [];
        $('#shifts').innerHTML = items.length ? items.map((s) => `
      <tr class="click" data-id="${s.shiftNum}">
        <td class="mono">${esc(s.shiftNum)}</td><td class="mono">${esc(dtShow(s.startTime))}</td>
        <td class="mono">${esc(lineLabel(s.lineNum))}</td><td>${esc(s.operatorInitial)}</td>
        <td class="num">${esc(s.dtTotal)}</td><td>${statusChip('shiftDataStatus', s.shiftDataStatus)}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="muted">No matching shifts.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} shifts`;
        $('#listSub').textContent = `${items.length} shown`;
        document.querySelectorAll('#shifts tr.click').forEach((tr) => tr.addEventListener('click', () => void loadShift(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadShift(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const s = await client().getShift(id);
        editingId = id;
        $('#formTitle').textContent = `Edit shift #${id}`;
        $('#hStart').value = dtLocal(s.startTime);
        $('#hEnd').value = dtLocal(s.endTime);
        setV('#hLine', s.lineNum);
        setV('#hSchedule', s.scheduleType);
        setV('#hDt', s.dtTotal);
        setV('#hOper', s.operatorInitial);
        setV('#hStatus', s.shiftDataStatus);
        setV('#hNote', s.note);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newShift() {
    editingId = null;
    $('#formTitle').textContent = 'New shift';
    ['#hLine', '#hSchedule', '#hDt', '#hOper', '#hStatus', '#hNote'].forEach((id) => setV(id, ''));
    $('#hStart').value = '';
    $('#hEnd').value = '';
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new ShiftWrite({
        startTime: v('#hStart') ? new Date(v('#hStart')) : undefined,
        endTime: v('#hEnd') ? new Date(v('#hEnd')) : undefined,
        lineNum: v('#hLine') ? Number(v('#hLine')) : undefined,
        scheduleType: v('#hSchedule') ? Number(v('#hSchedule')) : undefined,
        dtTotal: v('#hDt') ? Number(v('#hDt')) : undefined,
        operatorInitial: v('#hOper') || undefined,
        shiftDataStatus: v('#hStatus') ? Number(v('#hStatus')) : undefined,
        note: v('#hNote') || undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createShift(body);
            setOk(`✓ Created shift #${created.shiftNum}.`);
        }
        else {
            await client().updateShift(editingId, body);
            setOk(`✓ Saved shift #${editingId}.`);
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
    const main = await initShell({ active: 'shifts' });
    main.innerHTML = scaffold();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newShift);
    $('#btnSave').addEventListener('click', () => void save());
    newShift();
    await search();
})();
