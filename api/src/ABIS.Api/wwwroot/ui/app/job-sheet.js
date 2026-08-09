// The live job sheet — legacy's PRODUCTION ORDER, rendered for screen and for paper.
//
// Shared by the production folder and the DAS console, because it is the same document in both
// places and the shop floor reads numbers off it. Two things are worth knowing before editing:
//
//  1. EVERY FIGURE HERE IS A NUMBER SOMEONE CUTS METAL TO. A blank is safer than a guess: an absent
//     dimension renders as an em dash, never as 0. A circle genuinely has no length, and printing
//     "0.000" beside a tolerance would read as a dimension to cut to.
//  2. The two warnings are the point of the top of the sheet. EDGE TRIMMING REQUIRED, and — only
//     when the trimmed width was also overridden — CONTACT FOREMAN BEFORE RUNNING. Legacy prints
//     them in red and yellow-on-red respectively; they are not decoration.
//
// Printing goes through a hidden iframe carrying its own stylesheet, the same route the skid tags
// take, so it lands on whatever printer the shop-floor PC has mapped without server-side plumbing.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
/** An em dash for anything absent. Never a zero — see the note at the top of this file. */
const dash = '—';
const txt = (v) => (v == null || v === '' ? dash : esc(v));
/** A number at up to `dp` decimals, with trailing zeros trimmed the way the sheet reads. */
function num(v, dp = 3) {
    if (v == null)
        return dash;
    const s = v.toFixed(dp).replace(/\.?0+$/, '');
    return s === '' || s === '-' ? '0' : s;
}
const dateOnly = (d) => (d == null ? dash : new Date(d).toLocaleDateString());
/**
 * One dimension as `value +tol/-tol`. A missing tolerance prints an explicit `+0` rather than
 * nothing, because legacy does: a blank where a tolerance belongs reads as "not measured", and
 * "no tolerance given" is a different statement.
 */
