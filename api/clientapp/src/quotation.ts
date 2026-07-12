// ABIS Quotation — the CirclePro yield calculator, ported faithfully from the legacy w_circlepro
// window (wf_line_30010, "Aluminum plate program") and restyled to the design system in the shared
// shell (#4 polish). It computes the material yield of cutting circular blanks from a plate,
// hex-nested (0.8660254 = √3/2) in 1/2/3/4-wide patterns. Pure client-side math — no API call —
// so the quote is reproducible exactly as the legacy formulas.
//
// Legacy variable map: cd=circle diameter, a=thickness/gauge, ct=circle-to-circle spacing
// (defaults to a), cw=edge margin, d=coil density (lb/in³), pz=plate length.
//
// Compiled by tsc to wwwroot/ui/app/quotation.js; served at /ui/quotation.html.
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const setErr = (m: string) => { $('#err').textContent = m; };
const numOf = (id: string) => Number($<HTMLInputElement>(id).value.trim());
const fmt = (n: number, dp = 2) => Number.isFinite(n) ? n.toLocaleString(undefined, { maximumFractionDigits: dp }) : '—';

const HEX = 0.8660254; // √3/2 — hex nesting factor
const PI = 3.141592;   // legacy used this literal (kept for bit-faithfulness)

interface Pattern { label: string; wide: number; width: number; circles: number; yieldPct: number; weight: number; }

function scaffold(): string {
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Commercial · Estimating</div><h1>Quotation — CirclePro</h1></div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Circle blank from plate</h2></header>
        <div class="body">
          <form id="calcForm">
            <div class="frow">
              <div class="fld"><label>Circle diameter (in)</label><input id="cd" type="number" step="0.001" placeholder="e.g. 12" style="width:120px" /></div>
              <div class="fld"><label>Thickness / gauge (in)</label><input id="a" type="number" step="0.001" placeholder="e.g. 0.125" style="width:120px" /></div>
              <div class="fld"><label>Circle spacing (in)</label><input id="ct" type="number" step="0.001" placeholder="= thickness" style="width:120px" /></div>
            </div>
            <div class="frow" style="margin-top:8px">
              <div class="fld"><label>Edge margin (in)</label><input id="cw" type="number" step="0.001" value="0.5" style="width:120px" /></div>
              <div class="fld"><label>Density (lb/in³)</label><input id="d" type="number" step="0.001" placeholder="0.098 (Al)" style="width:120px" /></div>
              <div class="fld"><label>Plate length (in)</label><input id="pz" type="number" step="0.1" placeholder="120" style="width:120px" /></div>
            </div>
            <div class="frow" style="margin-top:10px"><button class="btn sm" type="submit">Calculate</button></div>
          </form>
          <div id="err" class="err" style="margin-top:8px"></div>
          <p class="muted" style="margin-bottom:0">Ported from the legacy <span class="mono">w_circlepro</span> formulas — hex-nested (√3/2) blanks, 1–4 wide. Pure client-side, no server call.</p>
        </div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2>Yield by nesting pattern</h2></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:460px">
          <thead><tr><th>Pattern</th><th class="num">Strip width (in)</th><th class="num">Circles</th><th class="num">Yield</th><th class="num">Plate wt (lb)</th></tr></thead>
          <tbody id="out"><tr><td colspan="5" class="muted">Enter a diameter &amp; thickness.</td></tr></tbody>
        </table></div>
        <div class="body"><div id="best" class="ok-note" style="margin-bottom:0"></div></div>
      </div></div>
    </div>
  </div>`;
}

function compute(): Pattern[] | null {
  const cd = numOf('#cd');                 // circle diameter
  const a = numOf('#a');                   // thickness / gauge
  const ct = $<HTMLInputElement>('#ct').value.trim() ? numOf('#ct') : a; // spacing defaults to thickness
  const cw = numOf('#cw');                 // edge margin
  const d = $<HTMLInputElement>('#d').value.trim() ? numOf('#d') : 0.098; // density (Al default)
  const pzIn = $<HTMLInputElement>('#pz').value.trim() ? numOf('#pz') : 120; // plate length (assumed 120")

  if (!(cd > 0) || !(a > 0)) { setErr('Circle diameter and thickness are required and must be > 0.'); return null; }
  if (ct < 0 || cw < 0) { setErr('Spacing and margin cannot be negative.'); return null; }
  setErr('');

  // nz = circles per length; pz snapped to a whole number of pitches (legacy Int(x+0.5) = round).
  const nz = Math.round(pzIn / (cd + ct));
  const pz = nz * (cd + ct) + ct;

  // Strip widths for 1/2/3/4-wide hex-nested rows.
  const widths = [
    cd + 2 * cw,
    (cd + ct) * HEX + cd + 2 * cw,
    2 * (cd + ct) * HEX + cd + 2 * cw,
    3 * (cd + ct) * HEX + cd + 2 * cw,
  ];
  // Circle counts per pattern (from the legacy yield denominators).
  const counts = [nz, 2 * nz - 1, 3 * nz - 1, 4 * nz - 2];
  const circleArea = (cd / 2) ** 2 * PI;

  return widths.map((width, i): Pattern => ({
    label: `${i + 1} WIDE`,
    wide: i + 1,
    width,
    circles: counts[i],
    yieldPct: 100 * circleArea / ((pz * width) / counts[i]),
    weight: Math.round(d * pz * a * width), // plate weight ≈ density·length·thickness·width (legacy Int(x+0.5) = round)
  }));
}

function calc(): void {
  const rows = compute();
  if (!rows) { $('#out').innerHTML = '<tr><td colspan="5" class="muted">—</td></tr>'; $('#best').textContent = ''; return; }
  $('#out').innerHTML = rows.map((p) => `
    <tr>
      <td class="mono">${p.label}</td>
      <td class="num">${fmt(p.width, 3)}</td>
      <td class="num">${fmt(p.circles, 0)}</td>
      <td class="num"><strong>${fmt(p.yieldPct, 1)}%</strong></td>
      <td class="num">${fmt(p.weight, 0)}</td>
    </tr>`).join('');
  const best = rows.reduce((a, b) => (b.yieldPct > a.yieldPct ? b : a));
  $('#best').textContent = `Best yield: ${best.label} at ${fmt(best.yieldPct, 1)}% (${fmt(best.circles, 0)} circles, strip ${fmt(best.width, 3)}")`;
}

(async () => {
  const main = await initShell({ active: 'quotation' });
  main.innerHTML = scaffold();
  $<HTMLFormElement>('#calcForm').addEventListener('submit', (e) => { e.preventDefault(); calc(); });
  ['#cd', '#a', '#ct', '#cw', '#d', '#pz'].forEach((id) =>
    $<HTMLInputElement>(id).addEventListener('input', () => { if ($<HTMLInputElement>('#cd').value && $<HTMLInputElement>('#a').value) calc(); }));
})();
