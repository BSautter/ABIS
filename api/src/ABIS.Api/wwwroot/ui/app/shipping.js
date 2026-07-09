// ABIS Shipping — the legacy shipping window, restyled to the design system in the shared shell
// (#4 polish). Shipment search, detail, and the dispatch action (status / vehicle / sent + actual
// times / notes) via PATCH. Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/shipping.js; served at /ui/shipping.html.
import { AbisClient, ShipmentStatusPatch } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const dt = (v) => (v == null ? '' : v.toISOString().slice(0, 10));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const val = (id) => $(id).value.trim();
const dateOrUndef = (id) => (val(id) ? new Date(val(id)) : undefined);
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let selected = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Logistics · Shipping</div><h1>Shipping</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow"><div class="fld"><label>Customer id</label><input id="fCustomer" inputmode="numeric" style="width:120px" placeholder="any" /></div><button class="btn sm" type="submit">Search</button></form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Shipments</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:480px">
          <thead><tr><th>Packing list</th><th>BOL</th><th>Customer</th><th>Status</th><th>Vehicle</th></tr></thead>
          <tbody id="shipments"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2>Shipment detail</h2></header>
        <div class="body" id="detail"><p class="muted">Select a shipment to view + dispatch it.</p></div>
      </div></div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    const customerId = val('#fCustomer') ? Number(val('#fCustomer')) : undefined;
    try {
        const page = await client().listShipments(1, 50, customerId, undefined, undefined);
        const items = page.items ?? [];
        $('#shipments').innerHTML = items.length ? items.map((s) => `
      <tr class="click" data-id="${s.packingList}">
        <td class="mono">${esc(s.packingList)}</td><td class="mono">${esc(s.billOfLading)}</td><td class="mono">${esc(s.customerId)}</td>
        <td>${chip(s.shipmentStatus)}</td><td class="mono">${esc(s.vehicleId)}</td>
      </tr>`).join('') : '<tr><td colspan="5" class="muted">No matching shipments.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} shipments`;
        $('#listSub').textContent = `${items.length} shown`;
        document.querySelectorAll('#shipments tr.click').forEach((tr) => tr.addEventListener('click', () => void loadShipment(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadShipment(id) {
    setErr('');
    setBusy(true);
    selected = id;
    try {
        const s = await client().getShipment(id);
        $('#detail').innerHTML = `
      <div class="kv">
        <span><b>Packing list</b>${esc(s.packingList)}</span><span><b>BOL</b>${esc(s.billOfLading)}</span>
        <span><b>Customer</b>${esc(s.customerId)}</span><span><b>Carrier</b>${esc(s.carrierId)}</span><span><b>Sent</b>${esc(dt(s.dateSent))}</span>
      </div>
      <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:16px 0 8px">Dispatch</h3>
      <div class="frow">
        <div class="fld"><label>Status</label><input id="dStatus" value="${esc(s.shipmentStatus)}" style="width:80px" /></div>
        <div class="fld"><label>Vehicle status</label><input id="dVehicle" value="${esc(s.vehicleStatus)}" style="width:100px" /></div>
        <div class="fld"><label>Date sent</label><input id="dSent" type="date" value="${esc(dt(s.dateSent))}" /></div>
        <div class="fld"><label>Actual</label><input id="dActual" type="date" value="${esc(dt(s.shipmentActualedDateTime))}" /></div>
        <div class="fld" style="flex:1;min-width:180px"><label>Notes</label><input id="dNotes" value="${esc(s.shipmentNotes)}" /></div>
      </div>
      <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm" id="btnDispatch" type="button">Save dispatch</button><span id="dispOk" class="ok-note"></span></div>`;
        $('#btnDispatch').addEventListener('click', () => void dispatch());
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function dispatch() {
    if (selected == null)
        return;
    setErr('');
    setBusy(true);
    const patch = new ShipmentStatusPatch({
        shipmentStatus: val('#dStatus') ? Number(val('#dStatus')) : undefined,
        vehicleStatus: val('#dVehicle') ? Number(val('#dVehicle')) : undefined,
        dateSent: dateOrUndef('#dSent'),
        shipmentActualedDateTime: dateOrUndef('#dActual'),
        shipmentNotes: val('#dNotes') || undefined,
    });
    try {
        await client().patchShipment(selected, patch);
        $('#dispOk').textContent = '✓ Dispatch saved.';
        await search();
    }
    catch (e) {
        setErr(`Dispatch failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'shipping' });
    main.innerHTML = scaffold();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    await search();
})();
