// ABIS Warehouse — the warehouse view of finished sheet skids (legacy w_wh_business /
// w_wh_detail), restyled to the design system in the shared shell (#4 polish). It's the
// warehouse side of sheet_skid: where each skid is stored, its warehouse ticket, and status.
// List skids + a warehouse update (location / ticket / status) via PATCH.
//
// Compiled by tsc to wwwroot/ui/app/warehouse.js; served at /ui/warehouse.html.
import { AbisClient, SheetSkidWarehousePatch } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const num = (n) => (n == null ? '' : n.toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleString());
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let editingNum = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Production · Warehouse</div><h1>Warehouse</h1></div>
      <div class="frow" style="align-items:center"><button class="btn sm ghost" id="btnRefresh" type="button">Refresh</button><span class="shift-tag" id="count">—</span></div>
    </div>
    <div id="err" class="err" style="margin-bottom:8px"></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Finished sheet skids</h2><span class="sub">in the warehouse</span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:620px">
          <thead><tr><th>Skid</th><th>Job</th><th>Display</th><th>Location</th><th>Ticket</th><th>Status</th><th class="num">Net wt</th><th class="num">Pcs</th><th>Date</th></tr></thead>
          <tbody id="skids"><tr><td colspan="9" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card" id="detail">
        <header><h2 id="formTitle">Warehouse update</h2></header>
        <div class="body">
          <p class="muted" id="summary">Select a skid to update its location, ticket &amp; status.</p>
          <div class="frow">
            <div class="fld" style="flex:1;min-width:160px"><label>Location</label><input id="wLocation" maxlength="18" placeholder="e.g. WH-A-01" /></div>
            <div class="fld"><label>Warehouse ticket</label><input id="wTicket" maxlength="32" style="width:140px" /></div>
            <div class="fld"><label>Status</label><input id="wStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnSave" type="button">Save warehouse update</button><span id="ok" class="ok-note"></span></div>
        </div>
      </div></div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    try {
        const page = await client().listSheetSkids(1, 100, 'sheetSkidNum', 'desc');
        const items = page.items ?? [];
        $('#skids').innerHTML = items.length ? items.map((s) => `
      <tr class="click" data-id="${s.sheetSkidNum}">
        <td class="mono">${esc(s.sheetSkidNum)}</td><td class="mono">${esc(s.abJobNum)}</td><td>${esc(s.sheetSkidDisplayNum)}</td>
        <td>${esc(s.skidLocation)}</td><td class="mono">${esc(s.skidTicketIfWhed)}</td><td>${statusChip('skidSheetStatus', s.skidSheetStatus)}</td>
        <td class="num">${esc(num(s.sheetNetWt))}</td><td class="num">${esc(s.skidPieces)}</td><td class="mono">${esc(dShow(s.skidDate))}</td></tr>`).join('')
            : '<tr><td colspan="9" class="muted">No sheet skids.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#skids tr.click').forEach((tr) => tr.addEventListener('click', () => void loadSkid(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadSkid(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const s = await client().getSheetSkid(id);
        editingNum = id;
        $('#formTitle').textContent = `Skid #${id} — ${s.sheetSkidDisplayNum ?? ''}`;
        $('#summary').textContent = `Job ${s.abJobNum ?? ''} · ${num(s.sheetNetWt)} net · ${s.skidPieces ?? ''} pcs`;
        setV('#wLocation', s.skidLocation);
        setV('#wTicket', s.skidTicketIfWhed);
        setV('#wStatus', s.skidSheetStatus);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function save() {
    if (editingNum == null) {
        setErr('Select a skid first.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    const body = new SheetSkidWarehousePatch({
        skidLocation: v('#wLocation') || undefined,
        skidTicketIfWhed: v('#wTicket') || undefined,
        skidSheetStatus: v('#wStatus') ? Number(v('#wStatus')) : undefined,
    });
    try {
        await client().updateSheetSkidWarehouse(editingNum, body);
        setOk(`✓ Warehouse-updated skid #${editingNum}.`);
        await search();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'warehouse' });
    main.innerHTML = scaffold();
    $('#btnSave').addEventListener('click', () => void save());
    $('#btnRefresh').addEventListener('click', () => void search());
    await search();
})();
