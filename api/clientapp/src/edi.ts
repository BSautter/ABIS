// ABIS EDI — monitor for the EDI surface (legacy edi window; X12 856 ASN + related), restyled to
// the design system in the shared shell (#4 polish). Read-only: inbound/outbound transactions
// (+ detail), the processing log, the per-customer EDI setup, and the transaction-type lookup.
//
// Compiled by tsc to wwwroot/ui/app/edi.js; served at /ui/edi.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();
const dShow = (d: Date | undefined): string => (d == null ? '' : d.toLocaleString());
const faChip = (s: number | undefined): string =>
  (s ?? 0) >= 1 ? '<span class="chip ok">FA received</span>' : '<span class="chip warn">awaiting</span>';

function scaffold(): string {
  const tab = (id: string, label: string) => `<button id="tab-${id}" type="button">${label}</button>`;
  return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">EDI · Monitor</div><h1>EDI operations</h1></div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="tabs">${tab('tx', 'Transactions')}${tab('log', 'Processing log')}${tab('cust', 'Customer setup')}${tab('types', 'Types')}</div>

    <div id="pane-tx" class="grid">
      <div class="stack"><div class="card">
        <header><h2>Outbound / inbound transactions</h2><span class="sub" id="cTx"></span></header>
        <div class="body"><form id="txForm" class="frow" style="margin-bottom:8px"><div class="fld"><label>Customer id</label><input id="fCust" inputmode="numeric" style="width:110px" placeholder="any" /></div><button class="btn sm" type="submit">Filter</button></form></div>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:620px">
          <thead><tr><th>File</th><th>Time</th><th>DUNS from → to</th><th>Sent to</th><th>Name</th><th>FA</th></tr></thead>
          <tbody id="tTx"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2>Transaction detail</h2></header>
        <div class="body kv" id="txDetail"><p class="muted">Select a transaction.</p></div>
      </div></div>
    </div>

    <div id="pane-log" class="card" style="display:none">
      <header><h2>Processing log</h2><span class="sub" id="cLog"></span></header>
      <div class="body"><form id="logForm" class="frow" style="margin-bottom:8px"><div class="fld"><label>Customer id</label><input id="fCustLog" inputmode="numeric" style="width:110px" placeholder="any" /></div><button class="btn sm" type="submit">Filter</button></form></div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
        <thead><tr><th>When</th><th>Cust</th><th>EDI name</th><th>Flag</th><th>File</th><th>Contents</th></tr></thead>
        <tbody id="tLog"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div id="pane-cust" class="card" style="display:none">
      <header><h2>Per-customer EDI setup</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
        <thead><tr><th>Cust</th><th>Name</th><th>Type</th><th>Version</th><th>Description</th></tr></thead>
        <tbody id="tCust"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div id="pane-types" class="card" style="display:none">
      <header><h2>Transaction types</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:360px">
        <thead><tr><th>Type</th><th>Version</th><th>Description</th></tr></thead>
        <tbody id="tTypes"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>
  </div>`;
}

async function loadTransactions(): Promise<void> {
  setErr('');
  const customerId = val('#fCust') ? Number(val('#fCust')) : undefined;
  try {
    const page = await client().listEdiTransactions(1, 50, customerId, undefined, 'transactionTime', 'desc');
    $('#tTx').innerHTML = (page.items ?? []).length ? (page.items ?? []).map((x) => `
      <tr class="click" data-id="${x.ediFileId}">
        <td class="mono">${esc(x.ediFileId)}</td><td class="mono">${esc(dShow(x.transactionTime))}</td>
        <td class="mono">${esc(x.dunsFrom)} → ${esc(x.dunsTo)}</td><td>${esc(x.customerSentTo)}</td>
        <td class="mono">${esc(x.ediFileName)}</td><td>${faChip(x.faReceiveStatus)}</td></tr>`).join('')
      : '<tr><td colspan="6" class="muted">No transactions.</td></tr>';
    $('#cTx').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    document.querySelectorAll<HTMLTableRowElement>('#tTx tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadTxDetail(Number(tr.dataset.id))));
  } catch (e) { setErr(`Transactions load failed: ${(e as Error).message}`); }
}

async function loadTxDetail(id: number): Promise<void> {
  setBusy(true);
  try {
    const x = await client().getEdiTransaction(id);
    $('#txDetail').innerHTML = Object.entries(x as unknown as Record<string, unknown>)
      .filter(([, val2]) => val2 != null)
      .map(([k, val2]) => `<span><b>${esc(k)}</b>${esc(val2 instanceof Date ? val2.toLocaleString() : val2)}</span>`)
      .join('');
  } catch (e) { setErr(`Detail load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadLog(): Promise<void> {
  setErr('');
  const customerId = val('#fCustLog') ? Number(val('#fCustLog')) : undefined;
  try {
    const page = await client().listEdiLog(1, 50, customerId, 'ediLogTimestamp', 'desc');
    $('#tLog').innerHTML = (page.items ?? []).length ? (page.items ?? []).map((x) => `<tr>
      <td class="mono">${esc(dShow(x.ediLogTimestamp))}</td><td class="mono">${esc(x.customerId)}</td><td>${esc(x.customerEdiName)}</td>
      <td class="mono">${esc(x.ediLogFlag)}</td><td class="mono">${esc(x.ediFileId)}</td><td>${esc(x.ediLogContents)}</td></tr>`).join('')
      : '<tr><td colspan="6" class="muted">No log entries.</td></tr>';
    $('#cLog').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
  } catch (e) { setErr(`Log load failed: ${(e as Error).message}`); }
}

async function loadCustomers(): Promise<void> {
  try {
    const list = await client().listCustomerEdi();
    $('#tCust').innerHTML = (list ?? []).length ? (list ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.customerId)}</td><td>${esc(x.customerEdiName)}</td><td class="mono">${esc(x.ediTypeId)}</td>
      <td class="mono">${esc(x.ediVersion)}</td><td>${esc(x.customerEdiDesc)}</td></tr>`).join('')
      : '<tr><td colspan="5" class="muted">No customer EDI setups.</td></tr>';
  } catch (e) { setErr(`Customer EDI load failed: ${(e as Error).message}`); }
}

async function loadTypes(): Promise<void> {
  try {
    const list = await client().listEdiTypes();
    $('#tTypes').innerHTML = (list ?? []).length ? (list ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.ediTypeId)}</td><td class="mono">${esc(x.ediVersion)}</td><td>${esc(x.ediTypeDescription)}</td></tr>`).join('')
      : '<tr><td colspan="3" class="muted">No types.</td></tr>';
  } catch (e) { setErr(`Types load failed: ${(e as Error).message}`); }
}

function showTab(name: string): void {
  ['tx', 'log', 'cust', 'types'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
}

(async () => {
  const main = await initShell({ active: 'edi' });
  main.innerHTML = scaffold();
  ['tx', 'log', 'cust', 'types'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $<HTMLFormElement>('#txForm').addEventListener('submit', (e) => { e.preventDefault(); void loadTransactions(); });
  $<HTMLFormElement>('#logForm').addEventListener('submit', (e) => { e.preventDefault(); void loadLog(); });
  showTab('tx');
  await Promise.all([loadTransactions(), loadLog(), loadCustomers(), loadTypes()]);
})();
