// ABIS Test Results — the mechanical / tensile test-result register (pst_test_result). Enter a posted
// result for a coil (YTS/UTS/elongation/n/r + thickness/width at a sample position) and browse the list.
// The write endpoint (POST /api/test-results) is newer than the committed NSwag client, so the list + the
// create both go through authFetch (like the shipping packing-list and the 997 ingest).
//
// Compiled by tsc to wwwroot/ui/app/test-results.js; served at /ui/test-results.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const val = (id) => $(id).value.trim();
const numOrUndef = (id) => (val(id) ? Number(val(id)) : undefined);
const dText = (s) => {
    if (!s)
        return '';
    const d = new Date(s);
    return Number.isNaN(d.getTime()) ? String(s) : d.toLocaleString();
};
function scaffold() {
    const cell = (id, label, w = 90, ph = '') => `<div class="fld"><label>${label}</label><input id="${id}" inputmode="decimal" style="width:${w}px" placeholder="${ph}" /></div>`;
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Quality · Mechanical</div><h1>Test results</h1></div><div class="shift-tag" id="count">—</div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Record a result</h2><span class="sub">pst_test_result</span></header>
      <div class="body">
        <div class="frow">
          ${cell('fCoil', 'Coil ABC #', 120, 'coil abc')}
          ${cell('fPos', 'Position', 90, 'e.g. C / E')}
          ${cell('fType', 'Test type', 90, '#')}
          ${cell('fYts', 'YTS', 80)}
          ${cell('fUts', 'UTS', 80)}
          ${cell('fElong', 'Elong', 80)}
          ${cell('fN', 'n', 70)}
          ${cell('fR', 'r', 70)}
          ${cell('fThick', 'Thickness', 90)}
          ${cell('fWidth', 'Width', 90)}
        </div>
        <div class="frow" style="margin-top:10px;align-items:center">
          <button class="btn sm" id="btnAdd" type="button">Add result</button>
          <span id="ok" class="ok-note"></span>
        </div>
      </div>
    </div>

    <div class="card">
      <header><h2>Posted results</h2><span class="sub" id="listSub"></span></header>
      <div class="body">
        <form id="filterForm" class="frow">
          <div class="fld"><label>Position</label><input id="qPos" style="width:110px" placeholder="any" /></div>
          <div class="fld"><label>Test type</label><input id="qType" inputmode="numeric" style="width:100px" placeholder="any" /></div>
          <button class="btn sm ghost" type="submit">Filter</button>
        </form>
      </div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:720px">
        <thead><tr><th>Coil</th><th>Pos</th><th>Type</th><th style="text-align:right">YTS</th><th style="text-align:right">UTS</th><th style="text-align:right">Elong</th><th style="text-align:right">n</th><th style="text-align:right">r</th><th style="text-align:right">Thick</th><th style="text-align:right">Width</th><th>Recorded</th></tr></thead>
        <tbody id="rows"><tr><td colspan="11" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}
async function loadResults() {
    setErr('');
    const qs = new URLSearchParams({ page: '1', pageSize: '50' });
    if (val('#qPos'))
        qs.set('position', val('#qPos'));
    if (val('#qType'))
        qs.set('testType', val('#qType'));
    try {
        const r = await authFetch(`/api/test-results?${qs.toString()}`);
        if (!r.ok) {
            setErr(`Load failed (${r.status}).`);
            return;
        }
        const page = await r.json();
        const items = page.items ?? [];
        const n = (v) => `<td class="mono" style="text-align:right">${esc(v ?? '')}</td>`;
        $('#rows').innerHTML = items.length ? items.map((t) => `
      <tr>
        <td class="mono">${esc(t.coilAbcNum)}</td><td class="mono">${esc(t.position)}</td><td class="mono">${esc(t.testType)}</td>
        ${n(t.ytsVal)}${n(t.utsVal)}${n(t.elongVal)}${n(t.nVal)}${n(t.rVal)}${n(t.thickness)}${n(t.width)}
        <td class="mono">${esc(dText(t.createdDate))}</td>
      </tr>`).join('') : '<tr><td colspan="11" class="muted">No posted results.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} results`;
        $('#listSub').textContent = `${items.length} shown`;
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
}
async function addResult() {
    const coil = val('#fCoil');
    const pos = val('#fPos');
    if (!coil || !pos) {
        setErr('Coil ABC # and Position are required.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const body = {
            coilAbcNum: Number(coil), position: pos, testType: numOrUndef('#fType'),
            ytsVal: numOrUndef('#fYts'), utsVal: numOrUndef('#fUts'), elongVal: numOrUndef('#fElong'),
            nVal: numOrUndef('#fN'), rVal: numOrUndef('#fR'), thickness: numOrUndef('#fThick'), width: numOrUndef('#fWidth'),
        };
        const r = await authFetch('/api/test-results', {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
        });
        if (!r.ok) {
            const why = r.status === 404 ? `coil ${coil} not found` : r.status === 400 ? 'coil # and position are required' : `error ${r.status}`;
            setErr(`Add failed: ${why}.`);
            return;
        }
        setOk('✓ Result recorded.');
        ['#fYts', '#fUts', '#fElong', '#fN', '#fR', '#fThick', '#fWidth'].forEach((id) => { $(id).value = ''; });
        await loadResults();
    }
    catch (e) {
        setErr(`Add failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'test-results' });
    main.innerHTML = scaffold();
    $('#btnAdd').addEventListener('click', () => void addResult());
    $('#filterForm').addEventListener('submit', (e) => { e.preventDefault(); void loadResults(); });
    await loadResults();
})();
