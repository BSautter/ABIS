// ABIS Coil Ownership Transfer — greenfield (Path C) module for the legacy
// w_coil_ownership_transfer screens (silverdome4). Toll processing: a coil owned by one
// customer has its ownership moved to another, recorded as a numbered certificate. This
// is a typed SPA on the Phase-2 API: the transfer ledger, a coil picker + new-transfer
// form (which issues a certificate and re-points the coil's owner), and a printable
// certificate view. Driven entirely by the NSwag-generated client.
//
// Compiled by `tsc` to wwwroot/ui/app/coil-ownership.js; served at /ui/coil-ownership.html.
import { AbisClient, CoilOwnershipTransferWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, val) => { $(id).value = val == null ? '' : String(val); };
const num = (n) => (n == null ? '' : n.toLocaleString());
const dShow = (d) => (d == null ? '' : d.toLocaleString());
async function loadLedger() {
    setErr('');
    try {
        const cust = v('#fLedgerCust');
        const list = await client().getCoilOwnershipTransfers(cust ? Number(cust) : undefined);
        $('#tLedger').innerHTML = (list ?? []).map((t) => `
      <tr class="click" data-cert="${t.certificateNum}">
        <td>${esc(t.certificateNum)}</td>
        <td>${esc(t.coilAbcNumOrig)}${t.coilAbcNumNew ? ' → ' + esc(t.coilAbcNumNew) : ''}</td>
        <td>${esc(t.customerShortNameOrig)} → ${esc(t.customerShortNameNew)}</td>
        <td>${esc(t.coilAlloy2)} ${esc(t.coilTemper)}</td>
        <td>${esc(num(t.netWtBalance))}</td>
        <td>${esc(dShow(t.transferDatetime))}</td>
        <td>${esc(t.transferPerformedBy)}</td></tr>`).join('')
            || '<tr><td colspan="7" class="muted">No transfers.</td></tr>';
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
        const addr = (full, city, st, zip) => `${esc(full)}<br /><span class="muted">${esc(city)}, ${esc(st)} ${esc(zip)}</span>`;
        $('#certBody').innerHTML = `
      <div class="certgrid">
        <div><h4>From (original owner)</h4>${addr(c.customerFullNameOrig, c.customerCityOrig, c.customerStateOrig, c.customerZipOrig)}</div>
        <div><h4>To (new owner)</h4>${addr(c.customerFullNameNew, c.customerCityNew, c.customerStateNew, c.customerZipNew)}</div>
      </div>
      <table class="hdr"><tbody>
        <tr><th>Coil (orig)</th><td>${esc(c.coilAbcNumOrig)} (org ${esc(c.coilOrgNum)})</td></tr>
        <tr><th>Coil (new #)</th><td>${esc(c.coilAbcNumNew) || '—'}</td></tr>
        <tr><th>Metal</th><td>${esc(c.coilAlloy2)} ${esc(c.coilTemper)} · ${esc(num(c.coilGauge))} × ${esc(num(c.coilWidth))}</td></tr>
        <tr><th>Lot</th><td>${esc(c.lotNum)}</td></tr>
        <tr><th>Net wt / balance</th><td>${esc(num(c.netWt))} / ${esc(num(c.netWtBalance))}</td></tr>
        <tr><th>Transferred</th><td>${esc(dShow(c.transferDatetime))} by ${esc(c.transferPerformedBy)}</td></tr>
        <tr><th>Authorization</th><td>${esc(c.authorizationNote)}</td></tr>
        <tr><th>Notes</th><td>${esc(c.notes)}</td></tr>
      </tbody></table>`;
    }
    catch (e) {
        setErr(`Certificate failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// The coil picker — search transferable coils and click one into the new-transfer form.
async function searchCoils() {
    setErr('');
    try {
        const cust = v('#fCoilCust');
        const list = await client().getTransferableCoils(cust ? Number(cust) : undefined, v('#fCoilSearch') || undefined);
        $('#tCoils').innerHTML = (list ?? []).map((c) => `
      <tr class="click" data-coil="${c.coilAbcNum}" data-owner="${c.customerId ?? ''}">
        <td>${esc(c.coilAbcNum)}</td><td>${esc(c.customerShortName)}</td><td>${esc(c.coilOrgNum)}</td>
        <td>${esc(c.lotNum)}</td><td>${esc(c.coilAlloy2)} ${esc(c.coilTemper)}</td>
        <td>${esc(num(c.netWtBalance))}</td></tr>`).join('')
            || '<tr><td colspan="6" class="muted">No coils.</td></tr>';
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
        if (created.certificateNum != null)
            await openCertificate(created.certificateNum);
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
async function init() {
    ['ledger', 'new'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#ledgerForm').addEventListener('submit', (e) => { e.preventDefault(); void loadLedger(); });
    $('#coilForm').addEventListener('submit', (e) => { e.preventDefault(); void searchCoils(); });
    $('#btnTransfer').addEventListener('click', submitTransfer);
    showTab('ledger');
    await initShell({ active: 'coil-ownership', adopt: true });
    await loadLedger();
}
void init();
