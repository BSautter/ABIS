// Typed ABIS demo — the same coil-inventory screen as /ui/coils.html, but driven
// entirely by the NSwag-generated, strongly-typed client (./generated/abis-client).
// This proves the OpenAPI contract is usable end-to-end: models and method
// signatures are checked by the compiler, not hand-written `fetch` glue.
//
// Compiled by `tsc` (see ../tsconfig.json) to browser ES modules in
// wwwroot/ui/app/, so it is served with no runtime build step.
import { AbisClient, OrderCreateWithItems, CustomerOrderWrite, OrderItemWrite } from './generated/abis-client.js';
const $ = (sel) => document.querySelector(sel);
const keyInput = $('#apiKey');
keyInput.value = localStorage.getItem('abis_api_key') ?? 'dev-local-key';
keyInput.addEventListener('change', () => localStorage.setItem('abis_api_key', keyInput.value));
// A client whose every request carries the API key. baseUrl "" = same origin.
function client() {
    return new AbisClient('', {
        fetch: (url, init) => {
            const headers = new Headers(init?.headers);
            headers.set('X-Api-Key', keyInput.value);
            return fetch(url, { ...init, headers });
        },
    });
}
const fmt = (v) => (v == null ? '' : v.toLocaleString());
async function search() {
    const sort = $('#sort').value;
    const dir = $('#dir').value;
    const msg = $('#msg');
    msg.textContent = 'Loading…';
    try {
        // Strongly typed: wrong arg types or field names would fail compilation.
        const page = await client().listCoils(1, 50, undefined, undefined, undefined, undefined, undefined, undefined, sort, dir);
        const rows = (page.items ?? [])
            .map((c) => `
      <tr class="click" data-id="${c.coilAbcNum}">
        <td>${c.coilAbcNum}</td><td>${c.coilAlloy2 ?? ''}</td><td>${c.coilTemper ?? ''}</td>
        <td class="num">${fmt(c.coilGauge)}</td><td>${c.coilLocation ?? ''}</td>
        <td class="num">${fmt(c.netWt)}</td><td class="num">${fmt(c.netWtBalance)}</td>
        <td>${c.coilStatus ?? ''}</td>
      </tr>`)
            .join('');
        $('#tbl tbody').innerHTML = rows;
        document
            .querySelectorAll('#tbl tbody tr')
            .forEach((tr) => (tr.onclick = () => detail(Number(tr.dataset.id))));
        msg.textContent = `${page.totalCount} coil(s) · via generated TypeScript client`;
    }
    catch (e) {
        msg.innerHTML = `<span class="err">${e.message ?? e} — check the API key</span>`;
    }
}
async function detail(id) {
    const box = $('#detail');
    box.textContent = 'Loading…';
    try {
        const c = await client().getCoil(id);
        box.innerHTML = `<b>Coil ${c.coilAbcNum}</b> · ${c.coilAlloy2 ?? ''} ${c.coilTemper ?? ''} · ${c.coilLocation ?? ''}
      <div class="muted">net ${fmt(c.netWt)} / bal ${fmt(c.netWtBalance)} · status ${c.coilStatus ?? ''}</div>`;
    }
    catch (e) {
        box.innerHTML = `<span class="err">${e.message ?? e}</span>`;
    }
}
// Typed write path: create an order + line item through the generated client.
// The request body is built from generated DTO classes (compile-checked), and the
// response is a typed OrderDetail.
async function createOrder() {
    const msg = $('#createMsg');
    msg.textContent = 'Saving…';
    try {
        const body = new OrderCreateWithItems({
            order: new CustomerOrderWrite({
                origCustomerId: 4001,
                origCustomerPo: $('#oPo').value || 'PO-TYPED',
            }),
            items: [
                new OrderItemWrite({
                    enduserPartNum: $('#oPart').value || 'PN-TYPED',
                    alloy2: $('#oAlloy').value || '3003',
                }),
            ],
        });
        const detail = await client().createOrderWithItems(body);
        msg.innerHTML = `<span class="pill">Created order ${detail.order.orderAbcNum} with ${detail.items?.length ?? 0} item(s)</span>`;
    }
    catch (e) {
        msg.innerHTML = `<span class="err">${e.message ?? e}</span>`;
    }
}
$('#btnSearch').addEventListener('click', search);
$('#btnCreate').addEventListener('click', createOrder);
void search();
