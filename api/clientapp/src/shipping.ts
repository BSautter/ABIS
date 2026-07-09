// ABIS Shipping — fourth greenfield (Path C) module, replacing the legacy
// shipping window. A typed SPA on the Phase-2 API: shipment search, detail, and
// the dispatch action (status / vehicle / sent + actual times / notes) via PATCH,
// all through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/shipping.js; served at /ui/shipping.html.
import { AbisClient, ShipmentStatusPatch } from './generated/abis-client.js';

import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client(): AbisClient {
  return new AbisClient('', { fetch: authFetch });
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string));
const dt = (v: Date | undefined): string => (v == null ? '' : v.toISOString().slice(0, 10));
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const dateOrUndef = (id: string): Date | undefined => (val(id) ? new Date(val(id)) : undefined);

let selected: number | null = null;

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const customerId = val('#fCustomer') ? Number(val('#fCustomer')) : undefined;
  try {
    const page = await client().listShipments(1, 50, customerId, undefined, undefined);
    const rows = (page.items ?? []).map((s) => `
      <tr class="click" data-id="${s.packingList}">
        <td>${esc(s.packingList)}</td>
        <td>${esc(s.billOfLading)}</td>
        <td>${esc(s.customerId)}</td>
        <td>${esc(s.shipmentStatus)}</td>
        <td>${esc(s.vehicleId)}</td>
      </tr>`).join('');
    $('#shipments').innerHTML = rows || '<tr><td colspan="5" class="muted">No matching shipments.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#shipments tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadShipment(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadShipment(id: number): Promise<void> {
  setErr(''); setBusy(true); selected = id;
  try {
    const s = await client().getShipment(id);
    $('#detail').innerHTML = `
      <div class="kv">
        <span><b>Packing list</b> ${esc(s.packingList)}</span>
        <span><b>BOL</b> ${esc(s.billOfLading)}</span>
        <span><b>Customer</b> ${esc(s.customerId)}</span>
        <span><b>Carrier</b> ${esc(s.carrierId)}</span>
        <span><b>Sent</b> ${esc(dt(s.dateSent))}</span>
      </div>
      <h2 style="margin-top:12px">Dispatch</h2>
      <div class="row">
        <div><label>Status</label><br /><input id="dStatus" value="${esc(s.shipmentStatus)}" style="width:70px" /></div>
        <div><label>Vehicle status</label><br /><input id="dVehicle" value="${esc(s.vehicleStatus)}" style="width:90px" /></div>
        <div><label>Date sent</label><br /><input id="dSent" type="date" value="${esc(dt(s.dateSent))}" /></div>
        <div><label>Actual</label><br /><input id="dActual" type="date" value="${esc(dt(s.shipmentActualedDateTime))}" /></div>
      </div>
      <div class="row" style="margin-top:6px">
        <div style="flex:1"><label>Notes</label><br /><input id="dNotes" value="${esc(s.shipmentNotes)}" style="width:100%" /></div>
        <button id="btnDispatch" type="button">Save dispatch</button>
      </div>`;
    $('#btnDispatch').addEventListener('click', dispatch);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
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
    $('#detail').insertAdjacentHTML('afterbegin', '<div class="ok">✓ Dispatch saved.</div>');
    await search();
  } catch (e) { setErr(`Dispatch failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function init(): void {
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  void search();
}

void initShell({ active: 'shipping', adopt: true }).then(init);
