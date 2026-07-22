// ABIS Dimensional QC — surfaces the plant WinSPC (SPC) dimensional measurements for an ABIS job
// or coil: the measured value, WinSPC's own spec window (LSL/target/USL), and the derived pass/fail.
// Read-only. Everything comes from the WinSPC connector (GET /api/winspc/health + /winspc/job|coil/*/qc),
// which is disabled until a read-only SQL login to the WinSPC box is wired up — the page says so plainly.
// A trend chart (SVG run chart vs the spec band) renders below the table for a chosen characteristic.
//
// Compiled by tsc to wwwroot/ui/app/winspc-qc.js; served at /ui/winspc-qc.html.
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const num = (v: unknown): string => (v === null || v === undefined || v === '' ? '' : String(v));
const trimNum = (n: number): string => String(Math.round(n * 10000) / 10000);
const dText = (s: string | null | undefined): string => {
  if (!s) return '';
  const d = new Date(s);
  return Number.isNaN(d.getTime()) ? String(s) : d.toLocaleString();
};
const shortDt = (s: string | null | undefined): string => {
  if (!s) return '';
  const d = new Date(s);
  return Number.isNaN(d.getTime()) ? String(s) : d.toLocaleString([], { month: 'numeric', day: 'numeric', hour: 'numeric', minute: '2-digit' });
};
const firstNum = (xs: (number | undefined)[]): number | null => {
  for (const x of xs) if (typeof x === 'number' && Number.isFinite(x)) return x;
  return null;
};

interface Reading {
  readingAt?: string; partName?: string; characteristic?: string; dimension?: string;
  reading?: number; lsl?: number; target?: number; usl?: number; units?: string; inSpec?: boolean | null;
}
interface Qc {
  key?: string; keyKind?: string; totalReadings?: number; inSpecReadings?: number; outOfSpecReadings?: number;
  readings?: Reading[];
}

let lastReadings: Reading[] = [];