function dim(d) {
    if (!d)
        return dash;
    return `<span class="js-dim"><b>${num(d.value)}</b>` +
        `<span class="js-tol">+${num(d.plusTol ?? 0)}<br>-${num(d.minusTol ?? 0)}</span></span>`;
}
/** Sentence-case a camelCase dimension name for the caption: longLength -> "Long length". */
const dimLabel = (d, fallback) => !d?.name ? fallback : d.name.replace(/([A-Z])/g, (c) => ' ' + c.toLowerCase()).replace(/^./, (c) => c.toUpperCase());
function warnings(s) {
    const rows = [];
    if (s.trimmingRequired)
        rows.push('<div class="js-warn">EDGE TRIMMING REQUIRED</div>');
    // Both flags, never one. An override on an item nobody is trimming is not a hazard, and a banner
    // that cries wolf is a banner the floor stops reading.
    if (s.trimmedWidthOverridden)
        rows.push('<div class="js-warn js-warn-hard">TRIMMED WIDTH OUTSIDE OF EQUIPMENT TOLERANCE. CONTACT FOREMAN BEFORE RUNNING.</div>');
    return rows.join('');
}
function coilRows(s) {
    const list = s.coils ?? [];
    if (!list.length)
        return `<tr><td colspan="${s.byLot ? 6 : 4}" class="muted">No coils on this job yet.</td></tr>`;
    return list.map((c) => `<tr>
    <td>${txt(c.lotNum)}</td>
    <td class="mono">${txt(c.coilOrgNum)}</td>
    <td class="mono">${txt(c.coilAbcNum)}</td>
    <td class="num">${num(c.processQuantity, 0)}</td>
    ${s.byLot ? `<td class="num">${num(c.skids, 0)}</td><td class="num">${txt(c.piecesPerSkid)}</td>` : ''}
  </tr>`).join('');
}
function partialRows(s) {
    const list = s.partials ?? [];
    if (!list.length)
        return '';
    return `
    <h3>Partial skids carried in from another job</h3>
    <p class="js-note">Run these down before opening a new coil.</p>
    <table class="tbl">
      <thead><tr><th>Skid</th><th>Made on job</th><th>Lot</th><th>Coil</th><th class="num">Net wt</th><th class="num">Pieces</th><th>Location</th></tr></thead>
      <tbody>${list.map((p) => `<tr>
        <td class="mono">${txt(p.sheetSkidNum)}</td>
        <td class="mono">${txt(p.madeOnJob)}</td>
        <td>${txt(p.lotNum)}</td>
        <td class="mono">${txt(p.coilOrgNum)}</td>
        <td class="num">${num(p.netWt, 0)}</td>
        <td class="num">${txt(p.pieces)}</td>
        <td>${txt(p.location)}</td>
      </tr>`).join('')}</tbody>
    </table>`;
}
/** A numbered list that keeps its positions — the lines are referred to by number on the floor. */
function numbered(title, items) {
    const shown = items.map((v, i) => (v ? `<li value="${i + 1}">${esc(v)}</li>` : '')).join('');
    if (!shown)
        return '';
    return `<h3>${esc(title)}</h3><ol class="js-list">${shown}</ol>`;
}
/** The sheet's body markup. Shared verbatim between the screen card and the printed page. */
export function jobSheetHtml(s) {
    const specs = s.packagingSpecs ?? [];
    return `
  ${warnings(s)}
  <div class="js-grid">
    <span><b>Job</b>${txt(s.abJobNum)}</span>
    <span><b>Line</b>${txt(s.lineDesc)}</span>
    <span><b>Customer</b>${txt(s.customer)}</span>
    <span><b>End user</b>${txt(s.endUser)}</span>
    <span><b>ABCO order</b>${txt(s.orderAbcNum)}${s.orderItemNum != null ? ' / ' + esc(s.orderItemNum) : ''}</span>
    <span><b>Part #</b>${txt(s.enduserPartNum)}</span>
    <span><b>Order qty</b>${num(s.orderQty, 0)}</span>
    <span><b>Mat. rec'd</b>${num(s.materialReceived, 0)}</span>
    <span><b>Ship tol.</b>+${txt(s.shipTolerancePlus)} / -${txt(s.shipToleranceMinus)}</span>
    <span><b>Alloy — temper</b>${txt(s.alloy2)} — ${txt(s.temper)}</span>
    <span><b>Type</b>${txt(s.sheetType)}</span>
    <span><b>Scrap</b>${txt(s.scrapHandingType)}</span>
  </div>

  <h3>Piece weight</h3>
  <div class="js-pcwt">
    <span class="js-cell"><small>Gauge</small>${dim({ value: s.gauge, plusTol: s.gaugePlus, minusTol: s.gaugeMinus })}</span>
    <span class="js-x">×</span>
    <span class="js-cell"><small>${esc(dimLabel(s.width, 'Width'))}</small>${dim(s.width)}</span>
    <span class="js-x">×</span>
    <span class="js-cell"><small>${esc(dimLabel(s.length, 'Length'))}</small>${dim(s.length)}</span>
    <span class="js-x">×</span>
    <span class="js-cell"><small>Density</small><b>${num(s.metalDensity)}</b></span>
    <span class="js-x">=</span>
    <span class="js-cell"><small>Piece wt</small><b>${num(s.theoreticalUnitWt)}</b></span>
    <span class="js-cell"><small>Die #</small><b>${txt(s.dieName)}</b></span>
    <span class="js-cell"><small>Pitch</small>${dim({ value: s.pitch, plusTol: s.pitchPlus, minusTol: s.pitchMinus })}</span>
  </div>

  <div class="js-grid">
    <span><b>Pc./skid</b>${s.byLot ? 'See below' : txt(s.piecesPerSkid)}</span>
    <span><b>Max skid wt</b>${num(s.maxSkidWt, 0)}</span>
    <span><b>Est. skid wt</b>${num(s.estSkidWt, 0)}</span>
    <span><b>No. skids</b>${num(s.numSkids, 0)}</span>
    <span><b>Material yield</b>${num(s.materialYieldAfterTrim)}</span>
    <span><b>Max scrap wt</b>${num(s.maxScrapWt, 0)}</span>
    <span><b>Sheet wt</b>${num(s.sheetWt, 0)}</span>
    <span><b>Men used</b>${txt(s.numberOfMenUsed)}</span>
    <span><b>Started</b>${dateOnly(s.timeDateStarted)}</span>
    <span><b>Finished</b>${dateOnly(s.timeDateFinished)}</span>
  </div>

  ${s.trimmingRequired ? `<div class="js-grid">
    <span><b>Trim type</b>${txt(s.trimTypeDesc)}</span>
    <span><b>Incoming coil width</b>${num(s.incomingCoilWidth)}</span>
    <span><b>Trimmed coil width</b>${num(s.trimmedCoilWidth)}</span>
    <span><b>Yield strength</b>${txt(s.yieldStrength)}</span>
  </div>` : ''}

  ${s.sketchJobNote ? `<p class="js-note"><b>Note for this job:</b> ${esc(s.sketchJobNote)}</p>` : ''}
  ${s.jobNotes ? `<p class="js-note"><b>Job notes:</b> ${esc(s.jobNotes)}</p>` : ''}
  ${s.partialSkidNote ? `<p class="js-note js-partial">${esc(s.partialSkidNote)}</p>` : ''}

  <h3>Coils on this job${s.coils?.length ? ` — ${s.coils.length}, total ${num(s.materialReceived, 0)}` : ''}</h3>
  <table class="tbl">
    <thead><tr><th>Lot</th><th>Coil</th><th>ABC #</th><th class="num">Wt</th>
      ${s.byLot ? '<th class="num">Skids</th><th class="num">Pc./skid</th>' : ''}</tr></thead>
    <tbody>${coilRows(s)}</tbody>
  </table>

  ${partialRows(s)}

  ${numbered('Processing', [s.materialEndUse, s.surface, s.flatness,
        s.dimplingCode == null ? undefined : String(s.dimplingCode),
        s.oilStencilInterleave, s.itemAttachments, s.processingOtherSpec])}
  ${numbered('Packaging', [...specs, s.packagingBands, s.packagingOtherSpec])}
  ${s.itemNote ? `<p class="js-note"><b>Item notes:</b> ${esc(s.itemNote)}</p>` : ''}
  ${s.jobReferenceCodes ? `<p class="js-note"><b>Reference codes:</b> ${esc(s.jobReferenceCodes)}</p>` : ''}`;
}
/** Styles the sheet needs, used on screen and inlined into the printed page. */
const SHEET_CSS = `
.js-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:6px 18px;margin:10px 0}
.js-grid span{display:flex;gap:8px;justify-content:space-between;border-bottom:1px dotted var(--rail-line,#ccc);padding:3px 0}
.js-grid b{font-weight:600;opacity:.75}
.js-pcwt{display:flex;flex-wrap:wrap;align-items:flex-end;gap:10px;margin:8px 0 14px}
.js-cell{display:flex;flex-direction:column}
.js-cell small{opacity:.7;font-size:11px}
.js-x{opacity:.5;padding-bottom:4px}
.js-dim{display:inline-flex;align-items:center;gap:4px}
.js-tol{font-size:10px;line-height:1.1;opacity:.75}
.js-note{margin:6px 0}
.js-partial{font-weight:600}
.js-list{margin:4px 0 10px 22px}
.js-warn{background:#fde047;color:#7f1d1d;font-weight:700;text-align:center;padding:6px;border-radius:6px;margin-bottom:6px}
.js-warn-hard{background:#7f1d1d;color:#fde047}
.tbl .num,.js-grid .num{text-align:right}`;
/** Inject the sheet's stylesheet once per document. */
function ensureStyles() {
    if (document.getElementById('job-sheet-css'))
        return;
    const el = document.createElement('style');
    el.id = 'job-sheet-css';
    el.textContent = SHEET_CSS;
    document.head.appendChild(el);
}
/**
 * Fetch and render job `jobNum`'s sheet into `container`.
 *
 * Absence and failure are kept apart: a job with no sheet (404) says so, and a failed request says
 * that instead. A blank panel on a shop-floor console reads as "still loading" forever.
 */
