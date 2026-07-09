// ABIS Shifts — greenfield (Path C) module for shift records (legacy shift /
// shift_scheduler windows). A typed SPA on the Phase-2 API: line-filtered search +
// load → edit → save (create/replace). Through the NSwag-generated, compiler-checked
// client.
//
// Compiled by `tsc` to wwwroot/ui/app/shifts.js; served at /ui/shifts.html.
import { AbisClient, ShiftWrite } from './generated/abis-client.js';

import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client(): AbisClient {
  return new AbisClient('', { fetch: authFetch });
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };
const dtLocal = (d: Date | undefined): string => (d == null ? '' : d.toISOString().slice(0, 16));
const dtShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());

let editingId: number | null = null;

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const lineNum = v('#fLine') ? Number(v('#fLine')) : undefined;
  try {
    const page = await client().listShifts(1, 50, lineNum, 'startTime', 'desc');
    const rows = (page.items ?? []).map((s) => `
      <tr class="click" data-id="${s.shiftNum}">
        <td>${esc(s.shiftNum)}</td>
        <td>${esc(dtShow(s.startTime))}</td>
        <td>${esc(s.lineNum)}</td>
        <td>${esc(s.operatorInitial)}</td>
        <td>${esc(s.dtTotal)}</td>
        <td>${esc(s.shiftDataStatus)}</td>
      </tr>`).join('');
    $('#shifts').innerHTML = rows || '<tr><td colspan="6" class="muted">No matching shifts.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#shifts tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadShift(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadShift(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const s = await client().getShift(id);
    editingId = id;
    $('#formTitle').textContent = `Edit shift #${id}`;
    $<HTMLInputElement>('#hStart').value = dtLocal(s.startTime);
    $<HTMLInputElement>('#hEnd').value = dtLocal(s.endTime);
    setV('#hLine', s.lineNum); setV('#hSchedule', s.scheduleType); setV('#hDt', s.dtTotal);
    setV('#hOper', s.operatorInitial); setV('#hStatus', s.shiftDataStatus); setV('#hNote', s.note);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function newShift(): void {
  editingId = null;
  $('#formTitle').textContent = 'New shift';
  ['#hLine', '#hSchedule', '#hDt', '#hOper', '#hStatus', '#hNote'].forEach((id) => setV(id, ''));
  $<HTMLInputElement>('#hStart').value = ''; $<HTMLInputElement>('#hEnd').value = '';
  setOk(''); setErr('');
}

async function save(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
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
    } else {
      await client().updateShift(editingId, body);
      setOk(`✓ Saved shift #${editingId}.`);
    }
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function init(): Promise<void> {
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newShift);
  $('#btnSave').addEventListener('click', save);
  newShift();
  await search();
}

void initShell({ active: 'shifts', adopt: true }).then(init);
