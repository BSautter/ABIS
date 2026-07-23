// ABIS Quality / Recovery — the legacy w_recovery customer-defect setup, restyled to the design
// system in the shared shell (#4 polish). The scrap/defect catalog, the product-type lookup, the
// recovery-report customers, and — for a selected customer — the scrap/defect types they track.
// Through the NSwag-generated client.
//
// Compiled by tsc to wwwroot/ui/app/quality.js; served at /ui/quality.html.
import { AbisClient, RecoveryCustomerWrite, CustomerScrapTypeWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const flag = (s) => (s === 'Y' || s === 1 || s === '1' ? '<span class="chip ok">Y</span>' : `<span class="chip mut">${esc(s ?? '—')}</span>`);
function scaffold() {
    const tab = (id, label) => `<button id="tab-${id}" type="button">${label}</button>`;
    return `
  <div class="page">
    <div class="page-head"><div><div class="eyebrow">Quality · Recovery</div><h1>Quality / Recovery</h1></div></div>
    <div class="tabs">${tab('catalog', 'Defect catalog')}${tab('customers', 'Recovery customers')}${tab('defects', 'Customer defects')}${tab('products', 'Product types')}</div>

    <div id="pane-catalog" class="card">
      <header><h2>Scrap / defect catalog</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:360px">
        <thead><tr><th>Id</th><th>Code</th><th>Defect</th></tr></thead>
        <tbody id="tScrap"><tr><td colspan="3" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div id="pane-customers" class="card" style="display:none">
      <header><h2>Recovery-report customers</h2><span class="sub">click a row for their tracked defects</span></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
        <thead><tr><th>Customer</th><th>Name</th><th>All products</th><th>Auto only</th><th>Comm only</th><th></th></tr></thead>
        <tbody id="tCust"><tr><td colspan="6" class="muted">Loading…</td></tr></tbody>
      </table></div>
      <div class="body" style="border-top:1px solid var(--line);margin-top:8px">
        <div class="frow" style="align-items:flex-end">
          <div class="fld"><label>Customer id</label><input id="cId" inputmode="numeric" style="width:110px" /></div>
          <div class="fld"><label>Name</label><input id="cName" style="width:180px" /></div>
          <div class="fld"><label>All products</label><select id="cAll"><option>N</option><option>Y</option></select></div>
          <div class="fld"><label>Auto only</label><select id="cAuto"><option>N</option><option>Y</option></select></div>
          <div class="fld"><label>Comm only</label><select id="cComm"><option>N</option><option>Y</option></select></div>
          <button class="btn sm" id="btnSaveCust" type="button">Save customer</button>
          <span id="custOk" class="ok-note"></span>
        </div>
        <p class="muted" style="margin:6px 0 0">Enter an existing id to edit it; a new id adds it.</p>
      </div>
    </div>

    <div id="pane-defects" class="card" style="display:none">
      <header><h2>Defects tracked by a customer</h2></header>
      <div class="body"><form id="defForm" class="frow"><div class="fld"><label>Customer id</label><input id="fCust" inputmode="numeric" placeholder="id" style="width:120px" /></div><button class="btn sm" type="submit">Load</button></form></div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:560px">
        <thead><tr><th>Code</th><th>Defect</th><th>ABC / Mill</th><th>Autoparts</th><th>Non-autoparts</th><th></th></tr></thead>
        <tbody id="tDefects"><tr><td colspan="6" class="muted">Enter or pick a customer.</td></tr></tbody>
      </table></div>
      <div class="body" id="defAddWrap" style="border-top:1px solid var(--line);margin-top:8px;display:none">
        <div class="frow" style="align-items:flex-end">
          <div class="fld"><label>Scrap type</label><select id="dScrapType" style="min-width:200px"></select></div>
          <div class="fld"><label>ABC / Mill</label><select id="dAbcMill"><option>ABC</option><option>MILL</option></select></div>
          <div class="fld"><label>Autoparts</label><select id="dAuto"><option>N</option><option>Y</option></select></div>
          <div class="fld"><label>Non-autoparts</label><select id="dNon"><option>N</option><option>Y</option></select></div>
          <button class="btn sm" id="btnAddDefect" type="button">Add / update defect</button>
          <span id="defOk" class="ok-note"></span>
        </div>
      </div>
    </div>

    <div id="pane-products" class="card" style="display:none">
      <header><h2>Product types</h2></header>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:280px">
        <thead><tr><th>Id</th><th>Product type</th></tr></thead>
        <tbody id="tProd"><tr><td colspan="2" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div id="err" class="err" style="margin-top:8px"></div>
  </div>`;
}
let scrapTypes = [];
async function loadScrapTypes() {
    try {
        scrapTypes = (await client().getScrapTypes()) ?? [];
        $('#tScrap').innerHTML = scrapTypes.length ? scrapTypes.map((s) => `<tr>
      <td class="mono">${esc(s.scrapTypeId)}</td><td class="mono">${esc(s.scrapCode)}</td><td>${esc(s.scrapDefect)}</td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No scrap types.</td></tr>';
        // Populate the add-defect picker from the same catalog.
        $('#dScrapType').innerHTML = scrapTypes.map((s) => `<option value="${esc(s.scrapTypeId)}">${esc(s.scrapCode)} — ${esc(s.scrapDefect)}</option>`).join('');
    }
    catch (e) {
        setErr(`Scrap types failed: ${e.message}`);
    }
}
async function loadProductTypes() {
    try {
        const list = await client().getProductTypes();
        $('#tProd').innerHTML = (list ?? []).length ? (list ?? []).map((p) => `<tr>
      <td class="mono">${esc(p.productTypeId)}</td><td>${esc(p.productTypeName)}</td></tr>`).join('')
            : '<tr><td colspan="2" class="muted">No product types.</td></tr>';
    }
    catch (e) {
        setErr(`Product types failed: ${e.message}`);
    }
}
async function loadRecoveryCustomers() {
    try {
        const list = await client().getRecoveryCustomers();
        $('#tCust').innerHTML = (list ?? []).length ? (list ?? []).map((c) => `
      <tr class="click" data-id="${c.customerId}">
        <td class="mono">${esc(c.customerId)}</td><td>${esc(c.customerName)}</td>
        <td>${flag(c.allProducts)}</td><td>${flag(c.autoOnly)}</td><td>${flag(c.commOnly)}</td>
        <td><button class="btn xs ghost" type="button" data-del="${esc(c.customerId)}">Delete</button></td></tr>`).join('')
            : '<tr><td colspan="6" class="muted">No recovery customers.</td></tr>';
        document.querySelectorAll('#tCust tr.click').forEach((tr) => tr.addEventListener('click', (e) => {
            if (e.target.closest('[data-del]'))
                return; // let the Delete button handle its own click
            $('#fCust').value = tr.dataset.id ?? '';
            void loadDefects();
            showTab('defects');
        }));
        document.querySelectorAll('#tCust [data-del]').forEach((b) => b.addEventListener('click', () => void deleteCustomer(Number(b.dataset.del))));
    }
    catch (e) {
        setErr(`Recovery customers failed: ${e.message}`);
    }
}
async function loadDefects() {
    setErr('');
    setBusy(true);
    const id = $('#fCust').value.trim();
    $('#defAddWrap').style.display = id ? '' : 'none';
    if (!id) {
        $('#tDefects').innerHTML = '<tr><td colspan="6" class="muted">Enter or pick a customer.</td></tr>';
        setBusy(false);
        return;
    }
    try {
        const list = await client().getCustomerDefects(Number(id));
        $('#tDefects').innerHTML = (list ?? []).length ? (list ?? []).map((d) => `<tr>
      <td class="mono">${esc(d.scrapCode)}</td><td>${esc(d.scrapDefect)}</td><td>${esc(d.abcOrMill)}</td>
      <td>${flag(d.autoparts)}</td><td>${flag(d.nonAutoparts)}</td>
      <td><button class="btn xs ghost" type="button" data-del-defect="${esc(d.scrapTypeId)}">Remove</button></td></tr>`).join('')
            : '<tr><td colspan="6" class="muted">No tracked defects for this customer.</td></tr>';
        document.querySelectorAll('#tDefects [data-del-defect]').forEach((b) => b.addEventListener('click', () => void removeDefect(Number(id), Number(b.dataset.delDefect))));
    }
    catch (e) {
        setErr(`Customer defects failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function saveCustomer() {
    setErr('');
    $('#custOk').textContent = '';
    const id = $('#cId').value.trim();
    if (!id) {
        setErr('Customer id is required.');
        return;
    }
    setBusy(true);
    try {
        await client().upsertRecoveryCustomer(Number(id), new RecoveryCustomerWrite({
            customerName: $('#cName').value.trim() || undefined,
            allProducts: $('#cAll').value,
            autoOnly: $('#cAuto').value,
            commOnly: $('#cComm').value,
        }));
        $('#custOk').textContent = `✓ Saved customer ${id}.`;
        await loadRecoveryCustomers();
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function deleteCustomer(id) {
    if (!confirm(`Remove customer ${id} from recovery reporting? (Their tracked defects are removed too.)`))
        return;
    setErr('');
    setBusy(true);
    try {
        await client().deleteRecoveryCustomer(id);
        await loadRecoveryCustomers();
    }
    catch (e) {
        setErr(`Delete failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function addDefect() {
    setErr('');
    $('#defOk').textContent = '';
    const id = $('#fCust').value.trim();
    const scrapTypeId = $('#dScrapType').value;
    if (!id || !scrapTypeId) {
        setErr('Pick a customer and a scrap type.');
        return;
    }
    setBusy(true);
    try {
        await client().upsertCustomerScrapType(Number(id), Number(scrapTypeId), new CustomerScrapTypeWrite({
            abcOrMill: $('#dAbcMill').value,
            autoparts: $('#dAuto').value,
            nonAutoparts: $('#dNon').value,
        }));
        $('#defOk').textContent = '✓ Saved.';
        await loadDefects();
    }
    catch (e) {
        setErr(`Add defect failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function removeDefect(customerId, scrapTypeId) {
    if (!confirm(`Stop tracking scrap type ${scrapTypeId} for customer ${customerId}?`))
        return;
    setErr('');
    setBusy(true);
    try {
        await client().deleteCustomerScrapType(customerId, scrapTypeId);
        await loadDefects();
    }
    catch (e) {
        setErr(`Remove failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function showTab(name) {
    ['catalog', 'customers', 'defects', 'products'].forEach((t) => {
        $(`#pane-${t}`).style.display = t === name ? '' : 'none';
        $(`#tab-${t}`).classList.toggle('active', t === name);
    });
}
(async () => {
    const main = await initShell({ active: 'quality' });
    main.innerHTML = scaffold();
    ['catalog', 'customers', 'defects', 'products'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#defForm').addEventListener('submit', (e) => { e.preventDefault(); void loadDefects(); });
    $('#btnSaveCust').addEventListener('click', () => void saveCustomer());
    $('#btnAddDefect').addEventListener('click', () => void addDefect());
    showTab('catalog');
    await Promise.all([loadScrapTypes(), loadProductTypes(), loadRecoveryCustomers()]);
})();
