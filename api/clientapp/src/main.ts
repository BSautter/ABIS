// Typed ABIS demo — the same coil-inventory screen as /ui/coils.html, but driven
// entirely by the NSwag-generated, strongly-typed client (./generated/abis-client).
// This proves the OpenAPI contract is usable end-to-end: models and method
// signatures are checked by the compiler, not hand-written `fetch` glue.
//
// Compiled by `tsc` (see ../tsconfig.json) to browser ES modules in
// wwwroot/ui/app/, so it is served with no runtime build step.
import { AbisClient, Coil, OrderCreateWithItems, CustomerOrderWrite, OrderItemWrite } from './generated/abis-client.js';

const $ = <T extends HTMLElement = HTMLElement>(sel: string): T =>
  document.querySelector(sel) as T;

const keyInput = $<HTMLInputElement>('#apiKey');
keyInput.value = localStorage.getItem('abis_api_key') ?? 'dev-local-key';
keyInput.addEventListener('change', () => localStorage.setItem('abis_api_key', keyInput.value));

// A client whose every request carries the API key. baseUrl "" = same origin.
function client(): AbisClient {
  return new AbisClient('', {
    fetch: (url: RequestInfo, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      headers.set('X-Api-Key', keyInput.value);
      return fetch(url, { ...init, headers });
    },
  });
}

// Escape before interpolating into innerHTML. Coil alloy/temper/location/status are plant- and
// customer-entered free text, so without this they execute as HTML on the served page. Every other
// module in this app defines this helper; this one did not, which is how it was missed.
const esc = (v: unknown): string =>
  String(v ?? '').replace(/[&<>"']/g, (ch) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch] as string));

const fmt = (v: number | undefined): string => (v == null ? '' : v.toLocaleString());

async function search(): Promise<void> {
  const sort = $<HTMLSelectElement>('#sort').value;
  const dir = $<HTMLSelectElement>('#dir').value;
  const msg = $('#msg');
  msg.textContent = 'Loading…';
  try {
    // Strongly typed: wrong arg types or field names would fail compilation.
    const page = await client().listCoils(1, 50, undefined, undefined, undefined, undefined, undefined, undefined, sort, dir);
    const rows = (page.items ?? [])
      .map(
        (c: Coil) => `
      <tr class="click" data-id="${c.coilAbcNum}">
        <td>${c.coilAbcNum}</td><td>${esc(c.coilAlloy2)}</td><td>${esc(c.coilTemper)}</td>
        <td class="num">${fmt(c.coilGauge)}</td><td>${esc(c.coilLocation)}</td>
        <td class="num">${fmt(c.netWt)}</td><td class="num">${fmt(c.netWtBalance)}</td>
        <td>${esc(c.coilStatus)}</td>
      </tr>`
      )
      .join('');
    $('#tbl tbody').innerHTML = rows;
    document
      .querySelectorAll<HTMLTableRowElement>('#tbl tbody tr')
      .forEach((tr) => (tr.onclick = () => detail(Number(tr.dataset.id))));
    msg.textContent = `${page.totalCount} coil(s) · via generated TypeScript client`;
  } catch (e) {
    msg.innerHTML = `<span class="err">${(e as Error).message ?? e} — check the API key</span>`;
  }
}

async function detail(id: number): Promise<void> {
  const box = $('#detail');
  box.textContent = 'Loading…';
  try {
    const c = await client().getCoil(id);
    box.innerHTML = `<b>Coil ${c.coilAbcNum}</b> · ${c.coilAlloy2 ?? ''} ${c.coilTemper ?? ''} · ${c.coilLocation ?? ''}
      <div class="muted">net ${fmt(c.netWt)} / bal ${fmt(c.netWtBalance)} · status ${c.coilStatus ?? ''}</div>`;
  } catch (e) {
    box.innerHTML = `<span class="err">${(e as Error).message ?? e}</span>`;
  }
}

// Typed write path: create an order + line item through the generated client.
// The request body is built from generated DTO classes (compile-checked), and the
// response is a typed OrderDetail.
async function createOrder(): Promise<void> {
  const msg = $('#createMsg');
  msg.textContent = 'Saving…';
  try {
    const body = new OrderCreateWithItems({
      order: new CustomerOrderWrite({
        origCustomerId: 4001,
        origCustomerPo: $<HTMLInputElement>('#oPo').value || 'PO-TYPED',
      }),
      items: [
        new OrderItemWrite({
          enduserPartNum: $<HTMLInputElement>('#oPart').value || 'PN-TYPED',
          alloy2: $<HTMLInputElement>('#oAlloy').value || '3003',
        }),
      ],
    });
    const detail = await client().createOrderWithItems(body);
    msg.innerHTML = `<span class="pill">Created order ${detail.order.orderAbcNum} with ${detail.items?.length ?? 0} item(s)</span>`;
  } catch (e) {
    msg.innerHTML = `<span class="err">${(e as Error).message ?? e}</span>`;
  }
}

$('#btnSearch').addEventListener('click', search);
$('#btnCreate').addEventListener('click', createOrder);
void search();
