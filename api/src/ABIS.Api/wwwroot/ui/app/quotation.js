// ABIS Quotation — greenfield (Path C) CirclePro yield calculator, ported faithfully
// from the legacy w_circlepro window (wf_line_30010, "Aluminum plate program"). It
// computes the material yield of cutting circular blanks from a plate, hex-nested
// (0.8660254 = √3/2) in 1/2/3/4-wide patterns. Pure client-side math — no API call —
// so the quote is reproducible exactly as the legacy formulas.
//
// Legacy variable map: cd=circle diameter, a=thickness/gauge, ct=circle-to-circle
// spacing (defaults to a), cw=edge margin, d=coil density (lb/in³), pz=plate length.
//
// Compiled by `tsc` to wwwroot/ui/app/quotation.js; served at /ui/quotation.html.
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const setErr = (m) => { $('#err').textContent = m; };
const numOf = (id) => Number($(id).value.trim());
const fmt = (n, dp = 2) => Number.isFinite(n) ? n.toLocaleString(undefined, { maximumFractionDigits: dp }) : '—';
const HEX = 0.8660254; // √3/2 — hex nesting factor
const PI = 3.141592; // legacy used this literal (kept for bit-faithfulness)
function compute() {
    const cd = numOf('#cd'); // circle diameter
    const a = numOf('#a'); // thickness / gauge
    let ct = $('#ct').value.trim() ? numOf('#ct') : a; // spacing defaults to thickness
    const cw = numOf('#cw'); // edge margin
    const d = $('#d').value.trim() ? numOf('#d') : 0.098; // density (Al default)
    const pzIn = $('#pz').value.trim() ? numOf('#pz') : 120; // plate length (assumed 120")
    if (!(cd > 0) || !(a > 0)) {
        setErr('Circle diameter and thickness are required and must be > 0.');
        return null;
    }
    if (ct < 0 || cw < 0) {
        setErr('Spacing and margin cannot be negative.');
        return null;
    }
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
    return widths.map((width, i) => ({
        label: `${i + 1} WIDE`,
        wide: i + 1,
        width,
        circles: counts[i],
        yieldPct: 100 * circleArea / ((pz * width) / counts[i]),
        weight: Math.round(d * pz * a * width + 0.5), // plate weight ≈ density·length·thickness·width
    }));
}
function calc() {
    const rows = compute();
    if (!rows) {
        $('#out').innerHTML = '';
        return;
    }
    $('#out').innerHTML = rows.map((p) => `
    <tr>
      <td>${p.label}</td>
      <td>${fmt(p.width, 3)}</td>
      <td>${fmt(p.circles, 0)}</td>
      <td><strong>${fmt(p.yieldPct, 1)}%</strong></td>
      <td>${fmt(p.weight, 0)}</td>
    </tr>`).join('');
    const best = rows.reduce((a, b) => (b.yieldPct > a.yieldPct ? b : a));
    $('#best').textContent = `Best yield: ${best.label} at ${fmt(best.yieldPct, 1)}% (${fmt(best.circles, 0)} circles, strip ${fmt(best.width, 3)}")`;
}
async function init() {
    $('#calcForm').addEventListener('submit', (e) => { e.preventDefault(); calc(); });
    ['#cd', '#a', '#ct', '#cw', '#d', '#pz'].forEach((id) => $(id).addEventListener('input', () => { if ($('#cd').value && $('#a').value)
        calc(); }));
    await initShell({ active: 'quotation', adopt: true }); // keeps the auth bar / key field consistent with the other screens
}
void init();
