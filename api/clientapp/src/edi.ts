// ABIS EDI — greenfield (Path C) monitor for the EDI surface (legacy edi window;
// X12 856 ASN + related). Read-only: inbound/outbound transactions (+ detail), the
// processing log, the per-customer EDI setup, and the transaction-type lookup.
// A typed SPA on the Phase-2 API through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/edi.js; served at /ui/edi.html.
import { AbisClient } from './generated/abis-client.js';

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
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const v = (id: string) => $<HTMLInputElement>(id).value.trim();
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());

async function loadTransactions(): Promise<void> {
  setErr('');
  const customerId = v('#fCust') ? Number(v('#fCust')) : undefined;
  try {
    const page = await client().listEdiTransactions(1, 50, customerId, undefined, 'transactionTime', 'desc');
    $('#tTx').innerHTML = (page.items ?? []).map((x) => `
      <tr class="click" data-id="${x.ediFileId}">
        <td>${esc(x.ediFileId)}</td><td>${esc(dShow(x.transactionTime))}</td>
        <td>${esc(x.dunsFrom)} → ${esc(x.dunsTo)}</td><td>${esc(x.customerSentTo)}</td>
        <td>${esc(x.ediFileName)}</td><td>${esc(x.faReceiveStatus)}</td></tr>`).join('')
      || '<tr><td colspan="6" class="muted">No transactions.</td></tr>';
    $('#cTx').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#tTx tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadTxDetail(Number(tr.dataset.id))));
  } catch (e) { setErr(`Transactions load failed: ${(e as Error).message}`); }
}

async function loadTxDetail(id: number): Promise<void> {
  setBusy(true);
  try {
    const x = await client().getEdiTransaction(id);
    $('#txDetail').innerHTML = Object.entries(x as unknown as Record<string, unknown>)
      .map(([k, val]) => `<div><span class="k">${esc(k)}</span><span>${esc(val instanceof Date ? val.toLocaleString() : val)}</span></div>`)
      .join('');
  } catch (e) { setErr(`Detail load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadLog(): Promise<void> {
  setErr('');
  const customerId = v('#fCustLog') ? Number(v('#fCustLog')) : undefined;
  try {
    const page = await client().listEdiLog(1, 50, customerId, 'ediLogTimestamp', 'desc');
    $('#tLog').innerHTML = (page.items ?? []).map((x) => `<tr>
      <td>${esc(dShow(x.ediLogTimestamp))}</td><td>${esc(x.customerId)}</td><td>${esc(x.customerEdiName)}</td>
      <td>${esc(x.ediLogFlag)}</td><td>${esc(x.ediFileId)}</td><td>${esc(x.ediLogContents)}</td></tr>`).join('')
      || '<tr><td colspan="6" class="muted">No log entries.</td></tr>';
    $('#cLog').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Log load failed: ${(e as Error).message}`); }
}

async function loadCustomers(): Promise<void> {
  try {
    const list = await client().listCustomerEdi();
    $('#tCust').innerHTML = (list ?? []).map((x) => `<tr>
      <td>${esc(x.customerId)}</td><td>${esc(x.customerEdiName)}</td><td>${esc(x.ediTypeId)}</td>
      <td>${esc(x.ediVersion)}</td><td>${esc(x.customerEdiDesc)}</td></tr>`).join('')
      || '<tr><td colspan="5" class="muted">No customer EDI setups.</td></tr>';
  } catch (e) { setErr(`Customer EDI load failed: ${(e as Error).message}`); }
}

async function loadTypes(): Promise<void> {
  try {
    const list = await client().listEdiTypes();
    $('#tTypes').innerHTML = (list ?? []).map((x) => `<tr>
      <td>${esc(x.ediTypeId)}</td><td>${esc(x.ediVersion)}</td><td>${esc(x.ediTypeDescription)}</td></tr>`).join('')
      || '<tr><td colspan="3" class="muted">No types.</td></tr>';
  } catch (e) { setErr(`Types load failed: ${(e as Error).message}`); }
}

function showTab(name: string): void {
  ['tx', 'log', 'cust', 'types'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
}

async function init(): Promise<void> {
  ['tx', 'log', 'cust', 'types'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $<HTMLFormElement>('#txForm').addEventListener('submit', (e) => { e.preventDefault(); void loadTransactions(); });
  $<HTMLFormElement>('#logForm').addEventListener('submit', (e) => { e.preventDefault(); void loadLog(); });
  showTab('tx');
  await Promise.all([loadTransactions(), loadLog(), loadCustomers(), loadTypes()]);
}

void initShell({ active: 'edi', adopt: true }).then(init);
