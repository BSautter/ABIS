// ABIS Coil Scanner — the Zebra handheld surface (#4c). A standalone mobile-web page (NOT the
// desktop shell): a tall, single-column, scan-first flow sized for a ~5" gloved touchscreen, the
// modern face of the legacy coil-receiving scanner CGI.
//
// Flow: STEP 1 scan/enter a coil barcode → verify it against ABIS (GET /coils/{abc}) → show the
// "Verified from ERP" card (org/customer/status/weight) with a Ready pill → print the coil label.
//
// Scan input: on a Zebra, DataWedge is configured as a keyboard wedge — it types the barcode into
// the focused field and appends Enter, which this page treats as "look up". The reticle viewport is
// a visual affordance / manual-entry fallback (no live camera decode). We keep the input focused so
// wedged keystrokes always land. Label printing currently opens the browser/HTML coil label
// (GET /api/documents/coil-label/{abc}); native thermal ZPL/CPCL to the Zebra printer is a known
// gap (see PARITY_AUDIT "coil ZPL/CPCL labels").
//
// Compiled by tsc to wwwroot/ui/app/coil-scanner.js; served at /ui/coil-scanner.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { statusChip } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setBusy = (b) => document.body.classList.toggle('scan-busy', b);
const setMsg = (m, kind = '') => { const el = $('#msg'); el.textContent = m; el.className = `scan-msg ${kind}`; };
const num = (n) => (n == null ? '—' : n.toLocaleString());
const dt = (d) => (d == null ? '—' : d.toLocaleDateString());
let current = null;
function scaffold() {
    return `
  <div class="scan-app">
    <div class="scan-top">
      <div class="logo">AB</div>
      <div class="ttl">Coil Scanner<small>ABIS · Receiving</small></div>
      <div class="sp"></div>
      <button class="ico" id="btnPrintTop" type="button" title="Print label" disabled aria-label="Print label">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 9V2h12v7"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/></svg>
      </button>
    </div>
    <div class="scan-body">
      <div class="scan-steph">Step 1 · Scan coil barcode</div>
      <div class="scan-card">
        <div class="scan-reticle"><div class="frame"></div><div class="laser"></div><span class="hint">Align barcode in frame</span></div>
        <div class="scan-label">Scanned coil ID</div>
        <input class="scan-input" id="scan" inputmode="numeric" autocomplete="off" placeholder="waiting for scan…" />
        <button class="scan-btn blue" id="btnLookup" type="button">Look up coil</button>
        <div class="scan-msg" id="msg"></div>
      </div>

      <div id="verifyWrap" hidden>
        <div class="scan-steph">Step 2 · Verified from ERP</div>
        <div class="scan-card scan-verify">
          <div class="vhead"><span class="scan-pill" id="pill">✓ Verified</span><span id="vstatus"></span></div>
          <div id="verify"></div>
        </div>
        <button class="scan-btn green" id="btnPrint" type="button">Print inventory label</button>
        <button class="scan-btn ghost" id="btnReset" type="button">New scan</button>
        <div class="scan-note">Native thermal (ZPL/CPCL) printing to the Zebra and receiving-BOL import are upcoming; this opens the browser coil label.</div>
      </div>
    </div>
  </div>`;
}
function keepFocus() { const s = $('#scan'); s.focus(); s.select(); }
async function lookup() {
    const raw = $('#scan').value.trim().replace(/[^0-9]/g, '');
    if (!raw) {
        setMsg('Scan or type a coil ID.', 'err');
        return;
    }
    setBusy(true);
    setMsg('Looking up…');
    try {
        const c = await client().getCoil(Number(raw));
        current = c;
        renderVerify(c);
        $('#verifyWrap').hidden = false;
        $('#btnPrintTop').disabled = false;
        setMsg('');
        $('#verifyWrap').scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
    catch (e) {
        const msg = e.status === 404 ? `Coil ${raw} not found in ABIS.` : `Lookup failed: ${e.message}`;
        setMsg(msg, 'err');
        current = null;
        $('#verifyWrap').hidden = true;
        $('#btnPrintTop').disabled = true;
    }
    finally {
        setBusy(false);
    }
}
function renderVerify(c) {
    $('#pill').innerHTML = `✓ Coil #${esc(c.coilAbcNum)} ready`;
    $('#vstatus').innerHTML = statusChip('coilStatus', c.coilStatus);
    const rows = [
        ['Coil (ABC)', esc(c.coilAbcNum)],
        ['Mill / org #', esc(c.coilOrgNum ?? '—')],
        ['Alloy · temper', `${esc(c.coilAlloy2 ?? '—')} ${esc(c.coilTemper ?? '')}`.trim()],
        ['Gauge × width', `${num(c.coilGauge)} × ${num(c.coilWidth)}`],
        ['Net wt · balance', `${num(c.netWt)} / ${num(c.netWtBalance)} lb`],
        ['Customer', esc(c.customerId ?? '—')],
        ['Location', esc(c.coilLocation ?? '—')],
        ['Lot · MID', `${esc(c.lotNum ?? '—')} · ${esc(c.coilMidNum ?? '—')}`],
        ['Received', dt(c.dateReceived)],
    ];
    $('#verify').innerHTML = rows.map(([k, v]) => `<div class="vrow"><span class="k">${k}</span><span class="val">${v}</span></div>`).join('');
}
// The label doc is auth-gated, so fetch with the auth header and open the returned HTML via a blob
// URL (a plain window.open would not carry the API key / bearer token).
async function printLabel() {
    if (!current?.coilAbcNum)
        return;
    setBusy(true);
    setMsg('Fetching label…');
    try {
        const r = await authFetch(`/api/documents/coil-label/${current.coilAbcNum}`);
        if (!r.ok) {
            setMsg(`Label failed: ${r.status}`, 'err');
            return;
        }
        const blob = await r.blob();
        window.open(URL.createObjectURL(blob), '_blank');
        setMsg('✓ Label opened.', 'ok');
    }
    catch (e) {
        setMsg(`Label failed: ${e.message}`, 'err');
    }
    finally {
        setBusy(false);
    }
}
function reset() {
    current = null;
    $('#verifyWrap').hidden = true;
    $('#btnPrintTop').disabled = true;
    $('#scan').value = '';
    setMsg('');
    keepFocus();
}
(async () => {
    document.body.innerHTML = scaffold();
    const scan = $('#scan');
    // DataWedge wedges the barcode + Enter into the focused field → treat Enter as "look up".
    scan.addEventListener('keydown', (e) => { if (e.key === 'Enter') {
        e.preventDefault();
        void lookup();
    } });
    $('#btnLookup').addEventListener('click', () => void lookup());
    $('#btnPrint').addEventListener('click', () => void printLabel());
    $('#btnPrintTop').addEventListener('click', () => void printLabel());
    $('#btnReset').addEventListener('click', reset);
    // Keep the scan field focused so wedged keystrokes always land (tapping a button steals focus).
    document.addEventListener('click', (e) => { if (!e.target.closest('button, a, input'))
        keepFocus(); });
    keepFocus();
})();
