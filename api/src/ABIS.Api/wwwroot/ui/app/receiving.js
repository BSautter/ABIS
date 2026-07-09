// ABIS Receiving — the legacy receiving / coil_receiving window, restyled to the design system in
// the shared shell (#4 polish). Filterable inbound receiving-BOL search and a load → edit → save
// form (create or replace). The replace path re-sends all writable fields (bol + customerId are
// required) so a partial edit can't blank them. Then per-BOL coil lines, mint-to-inventory and the
// 861 generate action. Through the NSwag-generated, compiler-checked client.
//
// Compiled by tsc to wwwroot/ui/app/receiving.js; served at /ui/receiving.html.
import { AbisClient, ReceivingBolWrite, ReceivingBolCoilWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setOk = (m) => { $('#ok').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const v = (id) => $(id).value.trim();
const setV = (id, value) => { $(id).value = value == null ? '' : String(value); };
const dateOnly = (d) => (d == null ? '' : d.toISOString().slice(0, 10));
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let editingId = null;
const custNames = new Map();
const custLabel = (id) => id == null ? '' : custNames.has(id) ? `${id} · ${custNames.get(id)}` : String(id);
function scaffold() {
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Logistics · Receiving</div><h1>Receiving</h1></div><div class="shift-tag" id="count">—</div></div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Customer id</label><input id="fCustomer" list="custList" placeholder="id" style="width:120px" /></div>
        <div class="fld"><label>Status</label><input id="fStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="grid" style="margin-bottom:16px">
      <div class="stack"><div class="card">
        <header><h2>Inbound receiving BOLs</h2></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
          <thead><tr><th>BOL#</th><th>BOL</th><th>Customer</th><th>Received</th><th>Status</th><th>Created by</th></tr></thead>
          <tbody id="bols"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New receiving BOL</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld" style="flex:1;min-width:180px"><label>BOL number *</label><input id="rBol" maxlength="32" placeholder="required" /></div>
            <div class="fld"><label>Customer *</label><input id="rCustomer" list="custList" placeholder="customer id" style="width:130px" /></div>
          </div>
          <div class="frow" style="margin-top:8px">
            <div class="fld"><label>Received date</label><input id="rReceived" type="date" /></div>
            <div class="fld"><label>Created by</label><input id="rCreatedBy" maxlength="32" style="width:120px" /></div>
            <div class="fld"><label>Status</label><input id="rStatus" inputmode="numeric" placeholder="code" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnSave" type="button">Save</button>
            <button class="btn sm ghost" id="btnNew" type="button">New</button>
            <span id="ok" class="ok-note"></span>
          </div>
        </div>
      </div></div>
    </div>

    <div class="card" id="coilsSection">
      <header><h2>Coils on this BOL</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:720px">
        <thead><tr><th>Coil#</th><th>Org num</th><th>Alloy</th><th>Temper</th><th class="num">Net wt</th><th class="num">Gross wt</th><th class="num">Gauge</th><th class="num">Width</th><th>Status</th><th></th></tr></thead>
        <tbody id="tCoils"><tr><td colspan="10" class="muted">—</td></tr></tbody>
      </table></div>
      <div class="body">
        <div class="frow">
          <div class="fld"><label>Org num *</label><input id="cOrg" maxlength="32" style="width:120px" /></div>
          <div class="fld"><label>Alloy</label><input id="cAlloy" maxlength="40" style="width:80px" /></div>
          <div class="fld"><label>Temper</label><input id="cTemper" maxlength="8" style="width:70px" /></div>
          <div class="fld"><label>Net wt</label><input id="cNet" type="number" style="width:90px" /></div>
          <div class="fld"><label>Gross wt</label><input id="cGross" type="number" style="width:90px" /></div>
          <div class="fld"><label>Gauge</label><input id="cGauge" type="number" step="0.0001" style="width:90px" /></div>
          <div class="fld"><label>Width</label><input id="cWidth" type="number" step="0.0001" style="width:90px" /></div>
          <div class="fld"><label>Lot</label><input id="cLot" maxlength="40" style="width:90px" /></div>
          <button class="btn sm" id="btnAddCoil" type="button">Add coil</button>
        </div>
        <div class="frow" style="margin-top:10px;align-items:center">
          <button class="btn sm" id="btnMint" type="button">Mint coil inventory</button>
          <button class="btn sm ghost" id="btnGen861" type="button">Generate 861</button>
          <span class="muted" style="font-size:12px">Mint creates COIL inventory rows for lines not yet minted (status 2/new, 11/on-hold if damaged).</span>
        </div>
        <div id="coilOk" class="ok-note"></div>
      </div>
    </div>
  </div>
  <datalist id="custList"></datalist>`;
}
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
        const items = page.items ?? [];
        $('#bols').innerHTML = items.length ? items.map((b) => `
      <tr class="click" data-id="${b.receivingBolId}">
        <td class="mono">${esc(b.receivingBolId)}</td><td class="mono">${esc(b.bol)}</td><td>${esc(custLabel(b.customerId))}</td>
        <td class="mono">${esc(dateOnly(b.receivedDate))}</td><td>${chip(b.status)}</td><td>${esc(b.createdBy)}</td>
      </tr>`).join('') : '<tr><td colspan="6" class="muted">No matching BOLs.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} total`;
        document.querySelectorAll('#bols tr.click').forEach((tr) => tr.addEventListener('click', () => void loadBol(Number(tr.dataset.id))));
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
    $('#tCoils').innerHTML = (coils ?? []).length ? (coils ?? []).map((c) => `<tr>
    <td class="mono">${esc(c.coilId)}</td><td class="mono">${esc(c.coilOrgNum)}</td><td>${esc(c.alloy)}</td><td>${esc(c.temper)}</td>
    <td class="num">${esc(c.netWeight)}</td><td class="num">${esc(c.grossWeight)}</td><td class="num">${esc(c.coilGauge)}</td><td class="num">${esc(c.coilWidth)}</td>
    <td>${chip(c.status)}</td><td><button class="btn sm ghost rmCoil" data-c="${c.coilId}" type="button">remove</button></td></tr>`).join('')
        : '<tr><td colspan="10" class="muted">No coils on this BOL.</td></tr>';
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
    $('#tCoils').innerHTML = '<tr><td colspan="10" class="muted">Save a BOL to add coils.</td></tr>';
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
(async () => {
    const main = await initShell({ active: 'receiving' });
    main.innerHTML = scaffold();
    await loadCustomers();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newBol);
    $('#btnSave').addEventListener('click', () => void save());
    $('#btnAddCoil').addEventListener('click', () => void addCoil());
    $('#btnMint').addEventListener('click', () => void mintCoils());
    $('#btnGen861').addEventListener('click', () => void generate861());
    newBol();
    await search();
})();
