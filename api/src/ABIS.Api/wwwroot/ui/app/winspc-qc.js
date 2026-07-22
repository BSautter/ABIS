// ABIS Dimensional QC — surfaces the plant WinSPC (SPC) dimensional measurements for an ABIS job
// or coil: the measured value, WinSPC's own spec window (LSL/target/USL), and the derived pass/fail.
// Read-only. Everything comes from the WinSPC connector (GET /api/winspc/health + /winspc/job|coil/*/qc),
// which is disabled until a read-only SQL login to the WinSPC box is wired up — the page says so plainly.
//
// Compiled by tsc to wwwroot/ui/app/winspc-qc.js; served at /ui/winspc-qc.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const val = (id) => $(id).value.trim();
const num = (v) => (v === null || v === undefined || v === '' ? '' : String(v));
const dText = (s) => {
    if (!s)
        return '';
    const d = new Date(s);
    return Number.isNaN(d.getTime()) ? String(s) : d.toLocaleString();
};
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Quality · WinSPC</div><h1>Dimensional QC</h1></div><div class="shift-tag" id="status">—</div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>
    <div id="banner"></div>

    <div class="card" style="margin-bottom:16px">
      <header><h2>Look up measurements</h2><span class="sub">from WinSPC, by job or coil</span></header>
      <div class="body">
        <div class="frow" style="align-items:flex-end">
          <div class="fld">
            <label>By</label>
            <select id="fKind" style="width:110px"><option value="job">Job #</option><option value="coil">Coil #</option></select>
          </div>
          <div class="fld"><label>Number</label><input id="fKey" style="width:180px" placeholder="e.g. 124346" /></div>
          <button class="btn sm" id="btnGo" type="button">Look up</button>
        </div>
      </div>
    </div>

    <div class="card">
      <header><h2>Results</h2><span class="sub" id="rollup"></span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:840px">
        <thead><tr>
          <th>Recorded</th><th>Part</th><th>Characteristic</th><th>Dimension</th>
          <th style="text-align:right">LSL</th><th style="text-align:right">Target</th><th style="text-align:right">Reading</th><th style="text-align:right">USL</th>
          <th>In spec</th>
        </tr></thead>
        <tbody id="rows"><tr><td colspan="9" class="muted">Enter a job or coil number and click Look up.</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}
async function loadStatus() {
    try {
        const r = await authFetch('/api/winspc/health');
        if (!r.ok) {
            $('#status').textContent = 'status unknown';
            return;
        }
        const h = await r.json();
        if (!h.enabled) {
            $('#status').textContent = 'not configured';
            $('#banner').innerHTML = `<div class="card" style="margin-bottom:16px;border-color:var(--warn,#b8860b)"><div class="body">
        The WinSPC connector is <b>not enabled yet</b>. Once a read-only SQL login to the WinSPC database is wired up
        (<code>WinSpc:Enabled=true</code> + connection string), dimensional QC will appear here.</div></div>`;
        }
        else if (!h.reachable) {
            $('#status').textContent = 'unreachable';
            $('#banner').innerHTML = `<div class="card" style="margin-bottom:16px;border-color:var(--crit,#b00)"><div class="body">
        WinSPC is enabled but not reachable: <code>${esc(h.error)}</code></div></div>`;
        }
        else {
            $('#status').textContent = 'WinSPC connected';
            $('#banner').innerHTML = '';
        }
    }
    catch { /* status is best-effort */ }
}
function specChip(inSpec) {
    if (inSpec === true)
        return '<span class="chip ok">In spec</span>';
    if (inSpec === false)
        return '<span class="chip crit">Out of spec</span>';
    return '<span class="chip mut">—</span>';
}
async function lookup() {
    const kind = $('#fKind').value;
    const key = val('#fKey');
    if (!key) {
        setErr('Enter a job or coil number.');
        return;
    }
    setErr('');
    $('#rows').innerHTML = '<tr><td colspan="9" class="muted">Loading…</td></tr>';
    try {
        const r = await authFetch(`/api/winspc/${kind}/${encodeURIComponent(key)}/qc`);
        if (r.status === 503) {
            setErr('WinSPC is not configured yet.');
            $('#rows').innerHTML = '';
            return;
        }
        if (!r.ok) {
            setErr(`Lookup failed (${r.status}).`);
            $('#rows').innerHTML = '';
            return;
        }
        const qc = await r.json();
        const rows = qc.readings ?? [];
        $('#rollup').textContent = rows.length
            ? `${qc.totalReadings} readings · ${qc.inSpecReadings} in spec · ${qc.outOfSpecReadings} out of spec`
            : '';
        $('#rows').innerHTML = rows.length ? rows.map((t) => `
      <tr${t.inSpec === false ? ' style="background:var(--crit-bg,rgba(200,0,0,.08))"' : ''}>
        <td class="mono">${esc(dText(t.readingAt))}</td>
        <td>${esc(t.partName)}</td>
        <td>${esc(t.characteristic)}</td>
        <td class="mono">${esc(t.dimension ?? '')}</td>
        <td class="mono" style="text-align:right">${esc(num(t.lsl))}</td>
        <td class="mono" style="text-align:right">${esc(num(t.target))}</td>
        <td class="mono" style="text-align:right"><b>${esc(num(t.reading))}</b></td>
        <td class="mono" style="text-align:right">${esc(num(t.usl))}</td>
        <td>${specChip(t.inSpec)}</td>
      </tr>`).join('') : `<tr><td colspan="9" class="muted">No WinSPC measurements found for ${esc(kind)} ${esc(key)}.</td></tr>`;
    }
    catch (e) {
        setErr(`Lookup failed: ${e.message}`);
        $('#rows').innerHTML = '';
    }
}
(async () => {
    const main = await initShell({ active: 'winspc-qc' });
    main.innerHTML = scaffold();
    $('#btnGo').addEventListener('click', () => void lookup());
    $('#fKey').addEventListener('keydown', (e) => { if (e.key === 'Enter')
        void lookup(); });
    await loadStatus();
})();
