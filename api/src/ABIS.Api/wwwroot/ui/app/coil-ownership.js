// ABIS Coil Ownership Transfer — toll processing (legacy w_coil_ownership_transfer), restyled to
// the design system in the shared shell (#4 polish). The transfer ledger, a coil picker +
// new-transfer form (issues a certificate and re-points the coil owner), and a certificate view.
//
// Compiled by tsc to wwwroot/ui/app/coil-ownership.js; served at /ui/coil-ownership.html.
import { AbisClient, CoilOwnershipTransferWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const num = (n) => (n == null ? '' : n.toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleString());
function scaffold() {
    const tab = (id, label) => `<button id="tab-${id}" type="button">${label}</button>`;
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Logistics · Toll processing</div><h1>Coil ownership transfer</h1></div></div>
    <div id="err" class="err" style="margin-bottom:8px"></div>
    <div class="tabs">${tab('ledger', 'Transfer ledger')}${tab('new', 'New transfer')}</div>

    <div id="pane-ledger" class="grid">
      <div class="stack"><div class="card">
        <header><h2>Transfer ledger</h2></header>
        <div class="body"><form id="ledgerForm" class="frow" style="margin-bottom:8px"><div class="fld"><label>Customer id</label><input id="fLedgerCust" inputmode="numeric" style="width:110px" placeholder="any" /></div><button class="btn sm" type="submit">Filter</button></form></div>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:640px">
          <thead><tr><th>Cert</th><th>Coil</th><th>From → to</th><th>Metal</th><th class="num">Balance</th><th>When</th><th>By</th></tr></thead>
          <tbody id="tLedger"><tr><td colspan="7" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card disabled" id="cert">
        <header><h2 id="certTitle">Certificate</h2></header>
        <div class="body" id="certBody"><p class="muted">Select a transfer to view its certificate.</p></div>
      </div></div>
    </div>

    <div id="pane-new" class="grid" style="display:none">
      <div class="stack"><div class="card">
        <header><h2>Pick a coil to transfer</h2></header>
        <div class="body"><form id="coilForm" class="frow" style="margin-bottom:8px">
          <div class="fld"><label>Customer id</label><input id="fCoilCust" inputmode="numeric" style="width:110px" placeholder="any" /></div>
          <div class="fld"><label>Search</label><input id="fCoilSearch" style="width:130px" placeholder="coil / lot" /></div>
          <button class="btn sm" type="submit">Find coils</button>
        </form></div>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
          <thead><tr><th>Coil</th><th>Owner</th><th>Org</th><th>Lot</th><th>Metal</th><th class="num">Balance</th></tr></thead>
          <tbody id="tCoils"><tr><td colspan="6" class="muted">Find transferable coils.</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2>New transfer</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>Coil (orig)</label><input id="tCoilOrig" inputmode="numeric" style="width:120px" /></div>
            <div class="fld"><label>New owner (customer id)</label><input id="tCustNew" inputmode="numeric" style="width:150px" /></div>
            <div class="fld"><label>New coil # (optional)</label><input id="tCoilNew" inputmode="numeric" style="width:130px" /></div>
            <div class="fld"><label>Performed by</label><input id="tPerf" style="width:130px" /></div>
            <div class="fld" style="flex:1;min-width:180px"><label>Authorization note</label><input id="tAuth" /></div>
            <div class="fld" style="flex:1;min-width:180px"><label>Notes</label><textarea id="tNotes"></textarea></div>
          </div>
          <div id="pickedOwner" class="mono" style="font-size:11px;color:var(--ink-3);margin:6px 0"></div>
          <div class="frow" style="align-items:center"><button class="btn sm" id="btnTransfer" type="button">Record transfer</button><span id="ok" class="ok-note"></span></div>
        </div>
      </div></div>
    </div>
  </div>`;
}
async function loadLedger() {
    setErr('');
    try {
        const cust = v('#fLedgerCust');
        const list = await client().getCoilOwnershipTransfers(cust ? Number(cust) : undefined);
        $('#tLedger').innerHTML = (list ?? []).length ? (list ?? []).map((t) => `
      <tr class="click" data-cert="${t.certificateNum}">
        <td class="mono">${esc(t.certificateNum)}</td>
        <td class="mono">${esc(t.coilAbcNumOrig)}${t.coilAbcNumNew ? ' → ' + esc(t.coilAbcNumNew) : ''}</td>
        <td>${esc(t.customerShortNameOrig)} → ${esc(t.customerShortNameNew)}</td>
        <td>${esc(t.coilAlloy2)} ${esc(t.coilTemper)}</td><td class="num">${esc(num(t.netWtBalance))}</td>
        <td class="mono">${esc(dShow(t.transferDatetime))}</td><td>${esc(t.transferPerformedBy)}</td></tr>`).join('')
            : '<tr><td colspan="7" class="muted">No transfers.</td></tr>';
        document.querySelectorAll('#tLedger tr.click').forEach((tr) => tr.addEventListener('click', () => void openCertificate(Number(tr.dataset.cert))));
    }
    catch (e) {
        setErr(`Ledger failed: ${e.message}`);
    }
}
async function openCertificate(certificateNum) {
    setErr('');
    setBusy(true);
    try {
        const c = await client().getCoilOwnershipTransferCertificate(certificateNum);
        $('#certTitle').textContent = `Certificate #${certificateNum}`;
        $('#cert').classList.remove('disabled');
        const addr = (full, city, st, zip) => `${esc(full)}<div class="muted" style="font-size:11.5px">${esc(city)}, ${esc(st)} ${esc(zip)}</div>`;
        $('#certBody').innerHTML = `
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-bottom:14px">
        <div><div class="eyebrow">From (original owner)</div>${addr(c.customerFullNameOrig, c.customerCityOrig, c.customerStateOrig, c.customerZipOrig)}</div>
        <div><div class="eyebrow">To (new owner)</div>${addr(c.customerFullNameNew, c.customerCityNew, c.customerStateNew, c.customerZipNew)}</div>
      </div>
      <table class="tbl"><tbody>
        <tr><th style="text-transform:none">Coil (orig)</th><td>${esc(c.coilAbcNumOrig)} (org ${esc(c.coilOrgNum)})</td></tr>
        <tr><th style="text-transform:none">Coil (new #)</th><td>${esc(c.coilAbcNumNew) || '—'}</td></tr>
        <tr><th style="text-transform:none">Metal</th><td>${esc(c.coilAlloy2)} ${esc(c.coilTemper)} · ${esc(num(c.coilGauge))} × ${esc(num(c.coilWidth))}</td></tr>
        <tr><th style="text-transform:none">Lot</th><td>${esc(c.lotNum)}</td></tr>
        <tr><th style="text-transform:none">Net wt / balance</th><td>${esc(num(c.netWt))} / ${esc(num(c.netWtBalance))}</td></tr>
        <tr><th style="text-transform:none">Transferred</th><td>${esc(dShow(c.transferDatetime))} by ${esc(c.transferPerformedBy)}</td></tr>
        <tr><th style="text-transform:none">Authorization</th><td>${esc(c.authorizationNote)}</td></tr>
        <tr><th style="text-transform:none">Notes</th><td>${esc(c.notes)}</td></tr>
      </tbody></table>`;
    }
    catch (e) {
        setErr(`Certificate failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function searchCoils() {
    setErr('');
    try {
        const cust = v('#fCoilCust');
        const list = await client().getTransferableCoils(cust ? Number(cust) : undefined, v('#fCoilSearch') || undefined);
        $('#tCoils').innerHTML = (list ?? []).length ? (list ?? []).map((c) => `
      <tr class="click" data-coil="${c.coilAbcNum}" data-owner="${c.customerId ?? ''}">
        <td class="mono">${esc(c.coilAbcNum)}</td><td>${esc(c.customerShortName)}</td><td class="mono">${esc(c.coilOrgNum)}</td>
        <td class="mono">${esc(c.lotNum)}</td><td>${esc(c.coilAlloy2)} ${esc(c.coilTemper)}</td><td class="num">${esc(num(c.netWtBalance))}</td></tr>`).join('')
            : '<tr><td colspan="6" class="muted">No coils.</td></tr>';
        document.querySelectorAll('#tCoils tr.click').forEach((tr) => tr.addEventListener('click', () => {
            setV('#tCoilOrig', tr.dataset.coil);
            $('#pickedOwner').textContent = tr.dataset.owner ? `current owner: customer ${tr.dataset.owner}` : '';
        }));
    }
    catch (e) {
        setErr(`Coil search failed: ${e.message}`);
    }
}
async function submitTransfer() {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const created = await client().createCoilOwnershipTransfer(new CoilOwnershipTransferWrite({
            coilAbcNumOrig: v('#tCoilOrig') ? Number(v('#tCoilOrig')) : undefined,
            customerIdNew: v('#tCustNew') ? Number(v('#tCustNew')) : undefined,
            coilAbcNumNew: v('#tCoilNew') ? Number(v('#tCoilNew')) : undefined,
            transferPerformedBy: v('#tPerf') || undefined,
            authorizationNote: v('#tAuth') || undefined,
            notes: v('#tNotes') || undefined,
        }));
        setOk(`✓ Transfer recorded — certificate #${created.certificateNum}.`);
        ['#tCoilOrig', '#tCustNew', '#tCoilNew', '#tPerf', '#tAuth', '#tNotes'].forEach((i) => setV(i, ''));
        $('#pickedOwner').textContent = '';
        await loadLedger();
        if (created.certificateNum != null) {
            showTab('ledger');
            await openCertificate(created.certificateNum);
        }
    }
    catch (e) {
        setErr(`Transfer failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function showTab(name) {
    ['ledger', 'new'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
}
(async () => {
    const main = await initShell({ active: 'coil-ownership' });
    main.innerHTML = scaffold();
    ['ledger', 'new'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#ledgerForm').addEventListener('submit', (e) => { e.preventDefault(); void loadLedger(); });
    $('#coilForm').addEventListener('submit', (e) => { e.preventDefault(); void searchCoils(); });
    $('#btnTransfer').addEventListener('click', () => void submitTransfer());
    showTab('ledger');
    await loadLedger();
})();
