// ABIS Customers — master-data (legacy customer window), restyled to the design system in the
// shared shell (#4 polish). Customer search + edit/create, plus the selected customer's contacts
// (list + add/edit). Typed calls via the NSwag client.
//
// Compiled by tsc to wwwroot/ui/app/customers.js; served at /ui/customers.html.
import { AbisClient, CustomerWrite, CustomerContactWrite } from './generated/abis-client.js';
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
let editingCustomerId = null;
let editingContactId = null;
const contactsById = new Map();
function scaffold() {
    return `
  <div class="page">
    <div class="page-head">
      <div><div class="eyebrow">Commercial · Master data</div><h1>Customers</h1></div>
      <div class="shift-tag" id="count">—</div>
    </div>

    <div class="card" style="margin-bottom:16px"><div class="body">
      <form id="searchForm" class="frow">
        <div class="fld"><label>Name contains</label><input id="fName" style="width:200px" placeholder="ACME…" /></div>
        <button class="btn sm" type="submit">Search</button>
      </form>
      <div id="err" class="err" style="margin-top:8px"></div>
    </div></div>

    <div class="grid">
      <div class="stack"><div class="card">
        <header><h2>Customers</h2><span class="sub" id="listSub"></span></header>
        <div style="overflow-x:auto"><table class="tbl" style="min-width:480px">
          <thead><tr><th>Id</th><th>Name</th><th>Short</th><th>Location</th></tr></thead>
          <tbody id="customers"><tr><td colspan="4" class="muted">Loading…</td></tr></tbody>
        </table></div>
      </div></div>
      <div class="stack">
        <div class="card">
          <header><h2 id="formTitle">New customer</h2></header>
          <div class="body">
            <div class="frow">
              <div class="fld" style="flex:1;min-width:180px"><label>Name</label><input id="cName" /></div>
              <div class="fld"><label>Short</label><input id="cShort" style="width:120px" /></div>
              <div class="fld"><label>City</label><input id="cCity" style="width:130px" /></div>
              <div class="fld"><label>State</label><input id="cState" style="width:80px" /></div>
              <div class="fld"><label>Zip</label><input id="cZip" style="width:90px" /></div>
            </div>
            <div class="frow" style="margin-top:10px;align-items:center">
              <button class="btn sm" id="btnSave" type="button">Save</button>
              <button class="btn sm ghost" id="btnNew" type="button">New</button>
              <span id="ok" class="ok-note"></span>
            </div>
          </div>
        </div>
        <div class="card" id="contactPanel">
          <header><h2 id="contactFormTitle">New contact</h2><span class="sub" id="contactHint"></span></header>
          <div class="body">
            <div style="overflow-x:auto"><table class="tbl" style="min-width:420px">
              <thead><tr><th>Name</th><th>Dept</th><th>Phone</th><th>Email</th></tr></thead>
              <tbody id="contacts"></tbody>
            </table></div>
            <div class="frow" style="margin-top:12px">
              <div class="fld"><label>Last</label><input id="kLast" style="width:120px" /></div>
              <div class="fld"><label>First</label><input id="kFirst" style="width:120px" /></div>
              <div class="fld"><label>Department</label><input id="kDept" style="width:120px" /></div>
              <div class="fld"><label>Phone</label><input id="kPhone" style="width:130px" /></div>
              <div class="fld"><label>Email</label><input id="kEmail" style="width:170px" /></div>
              <div class="fld"><label>City</label><input id="kCity" style="width:110px" /></div>
              <div class="fld"><label>State</label><input id="kState" style="width:70px" /></div>
            </div>
            <div class="frow" style="margin-top:10px">
              <button class="btn sm" id="btnContactSave" type="button">Save contact</button>
              <button class="btn sm ghost" id="btnContactNew" type="button">New contact</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>`;
}
async function search() {
    setErr('');
    setBusy(true);
    const name = v('#fName') || undefined;
    try {
        const page = await client().listCustomers(1, 50, name, undefined, undefined);
        const items = page.items ?? [];
        $('#customers').innerHTML = items.length ? items.map((c) => `
      <tr class="click" data-id="${c.customerId}">
        <td class="mono">${esc(c.customerId)}</td><td>${esc(c.customerName)}</td><td>${esc(c.customerShortName)}</td>
        <td>${esc([c.customerCity, c.customerState].filter(Boolean).join(', '))}</td>
      </tr>`).join('') : '<tr><td colspan="4" class="muted">No matching customers.</td></tr>';
        $('#count').textContent = `${(page.totalCount ?? 0).toLocaleString()} customers`;
        $('#listSub').textContent = `${items.length} shown`;
        document.querySelectorAll('#customers tr.click').forEach((tr) => tr.addEventListener('click', () => void loadCustomer(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Search failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
async function loadCustomer(id) {
    setErr('');
    setOk('');
    setBusy(true);
    try {
        const c = await client().getCustomer(id);
        editingCustomerId = id;
        $('#formTitle').textContent = `Edit customer #${id}`;
        setV('#cName', c.customerName);
        setV('#cShort', c.customerShortName);
        setV('#cCity', c.customerCity);
        setV('#cState', c.customerState);
        setV('#cZip', c.customerZip);
        await loadContacts(id);
        setContactsEnabled(true);
    }
    catch (e) {
        setErr(`Load failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
function newCustomer() {
    editingCustomerId = null;
    $('#formTitle').textContent = 'New customer';
    ['#cName', '#cShort', '#cCity', '#cState', '#cZip'].forEach((id) => setV(id, ''));
    $('#contacts').innerHTML = '';
    contactsById.clear();
    setContactsEnabled(false);
    newContact();
    setOk('');
    setErr('');
}
async function saveCustomer() {
    setErr('');
    setOk('');
    setBusy(true);
    const body = new CustomerWrite({
        customerName: v('#cName') || undefined,
        customerShortName: v('#cShort') || undefined,
        customerCity: v('#cCity') || undefined,
        customerState: v('#cState') || undefined,
        customerZip: v('#cZip') || undefined,
    });
    try {
        if (editingCustomerId == null) {
            const created = await client().createCustomer(body);
            editingCustomerId = created.customerId ?? null;
            $('#formTitle').textContent = `Edit customer #${created.customerId}`;
            setOk(`✓ Created customer #${created.customerId}. You can now add contacts.`);
            setContactsEnabled(true);
        }
        else {
            await client().updateCustomer(editingCustomerId, body);
            setOk(`✓ Saved customer #${editingCustomerId}.`);
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
async function loadContacts(customerId) {
    contactsById.clear();
    try {
        const contacts = await client().getCustomerContacts(customerId);
        (contacts ?? []).forEach((k) => { if (k.contactId != null)
            contactsById.set(k.contactId, k); });
        $('#contacts').innerHTML = (contacts ?? []).length ? (contacts ?? []).map((k) => `
      <tr class="click" data-id="${k.contactId}">
        <td>${esc([k.firstName, k.lastName].filter(Boolean).join(' '))}</td><td>${esc(k.department)}</td>
        <td class="mono">${esc(k.phone1)}</td><td>${esc(k.email1)}</td>
      </tr>`).join('') : '<tr><td colspan="4" class="muted">No contacts yet.</td></tr>';
        document.querySelectorAll('#contacts tr.click').forEach((tr) => tr.addEventListener('click', () => editContact(Number(tr.dataset.id))));
    }
    catch (e) {
        setErr(`Contacts load failed: ${e.message}`);
    }
}
function setContactsEnabled(on) {
    $('#contactPanel').classList.toggle('disabled', !on);
    $('#contactHint').textContent = on ? '' : 'Save the customer first to add contacts.';
}
function editContact(contactId) {
    const k = contactsById.get(contactId);
    if (!k)
        return;
    editingContactId = contactId;
    $('#contactFormTitle').textContent = `Edit contact #${contactId}`;
    setV('#kLast', k.lastName);
    setV('#kFirst', k.firstName);
    setV('#kDept', k.department);
    setV('#kPhone', k.phone1);
    setV('#kEmail', k.email1);
    setV('#kCity', k.city);
    setV('#kState', k.state);
}
function newContact() {
    editingContactId = null;
    $('#contactFormTitle').textContent = 'New contact';
    ['#kLast', '#kFirst', '#kDept', '#kPhone', '#kEmail', '#kCity', '#kState'].forEach((id) => setV(id, ''));
}
async function saveContact() {
    if (editingCustomerId == null) {
        setErr('Save the customer before adding contacts.');
        return;
    }
    setErr('');
    setOk('');
    setBusy(true);
    const body = new CustomerContactWrite({
        lastName: v('#kLast') || undefined, firstName: v('#kFirst') || undefined, department: v('#kDept') || undefined,
        phone1: v('#kPhone') || undefined, email1: v('#kEmail') || undefined, city: v('#kCity') || undefined, state: v('#kState') || undefined,
    });
    try {
        if (editingContactId == null) {
            await client().createCustomerContact(editingCustomerId, body);
            setOk('✓ Added contact.');
        }
        else {
            await client().updateCustomerContact(editingContactId, body);
            setOk(`✓ Saved contact #${editingContactId}.`);
        }
        await loadContacts(editingCustomerId);
        newContact();
    }
    catch (e) {
        setErr(`Contact save failed: ${e.message}`);
    }
    finally {
        setBusy(false);
    }
}
(async () => {
    const main = await initShell({ active: 'customers' });
    main.innerHTML = scaffold();
    $('#searchForm').addEventListener('submit', (e) => { e.preventDefault(); void search(); });
    $('#btnNew').addEventListener('click', newCustomer);
    $('#btnSave').addEventListener('click', () => void saveCustomer());
    $('#btnContactSave').addEventListener('click', () => void saveContact());
    $('#btnContactNew').addEventListener('click', newContact);
    newCustomer();
    await search();
})();