function scaffold(): string {
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

    <div class="card" style="margin-bottom:16px">
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

    <div class="card">
      <header><h2>Trend</h2><span class="sub"><label style="font-weight:400">characteristic
        <select id="fChar" style="margin-left:6px;min-width:160px"></select></label></span></header>
      <div class="body"><div id="chart"><div class="muted">Look up a job or coil, then pick a characteristic to chart it against its spec limits.</div></div></div>
    </div>
  </div>`;
}

async function loadStatus(): Promise<void> {
  try {
    const r = await authFetch('/api/winspc/health');
    if (!r.ok) { $('#status').textContent = 'status unknown'; return; }
    const h = await r.json();
    if (!h.enabled) {
      $('#status').textContent = 'not configured';
      $('#banner').innerHTML = `<div class="card" style="margin-bottom:16px;border-color:var(--warn,#b8860b)"><div class="body">
        The WinSPC connector is <b>not enabled yet</b>. Once a read-only SQL login to the WinSPC database is wired up
        (<code>WinSpc:Enabled=true</code> + connection string), dimensional QC will appear here.</div></div>`;
    } else if (!h.reachable) {
      $('#status').textContent = 'unreachable';
      $('#banner').innerHTML = `<div class="card" style="margin-bottom:16px;border-color:var(--crit,#b00)"><div class="body">
        WinSPC is enabled but not reachable: <code>${esc(h.error)}</code></div></div>`;
    } else {
      $('#status').textContent = 'WinSPC connected';
      $('#banner').innerHTML = '';
    }
  } catch { /* status is best-effort */ }
}

function specChip(inSpec?: boolean | null): string {
  if (inSpec === true) return '<span class="chip ok">In spec</span>';
  if (inSpec === false) return '<span class="chip crit">Out of spec</span>';
  return '<span class="chip mut">—</span>';
}

// SVG run chart: the selected characteristic's readings over time against its LSL/target/USL band.
function renderChart(charName: string): void {
  const host = $('#chart');
  const pts = lastReadings
    .filter((r) => r.characteristic === charName && typeof r.reading === 'number')
    .slice()
    .reverse(); // the API returns newest-first; chart oldest→newest, left→right
  if (!pts.length) { host.innerHTML = '<div class="muted">No numeric readings for this characteristic.</div>'; return; }

  const lsl = firstNum(pts.map((p) => p.lsl));
  const usl = firstNum(pts.map((p) => p.usl));
  const tgt = firstNum(pts.map((p) => p.target));
  const vals = pts.map((p) => p.reading as number);
  const extras = [lsl, usl, tgt].filter((v): v is number => v != null);
  let lo = Math.min(...vals, ...extras);
  let hi = Math.max(...vals, ...extras);
  if (hi === lo) { hi += 1; lo -= 1; }
  const pad = (hi - lo) * 0.12; lo -= pad; hi += pad;

  const W = 900, H = 320, L = 60, R = 96, T = 16, B = 30;
  const pw = W - L - R, ph = H - T - B, n = pts.length;
  const x = (i: number) => (n === 1 ? L + pw / 2 : L + (pw * i) / (n - 1));
  const y = (v: number) => T + ph * (1 - (v - lo) / (hi - lo));

  // Colors go in `style` (CSS) not presentation attributes — var() only resolves reliably in CSS.
  const specLine = (v: number | null, label: string, color: string, dash = '') =>
    v == null ? '' :
    `<line x1="${L}" y1="${y(v).toFixed(1)}" x2="${L + pw}" y2="${y(v).toFixed(1)}" style="stroke:${color}${dash ? `;stroke-dasharray:${dash}` : ''}" stroke-width="1.5" />
     <text x="${L + pw + 6}" y="${(y(v) + 4).toFixed(1)}" style="fill:${color}" font-size="12">${label} ${trimNum(v)}</text>`;

  const poly = pts.map((p, i) => `${x(i).toFixed(1)},${y(p.reading as number).toFixed(1)}`).join(' ');
  const dots = pts.map((p, i) => {
    const oos = p.inSpec === false;
    return `<circle cx="${x(i).toFixed(1)}" cy="${y(p.reading as number).toFixed(1)}" r="${oos ? 4.5 : 3}" ` +
      `style="fill:${oos ? 'var(--crit,#d33)' : 'var(--accent,#4a90d9)'}"><title>${esc(shortDt(p.readingAt))}: ${p.reading}</title></circle>`;
  }).join('');

  const axis = 'font-size="11" style="fill:var(--ink,#889);opacity:.65"';
  host.innerHTML = `<div style="overflow-x:auto"><svg viewBox="0 0 ${W} ${H}" style="width:100%;min-width:640px;height:auto;font-family:inherit">
    <rect x="${L}" y="${T}" width="${pw}" height="${ph}" fill="none" style="stroke:var(--line,#8884)" />
    ${specLine(usl, 'USL', 'var(--crit,#d33)')}
    ${specLine(lsl, 'LSL', 'var(--crit,#d33)')}
    ${specLine(tgt, 'Target', 'var(--ok,#3a3)', '4 3')}
    <polyline points="${poly}" fill="none" stroke-width="1.5" style="stroke:var(--accent,#4a90d9)" />
    ${dots}
    <text x="${L}" y="${T + ph + 20}" ${axis}>${esc(shortDt(pts[0].readingAt))}</text>
    <text x="${L + pw}" y="${T + ph + 20}" text-anchor="end" ${axis}>${esc(shortDt(pts[n - 1].readingAt))}</text>
    <text x="${L - 6}" y="${(y(hi) + 9).toFixed(1)}" text-anchor="end" ${axis}>${trimNum(hi)}</text>
    <text x="${L - 6}" y="${(y(lo) + 4).toFixed(1)}" text-anchor="end" ${axis}>${trimNum(lo)}</text>
  </svg></div>`;
}

function populateChart(): void {
  const chars = Array.from(new Set(lastReadings.map((r) => r.characteristic).filter(Boolean))) as string[];
  const sel = $<HTMLSelectElement>('#fChar');
  sel.innerHTML = chars.map((c) => `<option>${esc(c)}</option>`).join('');
  if (chars.length) renderChart(chars[0]);
  else $('#chart').innerHTML = '<div class="muted">No characteristics to chart.</div>';
}

async function lookup(): Promise<void> {
  const kind = $<HTMLSelectElement>('#fKind').value;
  const key = val('#fKey');
  if (!key) { setErr('Enter a job or coil number.'); return; }
  setErr('');
  $('#rows').innerHTML = '<tr><td colspan="9" class="muted">Loading…</td></tr>';
  try {
    const r = await authFetch(`/api/winspc/${kind}/${encodeURIComponent(key)}/qc`);
    if (r.status === 503) { setErr('WinSPC is not configured yet.'); $('#rows').innerHTML = ''; return; }
    if (!r.ok) { setErr(`Lookup failed (${r.status}).`); $('#rows').innerHTML = ''; return; }
    const qc: Qc = await r.json();
    const rows = qc.readings ?? [];
    lastReadings = rows;
    $('#rollup').textContent = rows.length
      ? `${qc.totalReadings} readings · ${qc.inSpecReadings} in spec · ${qc.outOfSpecReadings} out of spec`
      : '';
    $('#rows').innerHTML = rows.length ? rows.map((t) => `
      <tr${t.inSpec === false ? ' style="background:var(--crit-soft,rgba(200,0,0,.08))"' : ''}>
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
    populateChart();
  } catch (e) { setErr(`Lookup failed: ${(e as Error).message}`); $('#rows').innerHTML = ''; }
}

(async () => {
  const main = await initShell({ active: 'winspc-qc' });
  main.innerHTML = scaffold();
  $('#btnGo').addEventListener('click', () => void lookup());
  $<HTMLInputElement>('#fKey').addEventListener('keydown', (e) => { if ((e as KeyboardEvent).key === 'Enter') void lookup(); });
  $<HTMLSelectElement>('#fChar').addEventListener('change', () => renderChart($<HTMLSelectElement>('#fChar').value));
  await loadStatus();
})();
