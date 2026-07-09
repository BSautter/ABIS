// ABIS Carriers — the carrier master (referenced by shipping), restyled to the design system in
// the shared shell (#4 polish). Status-filtered search + edit/create. Typed calls via the NSwag
// client.
//
// Compiled by tsc to wwwroot/ui/app/carriers.js; served at /ui/carriers.html.
import { AbisClient, CarrierWrite } from './generated/abis-client.js';
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
const chip = (s) => `<span class="chip mut">${esc(s ?? '—')}</span>`;
let editingId = null;
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Logistics · Master data</div><h1>Carriers</h1></div>
      <div class="shift-tag" id="count">—</div>
    </div>
    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Status</label><input id="fStatus" inputmode="numeric" style="width:100px" placeholder="any" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>
    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Carriers</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:620px">
          <thead><tr><th>Id</th><th>SCAC</th><th>Name</th><th>Type</th><th>Location</th><th>Phone</th><th>Status</th></tr></thead>
          <tbody id="carriers"><tr><td colspan="7" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack"><div class="card">
        <header><h2 id="formTitle">New carrier</h2></header>
        <div class="body">
          <div class="frow">
            <div class="fld"><label>SCAC</label><input id="cScac" style="width:90px" /></div>
            <div class="fld" style="flex:1;min-width:160px"><label>Name</label><input id="cName" /></div>
            <div class="fld"><label>Type code</label><input id="cType" style="width:100px" /></div>
            <div class="fld"><label>City</label><input id="cCity" style="width:120px" /></div>
            <div class="fld"><label>State</label><input id="cState" style="width:70px" /></div>
            <div class="fld"><label>Phone</label><input id="cPhone" style="width:140px" /></div>
            <div class="fld"><label>Status</label><input id="cStatus" inputmode="numeric" style="width:90px" /></div>
          </div>
          <div class="frow" style="margin-top:10px;align-items:center">
            <button class="btn sm" id="btnSave" type="button">Save</button>
            <button class="btn sm ghost" id="btnNew" type="button">New</button>
            <span id="ok" class="ok-note"></span>
          </div>
        </div>
      </div></div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    const status = v('#fStatus') ? Number(v('#fStatus')) : undefined;
    try {
        const page = await client().listCarriers(1, 50, status, undefined, undefined);
        const items = page.items ?? [];
        $('#carriers').innerHTML = items.length ? items.map((c) => `
      <tr class="click" data-id="${c.carrierId}">
        <td class="mono">${esc(c.carrierId)}</td><td class="mono">${esc(c.scac)}</td><td>${esc(c.carrierFullName)}</td>
        <td>${esc(c.carrierTypeCode)}</td><td>${esc([c.carrierCity, c.carrierState].filter(Boolean).join(', '))}</td>
        <td class="mono">${esc(c.carrierPhoneNumber)}</td><td>${chip(c.status)}</td>
      </tr>`).join('') : '<tr><td colspan="7" class="muted">No matching carriers.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} carriers`;
        $('#listSub').textContent = `${items.length} shown`;
        document.querySelectorAll('#carriers tr.click').forEach((tr) => tr.addEventListener('click', () => void loadCarrier(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadCarrier(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const c = await client().getCarrier(id);
        editingId = id;
        $('#formTitle').textContent = `Edit carrier #${id}`;
        setV('#cScac', c.scac);
        setV('#cName', c.carrierFullName);
        setV('#cType', c.carrierTypeCode);
        setV('#cCity', c.carrierCity);
        setV('#cState', c.carrierState);
        setV('#cPhone', c.carrierPhoneNumber);
        setV('#cStatus', c.status);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newCarrier() {
    editingId = null;
    $('#formTitle').textContent = 'New carrier';
    ['#cScac', '#cName', '#cType', '#cCity', '#cState', '#cPhone', '#cStatus'].forEach((id) => setV(id, ''));
    setOk('');
    setErr('');
}
async function save() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new CarrierWrite({
        scac: v('#cScac') || undefined,
        carrierFullName: v('#cName') || undefined,
        carrierTypeCode: v('#cType') || undefined,
        carrierCity: v('#cCity') || undefined,
        carrierState: v('#cState') || undefined,
        carrierPhoneNumber: v('#cPhone') || undefined,
        status: v('#cStatus') ? Number(v('#cStatus')) : undefined,
    });
    try {
        if (editingId == null) {
            const created = await client().createCarrier(body);
            setOk(`✓ Created carrier #${created.carrierId}.`);
        }
        else {
            await client().updateCarrier(editingId, body);
            setOk(`✓ Saved carrier #${editingId}.`);
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
    const main = await initShell({ active: 'carriers' });
    main.innerHTML = scaffold();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newCarrier);
    $('#btnSave').addEventListener('click', () => void save());
    newCarrier();
    await search();
})();
