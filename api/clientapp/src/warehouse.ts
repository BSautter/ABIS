// ABIS Warehouse — greenfield (Path C) module for the warehouse view/management of
// finished sheet skids (legacy w_wh_business / w_wh_detail windows). It's the
// warehouse side of `sheet_skid`: where each skid is stored, its warehouse ticket,
// and status. A typed SPA on the Phase-2 API — list skids + a warehouse update
// (location / ticket / status) via the new PATCH /sheet-skids/{n}/warehouse.
//
// Compiled by `tsc` to wwwroot/ui/app/warehouse.js; served at /ui/warehouse.html.
import { AbisClient, SheetSkidWarehousePatch } from './generated/abis-client.js';

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
const setErr = (m: string) => { $('#err').textContent = m; };
const setOk = (m: string) => { $('#ok').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const setV = (id: string, value: unknown) => { $<HTMLInputElement>(id).value = value == null ? '' : String(value); };
const num = (n: number | undefined): string => (n == null ? '' : n.toLocaleString());
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());

let editingNum: number | null = null;

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  try {
    const page = await client().listSheetSkids(1, 100, 'sheetSkidNum', 'desc');
    const rows = (page.items ?? []).map((s) => `
      <tr class="click" data-id="${s.sheetSkidNum}">
        <td>${esc(s.sheetSkidNum)}</td><td>${esc(s.abJobNum)}</td><td>${esc(s.sheetSkidDisplayNum)}</td>
        <td>${esc(s.skidLocation)}</td><td>${esc(s.skidTicketIfWhed)}</td><td>${esc(s.skidSheetStatus)}</td>
        <td>${esc(num(s.sheetNetWt))}</td><td>${esc(s.skidPieces)}</td><td>${esc(dShow(s.skidDate))}</td></tr>`).join('');
    $('#skids').innerHTML = rows || '<tr><td colspan="9" class="muted">No sheet skids.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#skids tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadSkid(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadSkid(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const s = await client().getSheetSkid(id);
    editingNum = id;
    $('#detail').classList.remove('disabled');
    $('#formTitle').textContent = `Skid #${id} — ${s.sheetSkidDisplayNum ?? ''}`;
    $('#summary').textContent = `Job ${s.abJobNum ?? ''} · ${num(s.sheetNetWt)} net · ${s.skidPieces ?? ''} pcs`;
    setV('#wLocation', s.skidLocation); setV('#wTicket', s.skidTicketIfWhed); setV('#wStatus', s.skidSheetStatus);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function save(): Promise<void> {
  if (editingNum == null) { setErr('Select a skid first.'); return; }
  setErr(''); setOk(''); setBusy(true);
  const body = new SheetSkidWarehousePatch({
    skidLocation: v('#wLocation') || undefined,
    skidTicketIfWhed: v('#wTicket') || undefined,
    skidSheetStatus: v('#wStatus') ? Number(v('#wStatus')) : undefined,
  });
  try {
    await client().updateSheetSkidWarehouse(editingNum, body);
    setOk(`✓ Warehouse-updated skid #${editingNum}.`);
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function init(): Promise<void> {
  $('#btnSave').addEventListener('click', save);
  $('#btnRefresh').addEventListener('click', () => void search());
  await search();
}

void initShell({ active: 'warehouse', adopt: true }).then(init);
