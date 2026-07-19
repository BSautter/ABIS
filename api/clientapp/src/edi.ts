// ABIS EDI — monitor for the EDI surface (legacy edi window; X12 856 ASN + related), restyled to
// the design system in the shared shell (#4 polish). Read-only: inbound/outbound transactions
// (+ detail), the processing log, the per-customer EDI setup, and the transaction-type lookup.
//
// Compiled by tsc to wwwroot/ui/app/edi.js; served at /ui/edi.html.
import { AbisClient, EdiPartnerProfile, EdiPartnerWrite } from './generated/abis-client.js';
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

    <div class="tabs">${tab('tx', 'Transactions')}${tab('log', 'Processing log')}${tab('partners', 'Partner profiles')}${tab('cust', 'Customer setup')}${tab('types', 'Types')}</div>

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

    <div id="pane-partners" class="card" style="display:none">
      <header><h2>Trading-partner profiles</h2><span class="sub">Per customer + document — how each customer's EDI is framed. Config only; generates/sends nothing.</span></header>
      <div class="body">
        <form id="partForm" class="frow" style="flex-wrap:wrap;gap:8px;margin-bottom:10px">
          <div class="fld"><label>Customer id</label><input id="pCust" inputmode="numeric" style="width:90px" required /></div>
          <div class="fld"><label>Document</label><select id="pSet"><option>861</option><option>870</option><option>846</option><option>856</option><option>863</option></select></div>
          <div class="fld"><label>Variant</label><input id="pVariant" style="width:130px" placeholder="novelis / aleris / …" /></div>
          <div class="fld"><label>Recv qual</label><input id="pRq" style="width:56px" placeholder="09" /></div>
          <div class="fld"><label>Receiver id (DUNS)</label><input id="pRid" style="width:150px" /></div>
          <div class="fld"><label>Comp sep</label><input id="pComp" style="width:50px" /></div>
          <div class="fld"><label>Suffix</label><input id="pSuffix" style="width:50px" placeholder="~" /></div>
          <div class="fld"><label>Version</label><input id="pVer" style="width:66px" placeholder="00401" /></div>
          <div class="fld"><label>GS code</label><input id="pGs" style="width:56px" placeholder="RC" /></div>
          <div class="fld"><label>GS sender</label><input id="pGsSender" style="width:90px" placeholder="(ABCo)" /></div>
          <div class="fld"><label>GS receiver</label><input id="pGsReceiver" style="width:110px" placeholder="(= receiver id)" /></div>
          <div class="fld"><label>File prefix</label><input id="pPrefix" style="width:140px" /></div>
          <div class="fld"><label>Item ref</label><input id="pItemRef" style="width:110px" /></div>
          <div class="fld"><label>Enabled</label><input id="pEnabled" type="checkbox" checked /></div>
          <button class="btn sm" type="submit">Save</button>
          <button class="btn sm ghost" id="pReset" type="button">Clear</button>
        </form>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:920px">
          <thead><tr><th>Cust</th><th>Doc</th><th>On</th><th>Variant</th><th>Receiver</th><th>Comp</th><th>Suf</th><th>Ver</th><th>GS</th><th>Prefix</th><th>Item ref</th><th></th></tr></thead>
          <tbody id="tPart"><tr><td colspan="12" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div>
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

let partners: EdiPartnerProfile[] = [];

async function loadPartners(): Promise<void> {
  try {
    partners = (await client().listEdiPartners(undefined)) ?? [];
    const chip = (on: boolean | undefined) => (on ? '<span class="chip ok">on</span>' : '<span class="chip">off</span>');
    $('#tPart').innerHTML = partners.length ? partners.map((p) => `<tr>
      <td class="mono">${esc(p.customerId)}</td><td class="mono">${esc(p.transactionSet)}</td><td>${chip(p.enabled)}</td>
      <td>${esc(p.variant)}</td><td class="mono">${esc(p.receiverQualifier)}/${esc(p.receiverId)}</td>
      <td class="mono">${esc(p.componentSeparator)}</td><td class="mono">${esc(p.segmentSuffix)}</td>
      <td class="mono">${esc(p.envelopeVersion)}</td><td class="mono">${esc(p.gsFunctionalCode)}</td>
      <td class="mono">${esc(p.filePrefix)}</td><td class="mono">${esc(p.itemReference)}</td>
      <td style="white-space:nowrap"><button class="btn sm ghost pEdit" data-c="${p.customerId}" data-s="${esc(p.transactionSet)}" type="button">edit</button>
        <button class="btn sm ghost pDel" data-c="${p.customerId}" data-s="${esc(p.transactionSet)}" type="button">del</button></td></tr>`).join('')
      : '<tr><td colspan="12" class="muted">No profiles.</td></tr>';
    document.querySelectorAll<HTMLButtonElement>('#tPart .pEdit').forEach((b) => b.addEventListener('click', () =>
      fillPartner(partners.find((x) => String(x.customerId) === b.dataset.c && x.transactionSet === b.dataset.s))));
    document.querySelectorAll<HTMLButtonElement>('#tPart .pDel').forEach((b) => b.addEventListener('click', () =>
      void deletePartner(Number(b.dataset.c), b.dataset.s ?? '')));
  } catch (e) { setErr(`Partner profiles load failed: ${(e as Error).message}`); }
}

