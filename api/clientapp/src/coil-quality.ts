// ABIS Coil Quality — capture the per-coil quality header (material grade / dimensions / mill / PCC,
// legacy COIL_QUALITY) and its flaw map (COIL_QUALITY_FLAW_MAPPING). Look up a coil, edit + save the
// header, and add/remove flaw segments (start→end position + single-char code + handling). All via
// authFetch (GET/PUT /api/coils/{n}/quality, POST/DELETE .../quality/flaws).
//
// Compiled by tsc to wwwroot/ui/app/coil-quality.js; served at /ui/coil-quality.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const numOrUndef = (id: string): number | undefined => (val(id) ? Number(val(id)) : undefined);
const setV = (id: string, v: unknown) => { $<HTMLInputElement>(id).value = v == null ? '' : String(v); };

let coil = 0;

function scaffold(): string {
  const f = (id: string, label: string, w = 130, ph = '') =>
    `<div class="fld"><label>${label}</label><input id="${id}" style="width:${w}px" placeholder="${ph}" /></div>`;
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Production · Coil quality</div><h1>Coil quality</h1></div><div class="shift-tag" id="coiltag">—</div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Load a coil</h2><span class="sub">COIL_QUALITY</span></header>
      <div class="body"><div class="frow" style="align-items:flex-end">
        <div class="fld"><label>Coil ABC #</label><input id="fCoil" inputmode="numeric" style="width:140px" placeholder="coil abc" /></div>
        <button class="btn sm" id="btnLoad" type="button">Load</button>
      </div></div>
    </div>

    <div class="card" id="headerCard" style="margin-bottom:16px;display:none">
      <header><h2>Quality header</h2><span class="sub" id="hsub"></span></header>
      <div class="body">
        <div class="frow">
          ${f('hOrg', 'Coil org # *', 140, 'mill / org #')}
          ${f('hPart', 'Part #', 130)}
          ${f('hGrade', 'Material grade', 110)}
          ${f('hMill', 'Mill id', 90)}
          ${f('hPcc', 'PCC #', 120)}
          ${f('hRev', 'Rev', 60)}
          ${f('hWidth', 'Width', 90)}
          ${f('hWeight', 'Weight', 90)}
          ${f('hThick', 'Thickness', 90)}
          ${f('hLen', 'Net length', 90)}
          ${f('hPre', 'Pre-treat (Y/N)', 90)}
          ${f('hSampling', 'Sampling (Y/N)', 90)}
        </div>
        <div class="frow" style="margin-top:10px;align-items:center">
          <button class="btn sm" id="btnSaveHeader" type="button">Save header</button>
          <span id="ok" class="ok-note"></span>
        </div>
      </div>
    </div>

    <div class="card" id="flawCard" style="display:none">
      <header><h2>Flaw map</h2><span class="sub" id="fsub"></span></header>
      <div class="body">
        <div class="frow" style="align-items:flex-end">
          ${f('nStart', 'Start pos', 90)}
          ${f('nEnd', 'End pos', 90)}
          ${f('nCode', 'Flaw code', 80, '1 char')}
          ${f('nHand', 'Handling', 80)}
          <button class="btn sm" id="btnAddFlaw" type="button">Add flaw</button>
        </div>
      </div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
        <thead><tr><th style="text-align:right">Start</th><th style="text-align:right">End</th><th>Code</th><th>Handling</th><th></th></tr></thead>
        <tbody id="flaws"><tr><td colspan="5" class="muted">No flaws recorded.</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}

interface Flaw { startingPosition?: number; endingPosition?: number; flawCode?: string; handlingCode?: string; }

