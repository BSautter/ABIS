// ABIS Order Entry — the first real greenfield (Path C) module UI, replacing the
// legacy PowerBuilder order_entry window. A typed single-page app on the Phase-2
// API: order search, the order-detail read model (header + customer + lines), and
// a transactional create-with-items save. Every call goes through the NSwag-
// generated, strongly-typed client, so the OpenAPI contract is compiler-checked.
//
// Compiled by `tsc` to wwwroot/ui/app/order-entry.js (no runtime build step);
// served at /ui/order-entry.html. See docs/PHASE3_PILOT_LOG.md.
import { AbisClient, OrderCreateWithItems, CustomerOrderWrite, OrderItemWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client() {
    return new AbisClient('', { fetch: authFetch });
}
const esc = (s) => String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));
const numf = (v) => (v == null ? '' : v.toLocaleString());
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
let alloys = [];
// ---- Search + list ---------------------------------------------------------
async function search() {
    setErr('');
    setBusy(true);
    const custRaw = $('#fCustomer').value.trim();
    const po = $('#fPo').value.trim() || undefined;
    const customerId = custRaw ? Number(custRaw) : undefined;
    try {
        const page = await client().listOrders(1, 50, customerId, po, undefined, undefined);
        const rows = (page.items ?? []).map((o) => `
      <tr class="click" data-id="${o.orderAbcNum}">
        <td>${esc(o.orderAbcNum)}</td>
        <td>${esc(o.origCustomerId)}</td>
        <td>${esc(o.origCustomerPo)}</td>
        <td>${esc(o.enduserPo)}</td>
      </tr>`).join('');
        $('#orders').innerHTML = rows || '<tr><td colspan="4" class="muted">No matching orders.</td></tr>';
        $('#orderCount').textContent = `${numf(page.totalCount)} total`;
        document.querySelectorAll('#orders tr.click').forEach((tr) => tr.addEventListener('click', () => loadOrder(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// ---- Detail (header + customer + line items) -------------------------------
async function loadOrder(id) {
    setErr('');
    setBusy(true);
    try {
        const d = await client().getOrderDetail(id);
        const items = (d.items ?? []).map((it) => `
      <tr>
        <td>${esc(it.orderItemNum)}</td>
        <td>${esc(it.enduserPartNum)}</td>
        <td>${esc(it.alloy2)}</td>
        <td>${esc(it.sheetType)}</td>
        <td class="num">${numf(it.gauge)}</td>
        <td class="num">${numf(it.piecesSkid)}</td>
      </tr>`).join('');
        $('#detail').innerHTML = `
      <div class="kv">
        <span><b>Order</b> ${esc(d.order?.orderAbcNum)}</span>
        <span><b>PO</b> ${esc(d.order?.origCustomerPo)}</span>
        <span><b>Customer</b> ${esc(d.customer?.customerName ?? d.order?.origCustomerId)}</span>
        <span><b>Enduser PO</b> ${esc(d.order?.enduserPo)}</span>
      </div>
      <table><thead><tr><th>Line</th><th>Part</th><th>Alloy</th><th>Sheet</th>
        <th class="num">Gauge</th><th class="num">Pieces</th></tr></thead>
        <tbody>${items || '<tr><td colspan="6" class="muted">No line items.</td></tr>'}</tbody></table>`;
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// ---- New order (transactional create-with-items) ---------------------------
function lineRow() {
    const div = document.createElement('div');
    div.className = 'line';
    div.innerHTML = `
    <input class="ePart" placeholder="part #" style="width:110px" />
    <select class="eAlloy">${alloys.map((a) => `<option>${esc(a)}</option>`).join('')}</select>
    <input class="eSheet" placeholder="sheet" value="FLAT" style="width:70px" />
    <input class="eGauge" placeholder="gauge" type="number" step="0.001" style="width:70px" />
    <input class="ePieces" placeholder="pieces" type="number" style="width:65px" />
    <input class="eQty" placeholder="qty" type="number" style="width:65px" />
    <input class="eDue" type="date" title="item due date" style="width:130px" />
    <input class="ePrice" placeholder="unit $" type="number" step="0.00001" style="width:75px" />
    <button class="del" type="button" title="remove line">✕</button>`;
    div.querySelector('.del').addEventListener('click', () => div.remove());
    return div;
}
async function createOrder() {
    setErr('');
    setBusy(true);
    const order = new CustomerOrderWrite({
        origCustomerId: Number($('#nCustomer').value.trim()) || undefined,
        enduserId: Number($('#nEnduser').value.trim()) || undefined,
        origCustomerPo: $('#nPo').value.trim() || undefined,
        enduserPo: $('#nEnduserPo').value.trim() || undefined,
    });
    const items = Array.from(document.querySelectorAll('#lines .line')).map((row) => {
        const due = row.querySelector('.eDue').value;
        return new OrderItemWrite({
            enduserPartNum: row.querySelector('.ePart').value.trim() || undefined,
            alloy2: row.querySelector('.eAlloy').value || undefined,
            sheetType: row.querySelector('.eSheet').value.trim() || undefined,
            gauge: Number(row.querySelector('.eGauge').value) || undefined,
            piecesSkid: Number(row.querySelector('.ePieces').value) || undefined,
            quantity: Number(row.querySelector('.eQty').value) || undefined,
            itemDueDate: due ? new Date(due) : undefined,
            unitPrice: Number(row.querySelector('.ePrice').value) || undefined,
        });
    });
    if (items.length === 0) {
        setErr('Add at least one line item.');
        setBusy(false);
        return;
    }
    try {
        const created = await client().createOrderWithItems(new OrderCreateWithItems({ order, items }));
        $('#newResult').textContent = `✓ Created order ${created.order?.orderAbcNum} with ${created.items?.length ?? 0} line(s).`;
        await search();
        if (created.order?.orderAbcNum)
            await loadOrder(created.order.orderAbcNum);
    }
    catch (e) {
        setErr(`Create failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// ---- Wire up ---------------------------------------------------------------
async function init() {
    try {
        alloys = await client().listAlloys();
    }
    catch {
        alloys = [];
    }
    $('#lines').appendChild(lineRow());
    $('#btnSearch').addEventListener('click', search);
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnAddLine').addEventListener('click', () => $('#lines').appendChild(lineRow()));
    $('#btnCreate').addEventListener('click', createOrder);
    await search();
}
void initShell({ active: 'order-entry', adopt: true }).then(init);
