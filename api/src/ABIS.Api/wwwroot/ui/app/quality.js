// ABIS Quality / Recovery — the legacy w_recovery customer-defect setup, restyled to the design
// system in the shared shell (#4 polish). The scrap/defect catalog, the product-type lookup, the
// recovery-report customers, and — for a selected customer — the scrap/defect types they track.
// Through the NSwag-generated client.
//
// Compiled by tsc to wwwroot/ui/app/quality.js; served at /ui/quality.html.
import { AbisClient } from './generated/abis-client.js';
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
      <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
        <thead><tr><th>Customer</th><th>Name</th><th>All products</th><th>Auto only</th><th>Comm only</th></tr></thead>
        <tbody id="tCust"><tr><td colspan="5" class="muted">Loading…</td></tr></tbody>
      </table></div>
    </div>

    <div id="pane-defects" class="card" style="display:none">
      <header><h2>Defects tracked by a customer</h2></header>
      <div class="body"><form id="defForm" class="frow"><div class="fld"><label>Customer id</label><input id="fCust" inputmode="numeric" placeholder="id" style="width:120px" /></div><button class="btn sm" type="submit">Load</button></form></div>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:520px">
        <thead><tr><th>Code</th><th>Defect</th><th>ABC / Mill</th><th>Autoparts</th><th>Non-autoparts</th></tr></thead>
        <tbody id="tDefects"><tr><td colspan="5" class="muted">Enter or pick a customer.</td></tr></tbody>
      </table></div>
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
async function loadScrapTypes() {
    try {
        const list = await client().getScrapTypes();
        $('#tScrap').innerHTML = (list ?? []).length ? (list ?? []).map((s) => `<tr>
      <td class="mono">${esc(s.scrapTypeId)}</td><td class="mono">${esc(s.scrapCode)}</td><td>${esc(s.scrapDefect)}</td></tr>`).join('')
            : '<tr><td colspan="3" class="muted">No scrap types.</td></tr>';
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
        <td>${flag(c.allProducts)}</td><td>${flag(c.autoOnly)}</td><td>${flag(c.commOnly)}</td></tr>`).join('')
            : '<tr><td colspan="5" class="muted">No recovery customers.</td></tr>';
        document.querySelectorAll('#tCust tr.click').forEach((tr) => tr.addEventListener('click', () => { $('#fCust').value = tr.dataset.id ?? ''; void loadDefects(); showTab('defects'); }));
    }
    catch (e) {
        setErr(`Recovery customers failed: ${e.message}`);
    }
}
async function loadDefects() {
    setErr('');
    setBusy(true);
    const id = $('#fCust').value.trim();
    if (!id) {
        $('#tDefects').innerHTML = '<tr><td colspan="5" class="muted">Enter or pick a customer.</td></tr>';
        setBusy(false);
        return;
    }
    try {
        const list = await client().getCustomerDefects(Number(id));
        $('#tDefects').innerHTML = (list ?? []).length ? (list ?? []).map((d) => `<tr>
      <td class="mono">${esc(d.scrapCode)}</td><td>${esc(d.scrapDefect)}</td><td>${esc(d.abcOrMill)}</td>
      <td>${flag(d.autoparts)}</td><td>${flag(d.nonAutoparts)}</td></tr>`).join('')
            : '<tr><td colspan="5" class="muted">No tracked defects for this customer.</td></tr>';
    }
    catch (e) {
        setErr(`Customer defects failed: ${e.message}`);
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
    showTab('catalog');
    await Promise.all([loadScrapTypes(), loadProductTypes(), loadRecoveryCustomers()]);
})();
