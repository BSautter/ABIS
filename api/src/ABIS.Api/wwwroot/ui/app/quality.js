// ABIS Quality / Recovery — greenfield (Path C) module for the legacy w_recovery
// customer-defect setup. A typed SPA on the Phase-2 API: the scrap/defect catalog,
// the product-type lookup, the recovery-report customers, and — for a selected
// customer — the scrap/defect types they track. Through the NSwag-generated client.
//
// Compiled by `tsc` to wwwroot/ui/app/quality.js; served at /ui/quality.html.
import { AbisClient } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
async function loadScrapTypes() {
    try {
        const list = await client().getScrapTypes();
        $('#tScrap').innerHTML = (list ?? []).map((s) => `<tr>
      <td>${esc(s.scrapTypeId)}</td><td>${esc(s.scrapCode)}</td><td>${esc(s.scrapDefect)}</td></tr>`).join('')
            || '<tr><td colspan="3" class="muted">No scrap types.</td></tr>';
    }
    catch (e) {
        setErr(`Scrap types failed: ${e.message}`);
    }
}
async function loadProductTypes() {
    try {
        const list = await client().getProductTypes();
        $('#tProd').innerHTML = (list ?? []).map((p) => `<tr>
      <td>${esc(p.productTypeId)}</td><td>${esc(p.productTypeName)}</td></tr>`).join('')
            || '<tr><td colspan="2" class="muted">No product types.</td></tr>';
    }
    catch (e) {
        setErr(`Product types failed: ${e.message}`);
    }
}
async function loadRecoveryCustomers() {
    try {
        const list = await client().getRecoveryCustomers();
        $('#tCust').innerHTML = (list ?? []).map((c) => `
      <tr class="click" data-id="${c.customerId}">
        <td>${esc(c.customerId)}</td><td>${esc(c.customerName)}</td>
        <td>${esc(c.allProducts)}</td><td>${esc(c.autoOnly)}</td><td>${esc(c.commOnly)}</td></tr>`).join('')
            || '<tr><td colspan="5" class="muted">No recovery customers.</td></tr>';
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
        $('#tDefects').innerHTML = (list ?? []).map((d) => `<tr>
      <td>${esc(d.scrapCode)}</td><td>${esc(d.scrapDefect)}</td><td>${esc(d.abcOrMill)}</td>
      <td>${esc(d.autoparts)}</td><td>${esc(d.nonAutoparts)}</td></tr>`).join('')
            || '<tr><td colspan="5" class="muted">No tracked defects for this customer.</td></tr>';
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
async function init() {
    ['catalog', 'customers', 'defects', 'products'].forEach((t) => $(`#tab-${t}`).addEventListener('click', () => showTab(t)));
    $('#defForm').addEventListener('submit', (e) => { e.preventDefault(); void loadDefects(); });
    showTab('catalog');
    await initShell({ active: 'quality', adopt: true });
    await Promise.all([loadScrapTypes(), loadProductTypes(), loadRecoveryCustomers()]);
}
void init();
