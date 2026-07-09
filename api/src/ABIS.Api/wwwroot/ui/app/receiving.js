// ABIS Receiving — greenfield (Path C) module replacing the legacy receiving /
// coil_receiving window. A typed SPA on the Phase-2 API: filterable inbound
// receiving-BOL search and a load → edit → save form (create or replace). The
// replace path re-sends all writable fields (bol + customerId are required) so a
// partial edit can't blank them. Through the NSwag-generated, compiler-checked client.
//
// Compiled by `tsc` to wwwroot/ui/app/receiving.js; served at /ui/receiving.html.
import { AbisClient, ReceivingBolWrite, ReceivingBolCoilWrite } from './generated/abis-client.js';
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
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const dateOnly = (d) => (d == null ? '' : d.toISOString().slice(0, 10));
let editingId = null;
const custNames = new Map();
const custLabel = (id) => id == null ? '' : custNames.has(id) ? `${id} · ${custNames.get(id)}` : String(id);
// Customer datalist (value = id, label = name) + an id→name map for the list view.
async function loadCustomers() {
    try {
        const page = await client().listCustomers(1, 500, undefined, undefined, undefined);
        custNames.clear();
        $('#custList').innerHTML = (page.items ?? []).map((c) => {
            if (c.customerId != null)
                custNames.set(c.customerId, c.customerName ?? '');
            return `<option value="${esc(c.customerId)}">${esc(c.customerName)}</option>`;
        }).join('');
    }
    catch { /* dropdown is best-effort */ }
}
async function search() {
    setErr('');
    setBusy(true);
    const customerId = v('#fCustomer') ? Number(v('#fCustomer')) : undefined;
    const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
    try {
        const page = await client().listReceivingBols(1, 50, customerId, status, undefined, undefined);
        const rows = (page.items ?? []).map((b) => `
      <tr class="click" data-id="${b.receivingBolId}">
        <td>${esc(b.receivingBolId)}</td>
        <td>${esc(b.bol)}</td>
        <td>${esc(custLabel(b.customerId))}</td>
        <td>${esc(dateOnly(b.receivedDate))}</td>
        <td>${esc(b.status)}</td>
        <td>${esc(b.createdBy)}</td>
      </tr>`).join('');
        $('#bols').innerHTML = rows || '<tr><td colspan="6" class="muted">No matching BOLs.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#bols tr.click').forEach((tr) => tr.addEventListener('click', () => loadBol(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadBol(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const b = await client().getReceivingBol(id);
        editingId = id;
        $('#formTitle').textContent = `Edit receiving BOL #${id}`;
        setV('#rBol', b.bol);
        setV('#rCustomer', b.customerId);
        setV('#rCreatedBy', b.createdBy);
        $('#rReceived').value = dateOnly(b.receivedDate);
        setV('#rStatus', b.status);
        $('#coilsSection').classList.remove('disabled');
        await loadCoils();
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadCoils() {
    if (editingId == null)
        return;
    const coils = await client().getReceivingBolCoils(editingId);
    $('#tCoils').innerHTML = (coils ?? []).map((c) => `<tr>
    <td>${esc(c.coilId)}</td><td>${esc(c.coilOrgNum)}</td><td>${esc(c.alloy)}</td><td>${esc(c.temper)}</td>
    <td>${esc(c.netWeight)}</td><td>${esc(c.grossWeight)}</td><td>${esc(c.coilGauge)}</td><td>${esc(c.coilWidth)}</td>
    <td>${esc(c.status)}</td><td><button class="sec rmCoil" data-c="${c.coilId}" type="button">remove</button></td></tr>`).join('')
        || '<tr><td colspan="10" class="muted">No coils on this BOL.</td></tr>';
    document.querySelectorAll('#tCoils .rmCoil').forEach((b) => b.addEventListener('click', () => void deleteCoil(Number(b.dataset.c))));
}
async function addCoil() {
    if (editingId == null) {
        setErr('Load or save a BOL first.');
        return;
    }
    if (!v('#cOrg')) {
        setErr('Org num is required for a coil line.');
        return;
    }
    setBusy(true);
    try {
        await client().addReceivingBolCoil(editingId, new ReceivingBolCoilWrite({
            coilOrgNum: v('#cOrg'), alloy: v('#cAlloy') || undefined, temper: v('#cTemper') || undefined,
            netWeight: v('#cNet') ? Number(v('#cNet')) : undefined, grossWeight: v('#cGross') ? Number(v('#cGross')) : undefined,
            coilGauge: v('#cGauge') ? Number(v('#cGauge')) : undefined, coilWidth: v('#cWidth') ? Number(v('#cWidth')) : undefined,
            lot: v('#cLot') || undefined, status: 2,
        }));
        $('#coilOk').textContent = '✓ Coil added.';
        ['#cOrg', '#cAlloy', '#cTemper', '#cNet', '#cGross', '#cGauge', '#cWidth', '#cLot'].forEach((i) => setV(i, ''));
        await loadCoils();
    }
    catch (e) {
        setErr(`Add coil failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function deleteCoil(coilId) {
    if (editingId == null)
        return;
    setBusy(true);
    try {
        await client().deleteReceivingBolCoil(editingId, coilId);
        await loadCoils();
        $('#coilOk').textContent = '✓ Coil removed.';
    }
    catch (e) {
        setErr(`Remove coil failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// Mint COIL inventory for this BOL's lines (the legacy w_coil_receiving save).
async function mintCoils() {
    if (editingId == null) {
        setErr('Load or save a BOL first.');
        return;
    }
    setErr('');
    setBusy(true);
    try {
        const r = await client().mintBolCoils(editingId);
        $('#coilOk').textContent = `✓ Minted ${r.minted} coil(s) into inventory.`;
        await loadCoils();
    }
    catch (e) {
        setErr(`Mint failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function generate861() {
    if (editingId == null) {
        setErr('Load or save a BOL first.');
        return;
    }
    setErr('');
    setBusy(true);
    try {
        const r = await client().generateReceiving861(editingId);
        $('#coilOk').textContent = `861: ${r.status} — ${r.note ?? ''}`;
    }
    catch (e) {
        setErr(`861 failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newBol() {
    editingId = null;
    $('#formTitle').textContent = 'New receiving BOL';
    ['#rBol', '#rCustomer', '#rCreatedBy', '#rStatus'].forEach((id) => setV(id, ''));
    $('#rReceived').value = '';
    $('#coilsSection').classList.add('disabled');
    $('#tCoils').innerHTML = '';
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new ReceivingBolWrite({
        bol: v('#rBol') || undefined,
        customerId: v('#rCustomer') ? Number(v('#rCustomer')) : undefined,
        createdBy: v('#rCreatedBy') || undefined,
        receivedDate: v('#rReceived') ? new Date(v('#rReceived')) : undefined,
        status: v('#rStatus') ? Number(v('#rStatus')) : undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createReceivingBol(body);
            setOk(`✓ Created BOL #${created.receivingBolId}. Add its coils below.`);
            editingId = created.receivingBolId ?? null; // switch to edit mode so coils can be added
            $('#formTitle').textContent = `Edit receiving BOL #${editingId}`;
            $('#coilsSection').classList.remove('disabled');
            await loadCoils();
        }
        else {
            await client().updateReceivingBol(editingId, body);
            setOk(`✓ Saved BOL #${editingId}.`);
        }
        await search();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function init() {
    await loadCustomers();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newBol);
    $('#btnSave').addEventListener('click', save);
    $('#btnAddCoil').addEventListener('click', addCoil);
    $('#btnMint').addEventListener('click', mintCoils);
    $('#btnGen861').addEventListener('click', generate861);
    newBol();
    await search();
}
void initShell({ active: 'receiving', adopt: true }).then(init);
