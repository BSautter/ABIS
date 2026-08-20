// ABIS Order Entry — the legacy order_entry window, restyled to the design system in the shared
// shell (#4 polish). Order search, the order-detail read model (header + customer + lines), and a
// transactional create-with-items save. Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/order-entry.js; served at /ui/order-entry.html.
import { AbisClient, OrderCreateWithItems, CustomerOrderWrite, OrderItemWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';
const $ = (sel) => document.querySelector(sel);
const client = () => new AbisClient('', { fetch: authFetch });
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const numf = (v) => (v == null ? '' : v.toLocaleString());
const setErr = (m) => { $('#err').textContent = m; };
const setBusy = (b) => document.body.classList.toggle('busy', b);
const val = (id) => $(id).value.trim();
let alloys = [];
let currentDetail = null; // the loaded order detail (header + customer + items) for in-place edit
const custParts = new Map(); // the new-order customer's parts, keyed by part # (the picker source)
// Load the parts belonging to the customer typed into the New-order form; populate the shared
// datalist so a line's Part # field autocompletes and prefills the spec on selection.
// The sector domain (table SECTOR). Legacy order entry picks it from a dropdown and refuses to save
// a line without one — sector has been mandatory since 2017 and is filled in on every order line
// written since, so the picker must never be a free-text box.
let sectors = [];
function sectorOptions(selected) {
    const sel = selected == null ? '' : String(selected);
    return `<option value="">— pick —</option>` + sectors.map((x) => `<option value="${esc(x.sectorCode)}"${String(x.sectorCode) === sel ? ' selected' : ''}>${esc(x.sectorDesc ?? x.sectorCode)}</option>`).join('');
}
/// A 409 from the sector gate: a mix of sectors on one order. Legacy asks Yes/No and defaults to No,
/// so this asks too, and only retries when the operator says yes.
async function acceptedMixedSectors(r) {
    const b = await r.json().catch(() => null);
    if (b?.code !== 'mixed-sectors')
        return false;
    return window.confirm(`${b.message ?? 'There is a mix of sectors in this order.'}

Continue?`);
}
async function loadCustomerParts() {
    custParts.clear();
    const list = $('#partList');
    if (list)
        list.innerHTML = '';
    const cid = $('#nCustomer').value.trim();
    if (!cid)
        return;
    try {
        const page = await client().listParts(1, 500, Number(cid), undefined, undefined, undefined);
        const parts = (page.items ?? []).filter((p) => p.enduserPartNum);
        parts.forEach((p) => { if (p.enduserPartNum)
            custParts.set(p.enduserPartNum, p); });
        if (list)
            list.innerHTML = parts.map((p) => `<option value="${esc(p.enduserPartNum)}">#${esc(p.partNumId)} · ${esc(p.alloy)} ${esc(p.sheetType)} ${esc(p.gauge)}</option>`).join('');
    }
    catch { /* the picker is best-effort — manual entry still works */ }
}
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Commercial · Orders</div><h1>Order entry</h1></div>
      <div class="shift-tag" id="orderCount">—</div>
    </div>

    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Customer id</label><input id="fCustomer" inputmode="numeric" style="width:120px" placeholder="e.g. 4001" /></div>
        <div class="fld"><label>PO contains</label><input id="fPo" style="width:170px" placeholder="PO…" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Orders</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:480px">
          <thead><tr><th>Order</th><th>Cust</th><th>Cust PO</th><th>Enduser PO</th></tr></thead>
          <tbody id="orders"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2>Order detail</h2></header>
        <div class="body" id="detail"><p class="muted">Select an order to view its header, customer, and line items.</p></div>
      </div></div>
    </div>

    <div class="card" style="margin-top:16px">
      <header><h2>New order</h2><span class="sub">create with line items</span></header>
      <div class="body">
        <div class="frow">
          <div class="fld"><label>Customer id</label><input id="nCustomer" inputmode="numeric" style="width:120px" placeholder="e.g. 4001" /></div>
          <div class="fld"><label>Enduser id</label><input id="nEnduser" inputmode="numeric" style="width:120px" placeholder="optional" /></div>
          <div class="fld"><label>Customer PO</label><input id="nPo" style="width:170px" placeholder="required" /></div>
          <div class="fld"><label>Enduser PO</label><input id="nEnduserPo" style="width:170px" placeholder="optional" /></div>
        </div>
        <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:16px 0 8px">Line items</h3>
        <datalist id="partList"></datalist>
        <div id="lines"></div>
        <div class="frow" style="margin-top:10px;align-items:center">
          <button class="btn sm ghost" id="btnAddLine" type="button">+ Add line</button>
          <button class="btn sm" id="btnCreate" type="button">Create order</button>
          <span id="newResult" class="ok-note"></span>
        </div>
      </div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    const custRaw = $('#fCustomer').value.trim();
    const po = $('#fPo').value.trim() || undefined;
    const customerId = custRaw ? Number(custRaw) : undefined;
    try {
        const page = await client().listOrders(1, 50, customerId, po, undefined, undefined);
        const items = page.items ?? [];
        $('#orders').innerHTML = items.length ? items.map((o) => `
      <tr class="click" data-id="${o.orderAbcNum}">
        <td class="mono">${esc(o.orderAbcNum)}</td><td class="mono">${esc(o.origCustomerId)}</td>
        <td>${esc(o.origCustomerPo)}</td><td>${esc(o.enduserPo)}</td>
      </tr>`).join('') : '<tr><td colspan="4" class="muted">No matching orders.</td></tr>';
        $('#orderCount').textContent = `${numf(page.totalCount)} orders`;
        $('#listSub').textContent = `${items.length} shown`;
        document.querySelectorAll('#orders tr.click').forEach((tr) => tr.addEventListener('click', () => void loadOrder(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadOrder(id) {
    setErr('');
    setBusy(true);
    try {
        currentDetail = await client().getOrderDetail(id);
        renderDetail();
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// Read-only order detail with an Edit toggle.
function renderDetail() {
    const d = currentDetail;
    if (!d)
        return;
    const items = (d.items ?? []).map((it) => `
    <tr><td class="mono">${esc(it.orderItemNum)}</td><td>${esc(it.enduserPartNum)}</td><td>${esc(it.alloy2)}</td>
      <td>${esc(it.sheetType)}</td><td class="num">${numf(it.gauge)}</td><td class="num">${numf(it.piecesSkid)}</td></tr>`).join('');
    $('#detail').innerHTML = `
    <div class="frow" style="justify-content:space-between;align-items:flex-start">
      <div class="kv">
        <span><b>Order</b>${esc(d.order?.orderAbcNum)}</span>
        <span><b>PO</b>${esc(d.order?.origCustomerPo)}</span>
        <span><b>Customer</b>${esc(d.customer?.customerName ?? d.order?.origCustomerId)}</span>
        <span><b>Enduser PO</b>${esc(d.order?.enduserPo)}</span>
      </div>
      <div class="frow" style="gap:6px">
        <button class="btn sm ghost" id="btnEditOrder" type="button">Edit</button>
        <button class="btn sm ghost" id="btnCopyOrder" type="button">Duplicate</button>
      </div>
    </div>
    <div style="overflow-x:auto;margin-top:12px"><table class="tbl" style="min-width:420px">
      <thead><tr><th>Line</th><th>Part</th><th>Alloy</th><th>Sheet</th><th class="num">Gauge</th><th class="num">Pieces</th></tr></thead>
      <tbody>${items || '<tr><td colspan="6" class="muted">No line items.</td></tr>'}</tbody></table></div>
    <div id="coils" style="margin-top:16px"></div>`;
    $('#btnEditOrder').addEventListener('click', renderEditForm);
    $('#btnCopyOrder').addEventListener('click', () => void copyOrder(Number(d.order?.orderAbcNum)));
    void renderCoils(Number(d.order?.orderAbcNum));
}
// Duplicate this order (header + line items + geometry) into a new order, then open the copy.
async function copyOrder(orderId) {
    if (!orderId || !window.confirm(`Duplicate order ${orderId} into a new order?`))
        return;
    setErr('');
    setBusy(true);
    try {
        const r = await authFetch(`/api/orders/${orderId}/copy`, { method: 'POST' });
        if (!r.ok) {
            setErr(`Duplicate failed (${r.status}).`);
            return;
        }
        const copy = await r.json();
        await search();
        if (copy?.order?.orderAbcNum)
            await loadOrder(copy.order.orderAbcNum);
    }
    catch (e) {
        setErr(`Duplicate failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
// Customer coils earmarked to this order (legacy ORDER_COIL): the assigned list + a picker of the
// customer's available coils (status 1..9). Assigning a coil already on another order prompts a
// confirm (the dup-org warning) before POSTing with confirm=true. Raw authFetch (no client method).
async function renderCoils(orderId) {
    const box = $('#coils');
    if (!orderId) {
        box.innerHTML = '';
        return;
    }
    box.innerHTML = '<p class="muted">Loading coils…</p>';
    try {
        const [assigned, avail] = await Promise.all([
            authFetch(`/api/orders/${orderId}/coils`).then((r) => r.json()),
            authFetch(`/api/orders/${orderId}/available-coils`).then((r) => r.json()),
        ]);
        const arows = (assigned ?? []).map((c) => `
      <tr><td class="mono">${esc(c.coilAbcNum)}</td><td>${esc(c.coilOrgNum)}</td><td>${esc(c.coilAlloy2)}</td>
        <td class="num">${numf(c.netWtBalance)}</td>
        <td><button class="btn sm ghost" data-remove-coil="${esc(c.coilAbcNum)}" type="button">Remove</button></td></tr>`).join('');
        // The picker offers only coils not already on this order.
        const pick = (avail ?? []).filter((c) => !c.assignedToThisOrder);
        const prows = pick.map((c) => {
            const warn = c.otherOrderAbcNum
                ? ` <span style="color:var(--warn,#b26b00);font-size:11px" title="Already on order ${esc(c.otherOrderAbcNum)}">⚠ on ${esc(c.otherOrderAbcNum)}</span>` : '';
            return `<tr><td class="mono">${esc(c.coilAbcNum)}${warn}</td><td>${esc(c.coilOrgNum)}</td><td>${esc(c.coilAlloy2)}</td>
        <td class="num">${numf(c.netWtBalance)}</td>
        <td><button class="btn sm" data-assign-coil="${esc(c.coilAbcNum)}" data-other="${esc(c.otherOrderAbcNum ?? '')}" type="button">Assign</button></td></tr>`;
        }).join('');
        box.innerHTML = `
      <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:0 0 8px">Assigned coils</h3>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
        <thead><tr><th>Coil #</th><th>Org #</th><th>Alloy</th><th class="num">Balance wt</th><th></th></tr></thead>
        <tbody>${arows || '<tr><td colspan="5" class="muted">No coils assigned.</td></tr>'}</tbody></table></div>
      <h3 style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--ink-3);margin:16px 0 8px">Available customer coils</h3>
      <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
        <thead><tr><th>Coil #</th><th>Org #</th><th>Alloy</th><th class="num">Balance wt</th><th></th></tr></thead>
        <tbody>${prows || '<tr><td colspan="5" class="muted">No available coils for this customer.</td></tr>'}</tbody></table></div>
      <div id="coilMsg" class="ok-note" style="margin-top:8px"></div>`;
        box.querySelectorAll('[data-remove-coil]').forEach((b) => b.addEventListener('click', () => void removeCoil(orderId, Number(b.getAttribute('data-remove-coil')))));
        box.querySelectorAll('[data-assign-coil]').forEach((b) => b.addEventListener('click', () => void assignCoil(orderId, Number(b.getAttribute('data-assign-coil')), b.getAttribute('data-other') || '')));
    }
    catch (e) {
        box.innerHTML = `<p class="err">Coils failed: ${esc(e.message)}</p>`;
    }
}
async function assignCoil(orderId, coilNum, otherOrder) {
    let confirm = false;
    if (otherOrder) {
        if (!window.confirm(`Coil ${coilNum} is already earmarked to order ${otherOrder}. Assign it to this order too?`))
            return;
        confirm = true; // the dup-org warning was accepted
    }
    try {
        const r = await authFetch(`/api/orders/${orderId}/coils`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ coilAbcNum: coilNum, confirm }),
        });
        if (!r.ok) {
            const b = await r.json().catch(() => ({ message: `HTTP ${r.status}` }));
            const msg = $('#coilMsg');
            msg.textContent = b.message ?? `Assign failed (${r.status}).`;
            msg.className = 'err';
            return;
        }
        await renderCoils(orderId);
    }
    catch (e) {
        setErr(`Assign failed: ${e.message}`);
    }
}
async function removeCoil(orderId, coilNum) {
    if (!window.confirm(`Remove coil ${coilNum} from this order?`))
        return;
    try {
        const r = await authFetch(`/api/orders/${orderId}/coils/${coilNum}`, { method: 'DELETE' });
        if (!r.ok) {
            setErr(`Remove failed (${r.status}).`);
            return;
        }
        await renderCoils(orderId);
    }
    catch (e) {
        setErr(`Remove failed: ${e.message}`);
    }
}
// Editable order header + line items. Saves via full-replace PUTs, reconstructing the whole write
// object from the loaded read model (spread) so unedited fields are preserved.
function renderEditForm() {
    const d = currentDetail;
    const o = d.order ?? {};
    const lines = (d.items ?? []).map((it) => `
    <tr data-item="${esc(it.orderItemNum)}">
      <td class="mono">${esc(it.orderItemNum)}</td>
      <td><input class="li-part" value="${esc(it.enduserPartNum)}" style="width:110px" /></td>
      <td><input class="li-alloy" value="${esc(it.alloy2)}" style="width:70px" /></td>
      <td><input class="li-sheet" value="${esc(it.sheetType)}" style="width:80px" /></td>
      <td><select class="li-sector" style="width:120px">${sectorOptions(it.sector)}</select></td>
      <td><input class="li-gauge" type="number" step="0.001" value="${esc(it.gauge)}" style="width:80px" /></td>
      <td><input class="li-qty" type="number" value="${esc(it.quantity)}" style="width:75px" /></td>
      <td><button class="btn sm" data-save-line="${esc(it.orderItemNum)}" type="button">Save</button></td>
    </tr>`).join('');
    $('#detail').innerHTML = `
    <div class="frow">
      <div class="fld"><label>Cust PO</label><input id="eoPo" value="${esc(o.origCustomerPo)}" style="width:150px" /></div>
      <div class="fld"><label>Enduser PO</label><input id="eoEnduserPo" value="${esc(o.enduserPo)}" style="width:150px" /></div>
      <div class="fld"><label>Enduser id</label><input id="eoEnduser" inputmode="numeric" value="${esc(o.enduserId)}" style="width:100px" /></div>
      <div class="fld"><label>Reference</label><input id="eoRef" value="${esc(o.reference)}" style="width:130px" /></div>
      <div class="fld"><label>Sales order</label><input id="eoSales" value="${esc(o.salesOrder)}" style="width:130px" /></div>
      <div class="fld" style="flex:1;min-width:180px"><label>Order note</label><input id="eoNote" value="${esc(o.custOrderNote)}" style="width:100%" /></div>
    </div>
    <div class="frow" style="margin-top:8px;align-items:center">
      <button class="btn sm" id="btnSaveOrder" type="button">Save order</button>
      <button class="btn sm ghost" id="btnCancelEdit" type="button">Cancel</button>
      <span id="editOk" class="ok-note"></span>
    </div>
    <div style="overflow-x:auto;margin-top:12px"><table class="tbl" style="min-width:560px">
      <thead><tr><th>Line</th><th>Part</th><th>Alloy</th><th>Sheet</th><th>Gauge</th><th>Qty</th><th></th></tr></thead>
      <tbody>${lines || '<tr><td colspan="7" class="muted">No line items.</td></tr>'}</tbody></table></div>`;
    $('#btnSaveOrder').addEventListener('click', () => void saveHeader());
    $('#btnCancelEdit').addEventListener('click', renderDetail);
    $('#detail').querySelectorAll('[data-save-line]').forEach((b) => b.addEventListener('click', () => void saveLine(Number(b.getAttribute('data-save-line')))));
}
async function saveHeader() {
    const o = currentDetail?.order ?? {};
    setErr('');
    const body = {
        ...o,
        origCustomerPo: val('#eoPo') || null,
        enduserPo: val('#eoEnduserPo') || null,
        enduserId: val('#eoEnduser') ? Number(val('#eoEnduser')) : null,
        reference: val('#eoRef') || null,
        salesOrder: val('#eoSales') || null,
        custOrderNote: val('#eoNote') || null,
    };
    try {
        const r = await authFetch(`/api/orders/${o.orderAbcNum}`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
        });
        if (!r.ok) {
            setErr(`Save failed (${r.status}).`);
            return;
        }
        await loadOrder(o.orderAbcNum);
    }
    catch (e) {
        setErr(`Save failed: ${e.message}`);
    }
}
async function saveLine(itemNum) {
    const o = currentDetail?.order ?? {};
    const item = (currentDetail?.items ?? []).find((it) => it.orderItemNum === itemNum);
    if (!item)
        return;
    setErr('');
    const row = $(`#detail tr[data-item="${itemNum}"]`);
    const g = (sel) => row.querySelector(sel).value.trim();
    const body = {
        ...item,
        enduserPartNum: g('.li-part') || null,
        alloy2: g('.li-alloy') || null,
        sheetType: g('.li-sheet') || null,
        sector: Number(row.querySelector('.li-sector').value) || null,
        gauge: g('.li-gauge') ? Number(g('.li-gauge')) : null,
        quantity: g('.li-qty') ? Number(g('.li-qty')) : null,
    };
    const put = (confirm) => authFetch(`/api/orders/${o.orderAbcNum}/items/${itemNum}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...body, confirm }),
    });
    try {
        let r = await put(false);
        if (r.status === 409 && await acceptedMixedSectors(r))
            r = await put(true);
        if (!r.ok) {
            const b = await r.json().catch(() => null);
            setErr(b?.message ?? `Line ${itemNum} save failed (${r.status}).`);
            return;
        }
        await loadOrder(o.orderAbcNum);
    }
    catch (e) {
        setErr(`Line save failed: ${e.message}`);
    }
}
function lineRow() {
    const div = document.createElement('div');
    div.className = 'frow';
    div.style.cssText = 'gap:8px;margin-bottom:9px;padding-bottom:9px;border-bottom:1px solid var(--line-2)';
    div.innerHTML = `
    <div class="fld"><label>Part #</label><input class="ePart" list="partList" style="width:130px" placeholder="pick or type" /></div>
    <div class="fld"><label>Alloy</label><select class="eAlloy">${alloys.map((a) => `<option>${esc(a)}</option>`).join('')}</select></div>
    <div class="fld"><label>Sheet</label><input class="eSheet" value="FLAT" style="width:80px" /></div>
    <div class="fld"><label>Sector</label><select class="eSector" style="width:120px">${sectorOptions(null)}</select></div>
    <div class="fld"><label>Gauge</label><input class="eGauge" type="number" step="0.001" style="width:80px" /></div>
    <div class="fld"><label>Pieces</label><input class="ePieces" type="number" style="width:75px" /></div>
    <div class="fld"><label>Qty</label><input class="eQty" type="number" style="width:75px" /></div>
    <div class="fld"><label>Due</label><input class="eDue" type="date" style="width:140px" /></div>
    <div class="fld"><label>Unit $</label><input class="ePrice" type="number" step="0.00001" style="width:85px" /></div>
    <button class="mini" type="button" title="remove line" style="align-self:flex-end;color:var(--crit)">✕</button>`;
    div.querySelector('button').addEventListener('click', () => div.remove());
    // Picking a known part prefills the line's spec + tags the row with its part_num_id.
    const partInput = div.querySelector('.ePart');
    partInput.addEventListener('change', () => {
        const p = custParts.get(partInput.value.trim());
        if (!p) {
            delete div.dataset.partNumId;
            return;
        }
        div.dataset.partNumId = String(p.partNumId);
        const set = (sel, v) => { const el = div.querySelector(sel); if (el && v != null)
            el.value = String(v); };
        set('.eAlloy', p.alloy);
        set('.eSheet', p.sheetType);
        set('.eGauge', p.gauge);
        set('.ePieces', p.piecesSkid);
        set('.eSector', p.sector);
    });
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
    const items = Array.from($('#lines').querySelectorAll(':scope > div')).map((row) => {
        const due = row.querySelector('.eDue').value;
        return new OrderItemWrite({
            partNumId: row.dataset.partNumId ? Number(row.dataset.partNumId) : undefined,
            enduserPartNum: row.querySelector('.ePart').value.trim() || undefined,
            alloy2: row.querySelector('.eAlloy').value || undefined,
            sheetType: row.querySelector('.eSheet').value.trim() || undefined,
            sector: Number(row.querySelector('.eSector').value) || undefined,
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
    // authFetch rather than the typed client so the mixed-sector 409 can be read and re-offered;
    // the request objects still build (and serialize) the body.
    const post = (confirm) => {
        items.forEach((i) => { i.confirm = confirm; });
        return authFetch('/api/orders/with-items', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(new OrderCreateWithItems({ order, items })),
        });
    };
    try {
        let r = await post(false);
        if (r.status === 409 && await acceptedMixedSectors(r))
            r = await post(true);
        if (!r.ok) {
            const b = await r.json().catch(() => null);
            setErr(b?.message ?? `Create failed (${r.status}).`);
            return;
        }
        const created = await r.json();
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
(async () => {
    const main = await initShell({ active: 'order-entry' });
    main.innerHTML = scaffold();
    try {
        alloys = await client().listAlloys();
    }
    catch {
        alloys = [];
    }
    try {
        sectors = await (await authFetch('/api/lookups/sectors')).json();
    }
    catch {
        sectors = [];
    }
    $('#lines').appendChild(lineRow());
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnAddLine').addEventListener('click', () => $('#lines').appendChild(lineRow()));
    $('#nCustomer').addEventListener('change', () => void loadCustomerParts());
    $('#btnCreate').addEventListener('click', () => void createOrder());
    await search();
})();
