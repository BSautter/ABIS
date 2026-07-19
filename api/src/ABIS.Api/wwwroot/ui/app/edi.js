// ABIS EDI — monitor for the EDI surface (legacy edi window; X12 856 ASN + related), restyled to
// the design system in the shared shell (#4 polish). Read-only: inbound/outbound transactions
// (+ detail), the processing log, the per-customer EDI setup, and the transaction-type lookup.
//
// Compiled by tsc to wwwroot/ui/app/edi.js; served at /ui/edi.html.
import { AbisClient, EdiPartnerWrite, Edi997IngestWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const val = (id) => $(id).value.trim();
const dShow = (d) => (d == null ? '' : d.toLocaleString());
const faChip = (s) => (s ?? 0) >= 1 ? '<span class="chip ok">FA received</span>' : '<span class="chip warn">awaiting</span>';
// 997 age bucket → chip: fresh (ack window open) / waiting (2–24h, chase it) / overdue (>24h).
const bucketChip = (b) => {
    const cls = b === 'overdue' ? 'crit' : b === 'waiting' ? 'warn' : 'info';
    return `<span class="chip ${cls}">${esc(b ?? '')}</span>`;
};
function scaffold() {
    const tab = (id, label) => `<button id="tab-${id}" type="button">${label}</button>`;
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">EDI · Monitor</div><h1>EDI operations</h1></div></div>
    <div id="err" class="err" style="margin-bottom:12px"></div>

    <div class="tabs">${tab('tx', 'Transactions')}${tab('acks', 'Functional acks (997)')}${tab('log', 'Processing log')}${tab('partners', 'Partner profiles')}${tab('cust', 'Customer setup')}${tab('types', 'Types')}</div>

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

    <div id="pane-acks" class="grid" style="display:none">
      <div class="stack"><div class="card">
        <header><h2>Waiting on 997</h2><span class="sub" id="cWait"></span></header>
        <div class="body"><p class="muted" style="margin:0 0 4px">Outbound transactions with no functional acknowledgment yet — the in-app form of the legacy <span class="mono">check_997.sh</span>, oldest first. <b>fresh</b> &lt;2h (window open) · <b>waiting</b> 2–24h (chase it) · <b>overdue</b> &gt;24h.</p></div>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:660px">
          <thead><tr><th>Age</th><th>Age (h)</th><th>File</th><th>Doc</th><th>Cust</th><th>Group ctl</th><th>Time</th></tr></thead>
          <tbody id="tWait"><tr><td colspan="7" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2>Ingest a 997</h2></header>
        <div class="body">
          <p class="muted" style="margin:0 0 8px">Paste an inbound 997 (Functional Acknowledgment) to reconcile it against the outbound ledger — matched by group control number (= EDI file id). Parse + store only; never transmits.</p>
          <form id="ingForm">
            <div class="fld" style="margin-bottom:8px"><label>Source name (optional)</label><input id="iName" style="width:240px" placeholder="e.g. 997_in_1001.x12" /></div>
            <textarea id="iPayload" rows="9" style="width:100%;font-family:var(--mono,monospace);font-size:12px" placeholder="ISA*00*          *00*          *ZZ*PARTNER..."></textarea>
            <div style="margin-top:8px"><button class="btn sm" type="submit">Ingest &amp; reconcile</button></div>
          </form>
          <div id="ingResult" class="kv" style="margin-top:12px"></div>
        </div>
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
        <div style="overflow-x:auto"><table class="tbl" style="min-width:1020px">
          <thead><tr><th>Cust</th><th>Customer</th><th>Doc</th><th>On</th><th>Variant</th><th>Receiver</th><th>Comp</th><th>Suf</th><th>Ver</th><th>GS</th><th>Prefix</th><th>Item ref</th><th></th></tr></thead>
          <tbody id="tPart"><tr><td colspan="13" class="muted">Loading…</td></tr></tbody>
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
async function loadTransactions() {
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
        document.querySelectorAll('#tTx tr.click').forEach((tr) => tr.addEventListener('click', () => void loadTxDetail(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Transactions load failed: ${e.message}`);
    }
}
async function loadTxDetail(id) {
    setBusy(true);
    try {
        const x = await client().getEdiTransaction(id);
        $('#txDetail').innerHTML = Object.entries(x)
            .filter(([, val2]) => val2 != null)
            .map(([k, val2]) => `<span><b>${esc(k)}</b>${esc(val2 instanceof Date ? val2.toLocaleString() : val2)}</span>`)
            .join('');
    }
    catch (e) {
        setErr(`Detail load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadWaiting() {
    setErr('');
    try {
        const r = await client().edi997Waiting(1, 100, undefined);
        const items = r.items ?? [];
        $('#tWait').innerHTML = items.length ? items.map((x) => `<tr>
      <td>${bucketChip(x.bucket)}</td><td class="mono">${esc((x.ageHours ?? 0).toFixed(1))}</td>
      <td class="mono">${esc(x.ediFileId)}</td><td class="mono">${esc(x.transactionTypeId)}</td>
      <td class="mono">${esc(x.customerId)}</td><td class="mono">${esc(x.groupControlNumber)}</td>
      <td class="mono">${esc(dShow(x.transactionTime))}</td></tr>`).join('')
            : '<tr><td colspan="7" class="muted">Nothing waiting on a 997.</td></tr>';
        $('#cWait').textContent =
            `${(r.totalWaiting ?? 0).toLocaleString()} waiting · ${r.waitingCount ?? 0} to chase · ${r.overdueCount ?? 0} overdue`;
    }
    catch (e) {
        setErr(`997 waiting load failed: ${e.message}`);
    }
}
async function ingest997() {
    setErr('');
    const payload = $('#iPayload').value.trim();
    if (!payload) {
        setErr('Paste a 997 payload first.');
        return;
    }
    setBusy(true);
    try {
        const r = await client().edi997Ingest(new Edi997IngestWrite({ payload, sourceName: val('#iName') || undefined }));
        const row = (k, v) => `<span><b>${esc(k)}</b>${esc(v)}</span>`;
        const warn = (r.warnings ?? []).length ? `<span><b>warnings</b>${esc((r.warnings ?? []).join('; '))}</span>` : '';
        $('#ingResult').innerHTML =
            row('acks parsed', r.acksParsed) + row('matched', r.matched) + row('unmatched', r.unmatched) +
                row('accepted', r.accepted) + row('rejected', r.rejected) + row('partial', r.partial) +
                row('already acked', r.alreadyAcked) + warn;
        await Promise.all([loadWaiting(), loadTransactions()]);
    }
    catch (e) {
        setErr(`997 ingest failed: ${ediErr(e)}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadLog() {
    setErr('');
    const customerId = val('#fCustLog') ? Number(val('#fCustLog')) : undefined;
    try {
        const page = await client().listEdiLog(1, 50, customerId, 'ediLogTimestamp', 'desc');
        $('#tLog').innerHTML = (page.items ?? []).length ? (page.items ?? []).map((x) => `<tr>
      <td class="mono">${esc(dShow(x.ediLogTimestamp))}</td><td class="mono">${esc(x.customerId)}</td><td>${esc(x.customerEdiName)}</td>
      <td class="mono">${esc(x.ediLogFlag)}</td><td class="mono">${esc(x.ediFileId)}</td><td>${esc(x.ediLogContents)}</td></tr>`).join('')
            : '<tr><td colspan="6" class="muted">No log entries.</td></tr>';
        $('#cLog').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
    }
    catch (e) {
        setErr(`Log load failed: ${e.message}`);
    }
}
async function loadCustomers() {
    try {
        const list = await client().listCustomerEdi();
        $('#tCust').innerHTML = (list ?? []).length ? (list ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.customerId)}</td><td>${esc(x.customerEdiName)}</td><td class="mono">${esc(x.ediTypeId)}</td>
      <td class="mono">${esc(x.ediVersion)}</td><td>${esc(x.customerEdiDesc)}</td></tr>`).join('')
            : '<tr><td colspan="5" class="muted">No customer EDI setups.</td></tr>';
    }
    catch (e) {
        setErr(`Customer EDI load failed: ${e.message}`);
    }
}
async function loadTypes() {
    try {
        const list = await client().listEdiTypes();
        $('#tTypes').innerHTML = (list ?? []).length ? (list ?? []).map((x) => `<tr>
      <td class="mono">${esc(x.ediTypeId)}</td><td class="mono">${esc(x.ediVersion)}</td><td>${esc(x.ediTypeDescription)}</td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No types.</td></tr>';
    }
    catch (e) {
        setErr(`Types load failed: ${e.message}`);
    }
}
let partners = [];
async function loadPartners() {
    try {
        partners = (await client().listEdiPartners(undefined)) ?? [];
        const chip = (on) => (on ? '<span class="chip ok">on</span>' : '<span class="chip">off</span>');
        $('#tPart').innerHTML = partners.length ? partners.map((p) => `<tr>
      <td class="mono">${esc(p.customerId)}</td><td>${esc(p.customerName ?? '')}</td><td class="mono">${esc(p.transactionSet)}</td><td>${chip(p.enabled)}</td>
      <td>${esc(p.variant)}</td><td class="mono">${esc(p.receiverQualifier)}/${esc(p.receiverId)}</td>
      <td class="mono">${esc(p.componentSeparator)}</td><td class="mono">${esc(p.segmentSuffix)}</td>
      <td class="mono">${esc(p.envelopeVersion)}</td><td class="mono">${esc(p.gsFunctionalCode)}</td>
      <td class="mono">${esc(p.filePrefix)}</td><td class="mono">${esc(p.itemReference)}</td>
      <td style="white-space:nowrap"><button class="btn sm ghost pEdit" data-c="${p.customerId}" data-s="${esc(p.transactionSet)}" type="button">edit</button>
        <button class="btn sm ghost pDel" data-c="${p.customerId}" data-s="${esc(p.transactionSet)}" type="button">del</button></td></tr>`).join('')
            : '<tr><td colspan="13" class="muted">No profiles.</td></tr>';
        document.querySelectorAll('#tPart .pEdit').forEach((b) => b.addEventListener('click', () => fillPartner(partners.find((x) => String(x.customerId) === b.dataset.c && x.transactionSet === b.dataset.s))));
        document.querySelectorAll('#tPart .pDel').forEach((b) => b.addEventListener('click', () => void deletePartner(Number(b.dataset.c), b.dataset.s ?? '')));
    }
    catch (e) {
        setErr(`Partner profiles load failed: ${e.message}`);
    }
}
function fillPartner(p) {
    if (!p)
        return;
    $('#pCust').value = String(p.customerId ?? '');
    $('#pSet').value = p.transactionSet ?? '861';
    $('#pVariant').value = p.variant ?? '';
    $('#pRq').value = p.receiverQualifier ?? '';
    $('#pRid').value = p.receiverId ?? '';
    $('#pComp').value = p.componentSeparator ?? '';
    $('#pSuffix').value = p.segmentSuffix ?? '';
    $('#pVer').value = p.envelopeVersion ?? '';
    $('#pGs').value = p.gsFunctionalCode ?? '';
    $('#pGsSender').value = p.gsSenderCode ?? '';
    $('#pGsReceiver').value = p.gsReceiverCode ?? '';
    $('#pPrefix').value = p.filePrefix ?? '';
    $('#pItemRef').value = p.itemReference ?? '';
    $('#pEnabled').checked = p.enabled !== false;
}
function clearPartner() {
    ['#pCust', '#pVariant', '#pRq', '#pRid', '#pComp', '#pSuffix', '#pVer', '#pGs', '#pGsSender', '#pGsReceiver', '#pPrefix', '#pItemRef']
        .forEach((id) => { $(id).value = ''; });
    $('#pEnabled').checked = true;
}
async function savePartner() {
    setErr('');
    const customerId = Number(val('#pCust'));
    const set = $('#pSet').value;
    if (!customerId) {
        setErr('Customer id is required.');
        return;
    }
    const opt = (id) => { const v = val(id); return v === '' ? undefined : v; };
    setBusy(true);
    try {
        await client().upsertEdiPartner(customerId, set, new EdiPartnerWrite({
            enabled: $('#pEnabled').checked, variant: opt('#pVariant'),
            receiverQualifier: opt('#pRq'), receiverId: opt('#pRid'), componentSeparator: opt('#pComp'),
            segmentSuffix: opt('#pSuffix'), envelopeVersion: opt('#pVer'), gsFunctionalCode: opt('#pGs'),
            gsSenderCode: opt('#pGsSender'), gsReceiverCode: opt('#pGsReceiver'),
            filePrefix: opt('#pPrefix'), itemReference: opt('#pItemRef'),
        }));
        await loadPartners();
    }
    catch (e) {
        setErr(`Save failed: ${ediErr(e)}`);
    }
    finally {
        setBusy(false);
    }
}
// Surface a 403 from the feature gate clearly (the generated client's default message is unhelpful).
function ediErr(e) {
    const ex = e;
    return ex?.status === 403 ? 'you need the EDI permission for this action.' : (ex?.message ?? String(e));
}
async function deletePartner(customerId, set) {
    if (!confirm(`Remove the ${set} profile for customer ${customerId}?`))
        return;
    setBusy(true);
    try {
        await client().deleteEdiPartner(customerId, set);
        await loadPartners();
    }
    catch (e) {
        setErr(`Delete failed: ${ediErr(e)}`);
    }
    finally {
        setBusy(false);
    }
}
function showTab(name) {
    ['tx', 'acks', 'log', 'partners', 'cust', 'types'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
}
(async () => {
    const main = await initShell({ active: 'edi' });
    main.innerHTML = scaffold();
    ['tx', 'acks', 'log', 'partners', 'cust', 'types'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#txForm').addEventListener('submit', (e) => { e.preventDefault(); void loadTransactions(); });
    $('#ingForm').addEventListener('submit', (e) => { e.preventDefault(); void ingest997(); });
    $('#logForm').addEventListener('submit', (e) => { e.preventDefault(); void loadLog(); });
    $('#partForm').addEventListener('submit', (e) => { e.preventDefault(); void savePartner(); });
    $('#pReset').addEventListener('click', () => clearPartner());
    showTab('tx');
    await Promise.all([loadTransactions(), loadWaiting(), loadLog(), loadPartners(), loadCustomers(), loadTypes()]);
})();
