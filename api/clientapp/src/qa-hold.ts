// ABIS QA Hold — the coil quality-hold console (legacy w_qa_coil). Place a coil on QA hold
// (coil_status → 11) or release it back to service, list the coils currently on hold, and read a
// coil's full hold/release audit trail (COIL_TRACK_QA). Hold/release/history go through authFetch
// (POST /api/coils/{n}/qa-hold, /qa-release, GET /qa-history); the on-hold list reuses the paged
// coils endpoint filtered to status 11.
//
// Compiled by tsc to wwwroot/ui/app/qa-hold.js; served at /ui/qa-hold.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusText } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const dText = (s: string | null | undefined): string => {
  if (!s) return '';
  const d = new Date(s);
  return Number.isNaN(d.getTime()) ? String(s) : d.toLocaleString();
};

interface CoilRow {
  coilAbcNum?: number; coilOrgNum?: string; coilAlloy2?: string; coilTemper?: string;
  coilLocation?: string; customerId?: number; coilStatus?: number;
}
interface QaTrack {
  coilAbcNum?: number; coilTrackDate?: string; coilPreStatus?: number; coilCurStatus?: number;
  coilModifiedBy?: string; note?: string;
}

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Quality · QA hold</div><h1>QA hold</h1></div><div class="shift-tag" id="count">—</div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Hold or release a coil</h2><span class="sub">coil_status 11 · COIL_TRACK_QA</span></header>
      <div class="body">
        <div class="frow">
          <div class="fld"><label>Coil ABC #</label><input id="fCoil" inputmode="numeric" style="width:130px" placeholder="coil abc" /></div>
          <div class="fld"><label>Checked by</label><input id="fBy" style="width:150px" placeholder="your name" /></div>
          <div class="fld" style="flex:1;min-width:200px"><label>Reason / note</label><input id="fNote" style="width:100%" placeholder="why (required)" /></div>
        </div>
        <div class="frow" style="margin-top:10px;align-items:center">
          <button class="btn sm" id="btnHold" type="button">Place on hold</button>
          <button class="btn sm ghost" id="btnRelease" type="button">Release</button>
          <span id="ok" class="ok-note"></span>
        </div>
      </div>
    </div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Coils on QA hold</h2><span class="sub" id="listSub"></span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
        <thead><tr><th>Coil</th><th>Org #</th><th>Alloy</th><th>Temper</th><th>Location</th><th>Cust</th><th></th></tr></thead>
        <tbody id="rows"><tr><td colspan="7" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div class="card">
      <header><h2>Hold history</h2><span class="sub" id="histSub">pick a coil</span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
        <thead><tr><th>When</th><th>From</th><th>To</th><th>By</th><th>Note</th></tr></thead>
        <tbody id="hist"><tr><td colspan="5" class="muted">Enter a coil # and click “History”, or pick a coil above.</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}

async function loadHeld(): Promise<void> {
  setErr('');
  try {
    const r = await authFetch('/api/coils?status=11&page=1&pageSize=100');
    if (!r.ok) { setErr(`Load failed (${r.status}).`); return; }
    const page = await r.json();
    const items: CoilRow[] = page.items ?? [];
    $('#rows').innerHTML = items.length ? items.map((c) => `
      <tr data-coil="${esc(c.coilAbcNum)}">
        <td class="mono">${esc(c.coilAbcNum)}</td><td class="mono">${esc(c.coilOrgNum)}</td>
        <td>${esc(c.coilAlloy2)}</td><td>${esc(c.coilTemper)}</td><td>${esc(c.coilLocation)}</td><td class="mono">${esc(c.customerId)}</td>
        <td><button class="btn sm ghost" data-act="pick" data-coil="${esc(c.coilAbcNum)}" type="button">History</button></td>
      </tr>`).join('') : '<tr><td colspan="7" class="muted">No coils are on QA hold.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? items.length).toLocaleString()} on hold`;
    $('#listSub').textContent = `${items.length} shown`;
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
}

async function loadHistory(coil: number): Promise<void> {
  try {
    const r = await authFetch(`/api/coils/${coil}/qa-history`);
    if (!r.ok) { setErr(`History failed (${r.status}).`); return; }
    const rows: QaTrack[] = await r.json();
    $('#histSub').textContent = `coil ${coil} · ${rows.length} event${rows.length === 1 ? '' : 's'}`;
    $('#hist').innerHTML = rows.length ? rows.map((t) => `
      <tr>
        <td class="mono">${esc(dText(t.coilTrackDate))}</td>
        <td>${esc(statusText('coilStatus', t.coilPreStatus))}</td>
        <td>${esc(statusText('coilStatus', t.coilCurStatus))}</td>
        <td>${esc(t.coilModifiedBy)}</td><td>${esc(t.note)}</td>
      </tr>`).join('') : `<tr><td colspan="5" class="muted">No QA history for coil ${coil}.</td></tr>`;
  } catch (e) { setErr(`History failed: ${(e as Error).message}`); }
}

async function transition(kind: 'hold' | 'release'): Promise<void> {
  const coil = val('#fCoil');
  const by = val('#fBy');
  const note = val('#fNote');
  if (!coil) { setErr('Coil ABC # is required.'); return; }
  if (!by || !note) { setErr('Checked by and a reason/note are both required.'); return; }
  setErr(''); setOk(''); setBusy(true);
  try {
    const r = await authFetch(`/api/coils/${Number(coil)}/qa-${kind}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ modifiedBy: by, note }),
    });
    if (!r.ok) {
      let why = `error ${r.status}`;
      if (r.status === 404) why = `coil ${coil} not found`;
      else if (r.status === 409) { const p = await r.json().catch(() => null); why = p?.detail ?? p?.title ?? 'not allowed in the coil’s current state'; }
      else if (r.status === 400) why = 'checked-by and note are required';
      setErr(`${kind === 'hold' ? 'Hold' : 'Release'} failed: ${why}.`);
      return;
    }
    setOk(kind === 'hold' ? '✓ Coil placed on QA hold.' : '✓ Coil released.');
    $<HTMLInputElement>('#fNote').value = '';
    await Promise.all([loadHeld(), loadHistory(Number(coil))]);
  } catch (e) { setErr(`${kind === 'hold' ? 'Hold' : 'Release'} failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'qa-hold' });
  main.innerHTML = scaffold();
  $('#btnHold').addEventListener('click', () => void transition('hold'));
  $('#btnRelease').addEventListener('click', () => void transition('release'));
  // Delegate the per-row "History" button and row click to load that coil's audit.
  $('#rows').addEventListener('click', (e) => {
    const t = (e.target as HTMLElement).closest('[data-coil]') as HTMLElement | null;
    if (!t) return;
    const coil = Number(t.getAttribute('data-coil'));
    if (coil) { $<HTMLInputElement>('#fCoil').value = String(coil); void loadHistory(coil); }
  });
  await loadHeld();
})();
