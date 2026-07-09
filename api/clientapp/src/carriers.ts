// ABIS Carriers — greenfield (Path C) master-data module for the carrier master,
// referenced by shipping. A typed SPA on the Phase-2 API: status-filtered search +
// load → edit → save (create/replace, re-sending carrierFullName which is required).
// Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/carriers.js; served at /ui/carriers.html.
import { AbisClient, CarrierWrite } from './generated/abis-client.js';

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

let editingId: number | null = null;

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
  try {
    const page = await client().listCarriers(1, 50, status, undefined, undefined);
    const rows = (page.items ?? []).map((c) => `
      <tr class="click" data-id="${c.carrierId}">
        <td>${esc(c.carrierId)}</td>
        <td>${esc(c.scac)}</td>
        <td>${esc(c.carrierFullName)}</td>
        <td>${esc(c.carrierTypeCode)}</td>
        <td>${esc([c.carrierCity, c.carrierState].filter(Boolean).join(', '))}</td>
        <td>${esc(c.carrierPhoneNumber)}</td>
        <td>${esc(c.status)}</td>
      </tr>`).join('');
    $('#carriers').innerHTML = rows || '<tr><td colspan="7" class="muted">No matching carriers.</td></tr>';
    $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#carriers tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadCarrier(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadCarrier(id: number): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  try {
    const c = await client().getCarrier(id);
    editingId = id;
    $('#formTitle').textContent = `Edit carrier #${id}`;
    setV('#cScac', c.scac); setV('#cName', c.carrierFullName); setV('#cType', c.carrierTypeCode);
    setV('#cCity', c.carrierCity); setV('#cState', c.carrierState);
    setV('#cPhone', c.carrierPhoneNumber); setV('#cStatus', c.status);
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

function newCarrier(): void {
  editingId = null;
  $('#formTitle').textContent = 'New carrier';
  ['#cScac', '#cName', '#cType', '#cCity', '#cState', '#cPhone', '#cStatus'].forEach((id) => setV(id, ''));
  setOk(''); setErr('');
}

async function save(): Promise<void> {
  setErr(''); setOk(''); setBusy(true);
  const body = new CarrierWrite({
    scac: v('#cScac') || undefined,
    carrierFullName: v('#cName') || undefined,
    carrierTypeCode: v('#cType') || undefined,
    carrierCity: v('#cCity') || undefined,
    carrierState: v('#cState') || undefined,
    carrierPhoneNumber: v('#cPhone') || undefined,
    status: v('#cStatus') ? Number(v('#cStatus')) : undefined,
  });
  try {
    if (editingId == null) {
      const created = await client().createCarrier(body);
      setOk(`✓ Created carrier #${created.carrierId}.`);
    } else {
      await client().updateCarrier(editingId, body);
      setOk(`✓ Saved carrier #${editingId}.`);
    }
    await search();
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function init(): Promise<void> {
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnNew').addEventListener('click', newCarrier);
  $('#btnSave').addEventListener('click', save);
  newCarrier();
  await search();
}

void initShell({ active: 'carriers', adopt: true }).then(init);
