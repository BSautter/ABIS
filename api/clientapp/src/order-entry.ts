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

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

// Auth — a Bearer token (OIDC) or the X-Api-Key field — is attached by ./auth.
function client(): AbisClient {
  return new AbisClient('', { fetch: authFetch });
}

const esc = (s: unknown): string =>
  String(s ?? '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c] as string));
const numf = (v: number | undefined): string => (v == null ? '' : v.toLocaleString());
const setErr = (m: string) => { $('#err').textContent = m; };
const setBusy = (b: boolean) => document.body.classList.toggle('busy', b);

let alloys: string[] = [];

// ---- Search + list ---------------------------------------------------------
async function search(): Promise<void> {
  setErr(''); setBusy(true);
  const custRaw = $<HTMLInputElement>('#fCustomer').value.trim();
  const po = $<HTMLInputElement>('#fPo').value.trim() || undefined;
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
    document.querySelectorAll<HTMLTableRowElement>('#orders tr.click').forEach((tr) =>
      tr.addEventListener('click', () => loadOrder(Number(tr.dataset.id))));
  } catch (e) { setErr(`Search failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// ---- Detail (header + customer + line items) -------------------------------
async function loadOrder(id: number): Promise<void> {
  setErr(''); setBusy(true);
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
  } catch (e) { setErr(`Load failed: ${(e as Error).message}`); }
  finally { setBusy(false); }
}

// ---- New order (transactional create-with-items) ---------------------------
function lineRow(): HTMLDivElement {
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
  div.querySelector('.del')!.addEventListener('click', () => div.remove());
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
  const items = Array.from(document.querySelectorAll<HTMLDivElement>('#lines .line')).map((row) => {
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

// ---- Wire up ---------------------------------------------------------------
async function init(): Promise<void> {
  try { alloys = await client().listAlloys(); } catch { alloys = []; }
  $('#lines').appendChild(lineRow());
  $('#btnSearch').addEventListener('click', search);
  $<HTMLFormElement>('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
  $('#btnAddLine').addEventListener('click', () => $('#lines').appendChild(lineRow()));
  $('#btnCreate').addEventListener('click', createOrder);
  await search();
}

void initShell({ active: 'order-entry', adopt: true }).then(init);
