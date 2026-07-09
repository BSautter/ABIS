// ABIS QA Results — mechanical test results (legacy qa window), restyled to the design system in
// the shared shell (#4 polish). Posted + in-progress working set with test-type / position /
// date-range filters. Read-only. Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/qa-results.js; served at /ui/qa-results.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const dec = (v) => (v == null ? '' : v.toFixed(2));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const val = (id) => $(id).value.trim();
const dateOrUndef = (id) => (val(id) ? new Date(val(id)) : undefined);
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Quality · Mechanical tests</div><h1>QA results</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="filterForm" class="frow">
        <div class="fld"><label>Test type</label><input id="fType" inputmode="numeric" style="width:100px" placeholder="any" /></div>
        <div class="fld"><label>Position</label><input id="fPosition" style="width:100px" placeholder="any" /></div>
        <div class="fld"><label>From</label><input id="fFrom" type="date" /></div>
        <div class="fld"><label>To</label><input id="fTo" type="date" /></div>
        <label style="display:flex;align-items:center;gap:8px;font-size:13px;color:var(--ink)"><input type="checkbox" id="fInProgress" style="width:auto" /> In-progress</label>
        <button class="btn sm" type="submit">Load</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="card">
      <header><h2>Results</h2><span class="sub">YTS / UTS / elong / n / r / thickness / width</span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:720px">
        <thead><tr><th>Date</th><th>Type</th><th>Pos</th><th class="num">YTS</th><th class="num">UTS</th><th class="num">Elong</th><th class="num">n</th><th class="num">r</th><th class="num">Thick</th><th class="num">Width</th></tr></thead>
        <tbody id="results"><tr><td colspan="10" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}
function render(rows, total) {
    $('#results').innerHTML = rows.length ? rows.map((t) => `
    <tr><td class="mono">${esc(t.createdDate?.toISOString().slice(0, 10))}</td><td class="mono">${esc(t.testType)}</td><td>${esc(t.position)}</td>
      <td class="num">${dec(t.yts)}</td><td class="num">${dec(t.uts)}</td><td class="num">${dec(t.elong)}</td>
      <td class="num">${dec(t.n)}</td><td class="num">${dec(t.r)}</td><td class="num">${dec(t.thickness)}</td><td class="num">${dec(t.width)}</td></tr>`).join('')
        : '<tr><td colspan="10" class="muted">No matching results.</td></tr>';
    $('#count').textContent = `${(total ?? 0).toLocaleString()} results`;
}
async function load() {
    setErr('');
    setBusy(true);
    const testType = val('#fType') ? Number(val('#fType')) : undefined;
    const position = val('#fPosition') || undefined;
    const from = dateOrUndef('#fFrom');
    const to = dateOrUndef('#fTo');
    const inProgress = $('#fInProgress').checked;
    try {
        if (inProgress) {
            const page = await client().listTempTestResults(1, 100, testType, position, from, to, undefined, undefined);
            render((page.items ?? []).map((t) => ({
                createdDate: t.createdDate, testType: t.testType, position: t.position,
                yts: t.yts, uts: t.uts, elong: t.elongation, n: t.n, r: t.r, thickness: t.thickness, width: t.width,
            })), page.totalCount);
        }
        else {
            const page = await client().listTestResults(1, 100, testType, position, from, to, undefined, undefined);
            render((page.items ?? []).map((t) => ({
                createdDate: t.createdDate, testType: t.testType, position: t.position,
                yts: t.ytsVal, uts: t.utsVal, elong: t.elongVal, n: t.nVal, r: t.rVal, thickness: t.thickness, width: t.width,
            })), page.totalCount);
        }
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'qa-results' });
    main.innerHTML = scaffold();
    $('#filterForm').addEventListener('submit', (e) => { e.preventDefault(); void load(); });
    $('#fInProgress').addEventListener('change', () => void load());
    await load();
})();