function fillPartner(p: EdiPartnerProfile | undefined): void {
  if (!p) return;
  $<HTMLInputElement>('#pCust').value = String(p.customerId ?? '');
  $<HTMLSelectElement>('#pSet').value = p.transactionSet ?? '861';
  $<HTMLInputElement>('#pVariant').value = p.variant ?? '';
  $<HTMLInputElement>('#pRq').value = p.receiverQualifier ?? '';
  $<HTMLInputElement>('#pRid').value = p.receiverId ?? '';
  $<HTMLInputElement>('#pComp').value = p.componentSeparator ?? '';
  $<HTMLInputElement>('#pSuffix').value = p.segmentSuffix ?? '';
  $<HTMLInputElement>('#pVer').value = p.envelopeVersion ?? '';
  $<HTMLInputElement>('#pGs').value = p.gsFunctionalCode ?? '';
  $<HTMLInputElement>('#pGsSender').value = p.gsSenderCode ?? '';
  $<HTMLInputElement>('#pGsReceiver').value = p.gsReceiverCode ?? '';
  $<HTMLInputElement>('#pPrefix').value = p.filePrefix ?? '';
  $<HTMLInputElement>('#pItemRef').value = p.itemReference ?? '';
  $<HTMLInputElement>('#pEnabled').checked = p.enabled !== false;
}

function clearPartner(): void {
  ['#pCust', '#pVariant', '#pRq', '#pRid', '#pComp', '#pSuffix', '#pVer', '#pGs', '#pGsSender', '#pGsReceiver', '#pPrefix', '#pItemRef']
    .forEach((id) => { $<HTMLInputElement>(id).value = ''; });
  $<HTMLInputElement>('#pEnabled').checked = true;
}

async function savePartner(): Promise<void> {
  setErr('');
  const customerId = Number(val('#pCust'));
  const set = $<HTMLSelectElement>('#pSet').value;
  if (!customerId) { setErr('Customer id is required.'); return; }
  const opt = (id: string) => { const v = val(id); return v === '' ? undefined : v; };
  setBusy(true);
  try {
    await client().upsertEdiPartner(customerId, set, new EdiPartnerWrite({
      enabled: $<HTMLInputElement>('#pEnabled').checked, variant: opt('#pVariant'),
      receiverQualifier: opt('#pRq'), receiverId: opt('#pRid'), componentSeparator: opt('#pComp'),
      segmentSuffix: opt('#pSuffix'), envelopeVersion: opt('#pVer'), gsFunctionalCode: opt('#pGs'),
      gsSenderCode: opt('#pGsSender'), gsReceiverCode: opt('#pGsReceiver'),
      filePrefix: opt('#pPrefix'), itemReference: opt('#pItemRef'),
    }));
    await loadPartners();
  } catch (e) { setErr(`Save failed: ${ediErr(e)}`); }
  finally { setBusy(false); }
}

// Surface a 403 from the feature gate clearly (the generated client's default message is unhelpful).
function ediErr(e: unknown): string {
  const ex = e as { status?: number; message?: string };
  return ex?.status === 403 ? 'you need the EDI permission for this action.' : (ex?.message ?? String(e));
}

async function deletePartner(customerId: number, set: string): Promise<void> {
  if (!confirm(`Remove the ${set} profile for customer ${customerId}?`)) return;
  setBusy(true);
  try { await client().deleteEdiPartner(customerId, set); await loadPartners(); }
  catch (e) { setErr(`Delete failed: ${ediErr(e)}`); }
  finally { setBusy(false); }
}

function showTab(name: string): void {
  ['tx', 'log', 'partners', 'cust', 'types'].forEach((t) => {
    $(`#pane-${t}`).style.display = t === name ? '' : 'none';
    $(`#tab-${t}`).classList.toggle('active', t === name);
  });
}

(async () => {
  const main = await initShell({ active: 'edi' });
  main.innerHTML = scaffold();
  ['tx', 'log', 'partners', 'cust', 'types'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
  $<HTMLFormElement>('#txForm').addEventListener('submit', (e) => { e.preventDefault(); void loadTransactions(); });
  $<HTMLFormElement>('#logForm').addEventListener('submit', (e) => { e.preventDefault(); void loadLog(); });
  $<HTMLFormElement>('#partForm').addEventListener('submit', (e) => { e.preventDefault(); void savePartner(); });
  $('#pReset').addEventListener('click', () => clearPartner());
  showTab('tx');
  await Promise.all([loadTransactions(), loadLog(), loadPartners(), loadCustomers(), loadTypes()]);
})();
