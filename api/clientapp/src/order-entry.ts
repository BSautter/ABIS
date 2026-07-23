// ABIS Order Entry — the legacy order_entry window, restyled to the design system in the shared
// shell (#4 polish). Order search, the order-detail read model (header + customer + lines), and a
// transactional create-with-items save. Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/order-entry.js; served at /ui/order-entry.html.
import { AbisClient, OrderCreateWithItems, CustomerOrderWrite, OrderItemWrite } from './generated/abis-client.js';
import { authFetch } from './auth.js';
import { initShell } from './shell.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T => document.querySelector(sel) as T;
const client = (): AbisClient => new AbisClient('', { fetch: authFetch });
const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] as string));
const numf = (v: number | undefined): string => (v == null ? '' : v.toLocaleString());
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);
const val = (id: string) => $<HTMLInputElement>(id).value.trim();

let alloys: string[] = [];
let currentDetail: any = null;   // the loaded order detail (header + customer + items) for in-place edit

function scaffold(): string {
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

async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const custRaw = $<HTMLInputElement>('#fCustomer').value.trim();
  const po = $<HTMLInputElement>('#fPo').value.trim() || undefined;
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
    document.querySelectorAll<HTMLTableRowElement>('#orders tr.click').forEach((tr) =>
      tr.addEventListener('click', () => void loadOrder(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

async function loadOrder(id: number): Promise<void> {
  setErr(''); setBusy(true);
  try {
    currentDetail = await client().getOrderDetail(id);
    renderDetail();
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// Read-only order detail with an Edit toggle.
function renderDetail(): void {
  const d = currentDetail; if (!d) return;
  const items = (d.items ?? []).map((it: any) => `
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
      <button class="btn sm ghost" id="btnEditOrder" type="button">Edit</button>
    </div>
    <div style="overflow-x:auto;margin-top:12px"><table class="tbl" style="min-width:420px">
      <thead><tr><th>Line</th><th>Part</th><th>Alloy</th><th>Sheet</th><th class="num">Gauge</th><th class="num">Pieces</th></tr></thead>
      <tbody>${items || '<tr><td colspan="6" class="muted">No line items.</td></tr>'}</tbody></table></div>
    <div id="coils" style="margin-top:16px"></div>`;
  $('#btnEditOrder').addEventListener('click', renderEditForm);
  void renderCoils(Number(d.order?.orderAbcNum));
}

// Customer coils earmarked to this order (legacy ORDER_COIL): the assigned list + a picker of the
// customer's available coils (status 1..9). Assigning a coil already on another order prompts a
// confirm (the dup-org warning) before POSTing with confirm=true. Raw authFetch (no client method).
async function renderCoils(orderId: number): Promise<void> {
  const box = $('#coils'); if (!orderId) { box.innerHTML = ''; return; }
  box.innerHTML = '<p class="muted">Loading coils…</p>';
  try {
    const [assigned, avail] = await Promise.all([
      authFetch(`/api/orders/${orderId}/coils`).then((r) => r.json()),
      authFetch(`/api/orders/${orderId}/available-coils`).then((r) => r.json()),
    ]);
    const arows = ((assigned ?? []) as any[]).map((c) => `
      <tr><td class="mono">${esc(c.coilAbcNum)}</td><td>${esc(c.coilOrgNum)}</td><td>${esc(c.coilAlloy2)}</td>
        <td class="num">${numf(c.netWtBalance)}</td>
        <td><button class="btn sm ghost" data-remove-coil="${esc(c.coilAbcNum)}" type="button">Remove</button></td></tr>`).join('');
    // The picker offers only coils not already on this order.
    const pick = ((avail ?? []) as any[]).filter((c) => !c.assignedToThisOrder);
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
    box.querySelectorAll<HTMLButtonElement>('[data-remove-coil]').forEach((b) =>
      b.addEventListener('click', () => void removeCoil(orderId, Number(b.getAttribute('data-remove-coil')))));
    box.querySelectorAll<HTMLButtonElement>('[data-assign-coil]').forEach((b) =>
      b.addEventListener('click', () => void assignCoil(orderId, Number(b.getAttribute('data-assign-coil')), b.getAttribute('data-other') || '')));
  } catch (e) { box.innerHTML = `<p class="err">Coils failed: ${esc((e as Error).message)}</p>`; }
}

async function assignCoil(orderId: number, coilNum: number, otherOrder: string): Promise<void> {
  let confirm = false;
  if (otherOrder) {
    if (!window.confirm(`Coil ${coilNum} is already earmarked to order ${otherOrder}. Assign it to this order too?`)) return;
    confirm = true;   // the dup-org warning was accepted
  }
  try {
    const r = await authFetch(`/api/orders/${orderId}/coils`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ coilAbcNum: coilNum, confirm }),
    });
    if (!r.ok) {
      const b = await r.json().catch(() => ({ message: `HTTP ${r.status}` }));
      const msg = $('#coilMsg'); msg.textContent = b.message ?? `Assign failed (${r.status}).`; msg.className = 'err';
      return;
    }
    await renderCoils(orderId);
  } catch (e) { setErr(`Assign failed: ${(e as Error).message}`); }
}

async function removeCoil(orderId: number, coilNum: number): Promise<void> {
  if (!window.confirm(`Remove coil ${coilNum} from this order?`)) return;
  try {
    const r = await authFetch(`/api/orders/${orderId}/coils/${coilNum}`, { method: 'DELETE' });
    if (!r.ok) { setErr(`Remove failed (${r.status}).`); return; }
    await renderCoils(orderId);
  } catch (e) { setErr(`Remove failed: ${(e as Error).message}`); }
}

// Editable order header + line items. Saves via full-replace PUTs, reconstructing the whole write
// object from the loaded read model (spread) so unedited fields are preserved.
function renderEditForm(): void {
  const d = currentDetail; const o = d.order ?? {};
  const lines = (d.items ?? []).map((it: any) => `
    <tr data-item="${esc(it.orderItemNum)}">
      <td class="mono">${esc(it.orderItemNum)}</td>
      <td><input class="li-part" value="${esc(it.enduserPartNum)}" style="width:110px" /></td>
      <td><input class="li-alloy" value="${esc(it.alloy2)}" style="width:70px" /></td>
      <td><input class="li-sheet" value="${esc(it.sheetType)}" style="width:80px" /></td>
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
  $('#detail').querySelectorAll<HTMLButtonElement>('[data-save-line]').forEach((b) =>
    b.addEventListener('click', () => void saveLine(Number(b.getAttribute('data-save-line')))));
}

async function saveHeader(): Promise<void> {
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
    if (!r.ok) { setErr(`Save failed (${r.status}).`); return; }
    await loadOrder(o.orderAbcNum);
  } catch (e) { setErr(`Save failed: ${(e as Error).message}`); }
}

async function saveLine(itemNum: number): Promise<void> {
  const o = currentDetail?.order ?? {};
  const item = (currentDetail?.items ?? []).find((it: any) => it.orderItemNum === itemNum);
  if (!item) return;
  setErr('');
  const row = $<HTMLTableRowElement>(`#detail tr[data-item="${itemNum}"]`);
  const g = (sel: string) => (row.querySelector(sel) as HTMLInputElement).value.trim();
  const body = {
    ...item,
    enduserPartNum: g('.li-part') || null,
    alloy2: g('.li-alloy') || null,
    sheetType: g('.li-sheet') || null,
    gauge: g('.li-gauge') ? Number(g('.li-gauge')) : null,
    quantity: g('.li-qty') ? Number(g('.li-qty')) : null,
  };
  try {
    const r = await authFetch(`/api/orders/${o.orderAbcNum}/items/${itemNum}`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    if (!r.ok) { setErr(`Line ${itemNum} save failed (${r.status}).`); return; }
    await loadOrder(o.orderAbcNum);
  } catch (e) { setErr(`Line save failed: ${(e as Error).message}`); }
}

function lineRow(): HTMLDivElement {
  const div = document.createElement('div');
  div.className = 'frow';
  div.style.cssText = 'gap:8px;margin-bottom:9px;padding-bottom:9px;border-bottom:1px solid var(--line-2)';
  div.innerHTML = `
    <div class="fld"><label>Part #</label><input class="ePart" style="width:120px" /></div>
    <div class="fld"><label>Alloy</label><select class="eAlloy">${alloys.map((a) => `<option>${esc(a)}</option>`).join('')}</select></div>
    <div class="fld"><label>Sheet</label><input class="eSheet" value="FLAT" style="width:80px" /></div>
    <div class="fld"><label>Gauge</label><input class="eGauge" type="number" step="0.001" style="width:80px" /></div>
    <div class="fld"><label>Pieces</label><input class="ePieces" type="number" style="width:75px" /></div>
    <div class="fld"><label>Qty</label><input class="eQty" type="number" style="width:75px" /></div>
    <div class="fld"><label>Due</label><input class="eDue" type="date" style="width:140px" /></div>
    <div class="fld"><label>Unit $</label><input class="ePrice" type="number" step="0.00001" style="width:85px" /></div>
    <button class="mini" type="button" title="remove line" style="align-self:flex-end;color:var(--crit)">✕</button>`;
  div.querySelector('button')!.addEventListener('click', () => div.remove());
  return div;
}

async function createOrder(): Promise<void> {
  setErr(''); setBusy(true);
  const order = new CustomerOrderWrite({
    origCustomerId: Number($<HTMLInputElement>('#nCustomer').value.trim()) || undefined,
    enduserId: Number($<HTMLInputElement>('#nEnduser').value.trim()) || undefined,
    origCustomerPo: $<HTMLInputElement>('#nPo').value.trim() || undefined,
    enduserPo: $<HTMLInputElement>('#nEnduserPo').value.trim() || undefined,
  });
  const items = Array.from($('#lines').querySelectorAll<HTMLDivElement>(':scope > div')).map((row) => {
    const due = (row.querySelector('.eDue') as HTMLInputElement).value;
    return new OrderItemWrite({
      enduserPartNum: (row.querySelector('.ePart') as HTMLInputElement).value.trim() || undefined,
      alloy2: (row.querySelector('.eAlloy') as HTMLSelectElement).value || undefined,
      sheetType: (row.querySelector('.eSheet') as HTMLInputElement).value.trim() || undefined,
      gauge: Number((row.querySelector('.eGauge') as HTMLInputElement).value) || undefined,
      piecesSkid: Number((row.querySelector('.ePieces') as HTMLInputElement).value) || undefined,
      quantity: Number((row.querySelector('.eQty') as HTMLInputElement).value) || undefined,
      itemDueDate: due ? new Date(due) : undefined,
      unitPrice: Number((row.querySelector('.ePrice') as HTMLInputElement).value) || undefined,
    });
  });
  if (items.length === 0) { setErr('Add at least one line item.'); setBusy(false); return; }
  try {
    const created = await client().createOrderWithItems(new OrderCreateWithItems({ order, items }));
    $('#newResult').textContent = `✓ Created order ${created.order?.orderAbcNum} with ${created.items?.length ?? 0} line(s).`;
    await search();
    if (created.order?.orderAbcNum) await loadOrder(created.order.orderAbcNum);
  } catch (e) { setErr(`Create failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

(async () => {
  const main = await initShell({ active: 'order-entry' });
  main.innerHTML = scaffold();
  try { alloys = await client().listAlloys(); } catch { alloys = []; }
  $('#lines').appendChild(lineRow());
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnAddLine').addEventListener('click', () => $('#lines').appendChild(lineRow()));
  $('#btnCreate').addEventListener('click', () => void createOrder());
  await search();
})();