async function load(): Promise<void> {
  const c = val('#fCoil');
  if (!c) { setErr('Enter a coil ABC #.'); return; }
  setErr(''); setOk('');
  try {
    const r = await authFetch(`/api/coils/${Number(c)}/quality`);
    if (!r.ok) { setErr(`Load failed (${r.status}).`); return; }
    coil = Number(c);
    $('#coiltag').textContent = `coil ${coil}`;
    const d = await r.json();
    const h = d.header;
    // Prefill the header form (blank when no header yet — the org # can be entered to create one).
    setV('#hOrg', h?.coilOrgNum); setV('#hPart', h?.partNum); setV('#hGrade', h?.materialGrade);
    setV('#hMill', h?.millId); setV('#hPcc', h?.pccNumber); setV('#hRev', h?.revisionLevel);
    setV('#hWidth', h?.coilWidth); setV('#hWeight', h?.coilWeight); setV('#hThick', h?.materialThikness);
    setV('#hLen', h?.netCoilLength); setV('#hPre', h?.preTreatmentFlag); setV('#hSampling', h?.samplingRequired);
    $('#hsub').textContent = h ? 'recorded' : 'not yet recorded';
    $('#headerCard').style.display = ''; $('#flawCard').style.display = '';
    renderFlaws(d.flaws ?? []);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
}

function renderFlaws(flaws: Flaw[]): void {
  $('#fsub').textContent = flaws.length ? `${flaws.length} flaw${flaws.length === 1 ? '' : 's'}` : '';
  $('#flaws').innerHTML = flaws.length ? flaws.map((f) => `
    <tr>
      <td class="mono" style="text-align:right">${esc(f.startingPosition)}</td>
      <td class="mono" style="text-align:right">${esc(f.endingPosition)}</td>
      <td class="mono">${esc(f.flawCode)}</td><td>${esc(f.handlingCode)}</td>
      <td><button class="btn sm ghost" data-del="${esc(f.startingPosition)}|${esc(f.endingPosition)}|${esc(f.flawCode)}" type="button">Remove</button></td>
    </tr>`).join('') : '<tr><td colspan="5" class="muted">No flaws recorded.</td></tr>';
  $('#flaws').querySelectorAll<HTMLButtonElement>('[data-del]').forEach((b) =>
    b.addEventListener('click', () => void delFlaw(b.getAttribute('data-del')!)));
}

async function saveHeader(): Promise<void> {
  if (!coil) return;
  if (!val('#hOrg')) { setErr('Coil org # is required.'); return; }
  setErr(''); setOk('');
  try {
    const body = {
      coilOrgNum: val('#hOrg'), partNum: val('#hPart') || undefined, materialGrade: val('#hGrade') || undefined,
      millId: val('#hMill') || undefined, pccNumber: val('#hPcc') || undefined, revisionLevel: val('#hRev') || undefined,
      coilWidth: numOrUndef('#hWidth'), coilWeight: numOrUndef('#hWeight'), materialThikness: numOrUndef('#hThick'),
      netCoilLength: numOrUndef('#hLen'), preTreatmentFlag: val('#hPre') || undefined, samplingRequired: val('#hSampling') || undefined,
    };
    const r = await authFetch(`/api/coils/${coil}/quality`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) { setErr(r.status === 404 ? `Coil ${coil} not found.` : `Save failed (${r.status}).`); return; }
    setOk('✓ Header saved.'); $('#hsub').textContent = 'recorded';
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
}

async function addFlaw(): Promise<void> {
  if (!coil) return;
  const s = numOrUndef('#nStart'), en = numOrUndef('#nEnd'), code = val('#nCode');
  if (s === undefined || en === undefined || !code) { setErr('Start, end, and flaw code are required.'); return; }
  setErr('');
  try {
    const body = { startingPosition: s, endingPosition: en, flawCode: code, handlingCode: val('#nHand') || undefined };
    const r = await authFetch(`/api/coils/${coil}/quality/flaws`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!r.ok) {
      const why = r.status === 404 ? `coil ${coil} not found` : r.status === 400 ? 'invalid flaw (check start ≤ end, 1-char code)' : `error ${r.status}`;
      setErr(`Add failed: ${why}.`); return;
    }
    ['#nStart', '#nEnd', '#nCode', '#nHand'].forEach((i) => setV(i, ''));
    await load();
  } catch (e) { setErr(`Add failed: ${(e as Error).message}`); }
}

async function delFlaw(key: string): Promise<void> {
  const [s, en, code] = key.split('|');
  const qs = new URLSearchParams({ startingPosition: s, endingPosition: en, flawCode: code });
  try {
    const r = await authFetch(`/api/coils/${coil}/quality/flaws?${qs.toString()}`, { method: 'DELETE' });
    if (!r.ok && r.status !== 404) { setErr(`Remove failed (${r.status}).`); return; }
    await load();
  } catch (e) { setErr(`Remove failed: ${(e as Error).message}`); }
}

(async () => {
  const main = await initShell({ active: 'coil-quality' });
  main.innerHTML = scaffold();
  $('#btnLoad').addEventListener('click', () => void load());
  $<HTMLInputElement>('#fCoil').addEventListener('keydown', (e) => { if ((e as KeyboardEvent).key === 'Enter') void load(); });
  $('#btnSaveHeader').addEventListener('click', () => void saveHeader());
  $('#btnAddFlaw').addEventListener('click', () => void addFlaw());
})();
