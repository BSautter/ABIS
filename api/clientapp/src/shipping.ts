// ABIS Shipping — the legacy shipping window, restyled to the design system in the shared shell
// (#4 polish). Shipment search, detail, the dispatch action (status / vehicle / sent + actual
// times / notes) via PATCH, and a guided "Close BOL / Mark shipped" close-out. Typed calls via the
// NSwag client; the close-out POSTs /shipments/{pl}/close (newer than the committed client).
//
// Compiled by tsc to wwwroot/ui/app/shipping.js; served at /ui/shipping.html.
import { AbisClient, ShipmentStatusPatch } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
import { statusChip } from './status-labels.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const dt = (v: Date | undefined): string => (v == null ? '' : v.toISOString().slice(0, 10));
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const dateOrUndef = (id: string): Date | undefined => (val(id) ? new Date(val(id)) : undefined);
const chip = (s: unknown): string => `<span class="chip mut">${esc(s ?? '—')}</span>`;

let selected: number | null = null;

function scaffold(): string {
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

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const customerId = val('#fCustomer') ? Number(val('#fCustomer')) : undefined;
  try {
    const page = await client().listShipments(1, 50, customerId, undefined, undefined);
    const items = page.items ?? [];
    $('#shipments').innerHTML = items.length ? items.map((s) => `
      <tr class="click" data-id="${s.packingList}">
        <td class="mono">${esc(s.packingList)}</td><td class="mono">${esc(s.billOfLading)}</td><td class="mono">${esc(s.customerId)}</td>
        <td>${statusChip('shipmentStatus', s.shipmentStatus)}</td><td class="mono">${esc(s.vehicleId)}</td>
      </tr>`).join('') : '<tr><td colspan="5" class="muted">No matching shipments.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} shipments`;
    $('#listSub').textContent = `${items.length} shown`;
    document.querySelectorAll<HTMLTableRowElement>('#shipments tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadShipment(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadShipment(id: number): Promise<void> {
  setErr(''); setBusy(true); selected = id;
  try {
    const s = await client().getShipment(id);
    $('#detail').innerHTML = `
      <div class="kv">
        <span><b>Packing list</b>${esc(s.packingList)}</span><span><b>BOL</b>${esc(s.billOfLading)}</span>
        <span><b>Customer</b>${esc(s.customerId)}</span><span><b>Carrier</b>${esc(s.carrierId)}</span>
        <span><b>Status</b>${statusChip('shipmentStatus', s.shipmentStatus)}</span><span><b>Sent</b>${esc(dt(s.dateSent))}</span>
      </div>
      <div class="frow" style="margin:14px 0 4px;align-items:center">
        <button class="btn sm" id="btnClose" type="button">Mark shipped / Close BOL</button>
        <span id="closeOk" class="ok-note"></span>
      </div>
      <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:16px 0 8px">Packing list <span style="text-transform:none;letter-spacing:0;color:var(--ink-3);font-weight:400">— sheet + scrap skids (sheet feeds the 856)</span></h3>
      <div class="frow" style="align-items:flex-end;margin-bottom:8px">
        <div class="fld"><label>Type</label><select id="addType" style="width:140px"><option value="SHEET">Sheet skid</option><option value="SCRAP">Scrap skid</option><option value="REJECT_COIL">Reject coil</option></select></div>
        <div class="fld"><label>Skid / coil #</label><input id="addSkid" inputmode="numeric" style="width:130px" placeholder="skid or coil #" /></div>
        <button class="btn sm ghost" id="btnAddItem" type="button">Add</button>
        <span id="itemOk" class="ok-note"></span>
        <button class="btn sm ghost" id="btnPrintPacking" type="button" style="margin-left:auto">Print packing list</button>
        <button class="btn sm ghost" id="btnPrintBol" type="button">Print BOL</button>
      </div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
        <thead><tr><th>Type</th><th>Skid</th><th>Part</th><th>PO</th><th>Coil</th><th style="text-align:right">Net</th><th style="text-align:right">Gross</th><th style="text-align:right">Pcs</th><th></th></tr></thead>
        <tbody id="packItems"><tr><td colspan="9" class="muted">Loading…</td></tr></tbody>
      </table></div>
      <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:16px 0 8px">Dispatch <span style="text-transform:none;letter-spacing:0;color:var(--ink-3);font-weight:400">— manual edit</span></h3>
      <div class="frow">
        <div class="fld"><label>Status</label><input id="dStatus" value="${esc(s.shipmentStatus)}" style="width:80px" /></div>
        <div class="fld"><label>Vehicle status</label><input id="dVehicle" value="${esc(s.vehicleStatus)}" style="width:100px" /></div>
        <div class="fld"><label>Date sent</label><input id="dSent" type="date" value="${esc(dt(s.dateSent))}" /></div>
        <div class="fld"><label>Actual</label><input id="dActual" type="date" value="${esc(dt(s.shipmentActualedDateTime))}" /></div>
        <div class="fld" style="flex:1;min-width:180px"><label>Notes</label><input id="dNotes" value="${esc(s.shipmentNotes)}" /></div>
      </div>
      <div class="frow" style="margin-top:10px;align-items:center"><button class="btn sm ghost" id="btnDispatch" type="button">Save dispatch</button><span id="dispOk" class="ok-note"></span></div>`;
    $('#btnClose').addEventListener('click', () => void closeBol());
    $('#btnDispatch').addEventListener('click', () => void dispatch());
    $('#btnAddItem').addEventListener('click', () => void addPackItem());
    $('#btnPrintPacking').addEventListener('click', () => void printDoc(`/api/documents/packing-list/${id}`, 'Packing list'));
    $('#btnPrintBol').addEventListener('click', () => void printDoc(`/api/documents/bol/${id}`, 'BOL'));
    await loadPackingItems(id);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// ---- Packing-list line items (the skids a shipment carries) — endpoints newer than the NSwag client. ----
interface PackItem {
  packingItemId: number; itemType: string; refNum: number; skidDisplayNum?: string; netWeight: number;
  grossWeight: number; pieces: number; enduserPartNum?: string; origCustomerPo?: string; coilOrgNum?: string;
}

async function loadPackingItems(id: number): Promise<void> {
  try {
    const r = await authFetch(`/api/shipments/${id}/items`);
    if (!r.ok) { $('#packItems').innerHTML = `<tr><td colspan="9" class="muted">Failed to load items (${r.status}).</td></tr>`; return; }
    const items = (await r.json()) as PackItem[];
    const num = (n: unknown) => `<td class="mono" style="text-align:right">${esc(n)}</td>`;
    $('#packItems').innerHTML = items.length ? items.map((it) => `
      <tr>
        <td>${esc(it.itemType)}</td>
        <td class="mono">${esc(it.skidDisplayNum ?? it.refNum)}</td>
        <td class="mono">${esc(it.enduserPartNum)}</td><td class="mono">${esc(it.origCustomerPo)}</td><td class="mono">${esc(it.coilOrgNum)}</td>
        ${num(Math.round(it.netWeight))}${num(Math.round(it.grossWeight))}${num(it.pieces || '')}
        <td><button class="btn sm ghost" data-type="${esc(it.itemType)}" data-id="${it.packingItemId}" type="button">Remove</button></td>
      </tr>`).join('') : '<tr><td colspan="9" class="muted">No skids on this packing list yet.</td></tr>';
    document.querySelectorAll<HTMLButtonElement>('#packItems button[data-id]').forEach((b) =>
      b.addEventListener('click', () => void removePackItem(String(b.dataset.type), Number(b.dataset.id))));
  } catch (e) { setErr(`Items failed: ${(e as Error).message}`); }
}

async function addPackItem(): Promise<void> {
  if (selected == null) return;
  const skid = val('#addSkid');
  if (!skid) return;
  const itemType = $<HTMLSelectElement>('#addType').value;
  setErr('');
  try {
    const r = await authFetch(`/api/shipments/${selected}/items`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ itemType, refNum: Number(skid) }),
    });
    if (!r.ok) {
      const why = r.status === 409 ? 'that skid is already on this list'
        : r.status === 404 ? 'shipment or skid not found' : `error ${r.status}`;
      setErr(`Add failed: ${why}.`); return;
    }
    $<HTMLInputElement>('#addSkid').value = '';
    $('#itemOk').textContent = '✓ Skid added.';
    await loadPackingItems(selected);
  } catch (e) { setErr(`Add failed: ${(e as Error).message}`); }
}

// Print a shipping document — fetch the server-rendered HTML with auth, open it as a blob URL for printing
// (a plain window.open would not carry the API key / bearer token).
async function printDoc(url: string, label: string): Promise<void> {
  setErr('');
  try {
    const r = await authFetch(url);
    if (!r.ok) { setErr(`${label} failed: ${r.status}`); return; }
    window.open(URL.createObjectURL(await r.blob()), '_blank');
  } catch (e) { setErr(`${label} failed: ${(e as Error).message}`); }
}

async function removePackItem(itemType: string, itemId: number): Promise<void> {
  if (selected == null) return;
  if (!window.confirm(`Remove ${itemType} item ${itemId} from packing list ${selected}?`)) return;
  setErr('');
  try {
    const r = await authFetch(`/api/shipments/${selected}/items/${itemType}/${itemId}`, { method: 'DELETE' });
    if (!r.ok && r.status !== 404) { setErr(`Remove failed (${r.status}).`); return; }
    await loadPackingItems(selected);
  } catch (e) { setErr(`Remove failed: ${(e as Error).message}`); }
}

// Guided close-out: mark the shipment shipped + stamp sent/actual dates in one action
// (POST /shipments/{pl}/close — newer than the committed NSwag client, so via authFetch).
async function closeBol(): Promise<void> {
  if (selected == null) return;
  if (!window.confirm(`Close BOL / mark shipment ${selected} shipped? This stamps the sent + actual dates.`)) return;
  setErr(''); setBusy(true);
  try {
    const r = await authFetch(`/api/shipments/${selected}/close`, { method: 'POST' });
    if (!r.ok) { setErr(`Close failed (${r.status}).`); return; }
    const closedId = selected;
    await loadShipment(closedId);   // re-renders the panel (rebuilds #closeOk), so set the note after
    await search();
    $('#closeOk').textContent = '✓ Shipment closed / marked shipped.';
  } catch (e) { setErr(`Close failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function dispatch(): Promise<void> {
  if (selected == null) return;
  setErr(''); setBusy(true);
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
  } catch (e) { setErr(`Dispatch failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'shipping' });
  main.innerHTML = scaffold();
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  await search();
})();