export async function renderJobSheet(container, jobNum) {
    ensureStyles();
    if (jobNum == null) {
        container.innerHTML = '<p class="muted">No job selected.</p>';
        return null;
    }
    container.innerHTML = '<p class="muted">Loading job sheet…</p>';
    try {
        const sheet = await new AbisClient('', { fetch: authFetch }).getJobSheet(jobNum);
        container.innerHTML = jobSheetHtml(sheet);
        return sheet;
    }
    catch (e) {
        const msg = e.status === 404
            ? `No job sheet for job ${jobNum}.`
            : `Job sheet unavailable: ${e.message}`;
        container.innerHTML = `<p class="muted">${esc(msg)}</p>`;
        return null;
    }
}
/**
 * Print the sheet through a hidden iframe — the same route the skid tags take, so it lands on
 * whatever printer the shop-floor PC has mapped with no server-side printer plumbing. The frame
 * carries its own stylesheet because it does not inherit the app's.
 */
export function printJobSheet(sheet) {
    const frame = document.createElement('iframe');
    frame.setAttribute('aria-hidden', 'true');
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
    frame.srcdoc = `<!doctype html><html><head><meta charset="utf-8">
    <title>Production order ${esc(sheet.abJobNum)}</title>
    <style>
      body{font:12px/1.4 Arial,Helvetica,sans-serif;margin:14px;color:#000}
      h1{font-size:16px;margin:0 0 2px} h3{font-size:12px;margin:12px 0 4px;text-transform:uppercase;letter-spacing:.04em}
      table{border-collapse:collapse;width:100%;font-size:11px} th,td{border:1px solid #999;padding:3px 5px;text-align:left}
      .muted{color:#666}
      ${SHEET_CSS.replace(/var\(--rail-line,#ccc\)/g, '#999')}
    </style></head>
    <body><h1>PRODUCTION ORDER — job ${esc(sheet.abJobNum)}</h1>${jobSheetHtml(sheet)}</body></html>`;
    frame.onload = () => {
        const w = frame.contentWindow;
        if (w) {
            w.focus();
            w.print();
        }
        window.setTimeout(() => frame.remove(), 15000);
    };
    document.body.appendChild(frame);
}
